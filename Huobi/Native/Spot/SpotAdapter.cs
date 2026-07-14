namespace StockSharp.Huobi.Native.Spot;

using StockSharp.Huobi.Native.Spot.Model;

class SpotAdapter(HuobiMessageAdapter parent, Authenticator authenticator, string domain) : NativeAdapter(parent, authenticator, domain)
{
	private class OrderBookInfo(HuobiMessageAdapter parent, SecurityId secId, int depth)
	{
		private readonly struct BookInfo(DateTime time, OrderBook book)
		{
			public DateTime Time { get; } = time;
			public OrderBook Book { get; } = book;

			public class SeqNumComparer : IComparer<BookInfo>
			{
				public int Compare(BookInfo x, BookInfo y) => x.Book.SeqNum.CompareTo(y.Book.SeqNum);
			}
		}

		private readonly HuobiMessageAdapter _parent = parent;
		private readonly SecurityId _secId = secId;
		private readonly int _depth = depth;
		private readonly List<BookInfo> _suspendedIncrement = [];

		private bool _isSuspended;
		private DateTime _lastSuspendTime;
		private long _seqNum;

		QuoteChangeMessage ToMessage(BookInfo info, QuoteChangeStates state) => new()
		{
			SecurityId = _secId,
			Bids = info.Book.Bids?.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray() ?? [],
			Asks = info.Book.Asks?.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray() ?? [],
			ServerTime = info.Time,
			State = state,
		};

		private void ClearOldSuspended()
		{
			if (_suspendedIncrement.IsEmpty())
				return;

			var idx = _suspendedIncrement.IndexOf(i => i.Book.SeqNum > _seqNum);

			if (idx < 0)
				_suspendedIncrement.Clear();
			else
				_suspendedIncrement.RemoveRange(0, idx);
		}

		private void InsertSuspended(BookInfo info)
		{
			if (_suspendedIncrement.Count > 3000)
				return;

			var idx = _suspendedIncrement.BinarySearch(info, new BookInfo.SeqNumComparer());

			if (idx < 0)
				_suspendedIncrement.Insert(~idx, info);
		}

		private IEnumerable<Message> TrySuspend()
		{
			var now = DateTime.UtcNow;

			if (_isSuspended || now - _lastSuspendTime < TimeSpan.FromMilliseconds(300))
				yield break;

			_isSuspended = true;
			_lastSuspendTime = now;
			_parent.AddDebugLog("suspending MBP {0} ({1})", _secId, _depth);
			yield return new ProcessSuspendedMessage(_parent, Tuple.Create(_secId, _depth));
		}

		private void TryUnsuspend()
		{
			if (_isSuspended)
			{
				_parent.AddDebugLog("unsuspending MBP {0} ({1})", _secId, _depth);
				_isSuspended = false;
			}
		}

		private IEnumerable<Message> ProcessSuspended()
		{
			ClearOldSuspended();

			foreach (var info in _suspendedIncrement)
			{
				if (info.Book.PrevSeqNum != _seqNum)
					break;

				_seqNum = info.Book.SeqNum;
				yield return ToMessage(info, QuoteChangeStates.Increment);
			}

			ClearOldSuspended();

			if (!_suspendedIncrement.Any())
				yield break;

			foreach (var m in TrySuspend())
				yield return m;
		}

		public IEnumerable<Message> Process(OrderBook book, DateTime timestamp, bool snapshot)
			=> Process(new BookInfo(timestamp, book), snapshot);

		private IEnumerable<Message> Process(BookInfo info, bool snapshot)
		{
			if (snapshot)
			{
				TryUnsuspend();
				if (_seqNum < info.Book.SeqNum)
				{
					_seqNum = info.Book.SeqNum;
					yield return ToMessage(info, QuoteChangeStates.SnapshotComplete);
				}

				foreach (var m in ProcessSuspended())
					yield return m;

				yield break;
			}

			if (_seqNum >= info.Book.SeqNum)
				yield break;

			if (info.Book.PrevSeqNum == _seqNum)
			{
				_seqNum = info.Book.SeqNum;
				TryUnsuspend();
				yield return ToMessage(info, QuoteChangeStates.Increment);
				yield break;
			}

			InsertSuspended(info);

			if (_isSuspended)
				yield break;

			foreach (var m in TrySuspend())
				yield return m;
		}
	}

	private readonly SynchronizedDictionary<(SecurityId securityId, int depth), OrderBookInfo> _spotOrderBooks = [];
	private readonly SynchronizedDictionary<string, string> _accountTypes = [];

	private HttpClient _httpClient;
	private PusherClient _pusherClient;

    private void SubscribePusherClient()
	{
		_pusherClient.StateChanged += SendOutConnectionStateAsync;
		_pusherClient.Error += SessionOnPusherError;
		_pusherClient.Ping += SessionOnPing;
		_pusherClient.SubscriptionResponse += SessionOnSubscriptionResponse;

		_pusherClient.TickerChanged += SessionOnTickerChanged;
		_pusherClient.BestChanged += SessionOnBestChanged;
		_pusherClient.OrderBookChanged += SessionOnOrderBookChanged;
		_pusherClient.NewTrades += SessionOnNewTrades;
		_pusherClient.NewCandle += SessionOnNewCandle;
		_pusherClient.CandlesReceived += SessionOnCandlesReceived;
		_pusherClient.OrderChanged += SessionOnOrderChanged;
		_pusherClient.BalanceChanged += SessionOnBalanceChanged;
	}

	private void UnsubscribePusherClient()
	{
		_pusherClient.StateChanged -= SendOutConnectionStateAsync;
		_pusherClient.Error -= SessionOnPusherError;
		_pusherClient.Ping -= SessionOnPing;
		_pusherClient.SubscriptionResponse -= SessionOnSubscriptionResponse;

		_pusherClient.TickerChanged -= SessionOnTickerChanged;
		_pusherClient.BestChanged -= SessionOnBestChanged;
		_pusherClient.OrderBookChanged -= SessionOnOrderBookChanged;
		_pusherClient.NewTrades -= SessionOnNewTrades;
		_pusherClient.NewCandle -= SessionOnNewCandle;
		_pusherClient.CandlesReceived -= SessionOnCandlesReceived;
		_pusherClient.OrderChanged -= SessionOnOrderChanged;
		_pusherClient.BalanceChanged -= SessionOnBalanceChanged;
	}

	private static string AccountTypeToSource(string type)
	{
		return type switch
		{
			null or "spot" => "api",
			"margin" => "margin-api",

			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	private static OrderStates ToOrderState(string status)
	{
		return status switch
		{
			"rejected" => OrderStates.Failed,
			"pre-submitted" => OrderStates.Pending,

			"submitted" or "partial-filled" or "partial-canceled" or "cancelling"
				=> OrderStates.Active,

			"filled" or "canceled" => OrderStates.Done,

			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
	}

	protected override async ValueTask OnResetAsync(CancellationToken cancellationToken)
	{
		if (_httpClient != null)
		{
			try
			{
				_httpClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_httpClient = null;
		}

		if (_pusherClient != null)
		{
			try
			{
				UnsubscribePusherClient();
				_pusherClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_pusherClient = null;
		}

		_spotOrderBooks.Clear();
	}

	public override async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_pusherClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new HttpClient(Authenticator, Domain) { Parent = Parent };
		_pusherClient = new PusherClient(Authenticator, Domain, Parent.ReConnectionSettings.WorkingTime) { Parent = Parent };

		SubscribePusherClient();
		await _pusherClient.ConnectAsync(Parent.IsMarketData(), Parent.IsTransactional(), cancellationToken);
	}

	public override ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_pusherClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_httpClient.Dispose();
		_httpClient = null;

		_pusherClient.Disconnect();
		return default;
	}

	public override async ValueTask SuspendedAsync(ProcessSuspendedMessage message, CancellationToken cancellationToken)
	{
		var tuple = (Tuple<SecurityId, int>)message.Arg;
		await _pusherClient.RequestOrderBook(tuple.Item1, tuple.Item2, AddExtraRequest(), cancellationToken);
	}

	public override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (!timeMsg.OriginalTransactionId.IsEmpty())
			await _pusherClient.Pong(timeMsg.OriginalTransactionId, cancellationToken);
	}

	public override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var secTypes = lookupMsg.GetSecurityTypes();

		foreach (var symbol in await _httpClient.GetSymbolsAsync(cancellationToken))
		{
			var secMsg = new SecurityMessage
			{
				SecurityId = $"{symbol.BaseCurrency}/{symbol.QuoteCurrency}".ToStockSharp(true),
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityType = SecurityTypes.CryptoCurrency,
				PriceStep = symbol.PricePrecision.GetPriceStep(),
				VolumeStep = symbol.AmountPrecision.GetPriceStep(),
			}.TryFillUnderlyingId(symbol.BaseCurrency.ToUpperInvariant());

			if (secMsg.IsMatch(lookupMsg, secTypes))
				await SendOutMessageAsync(secMsg, cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	public override async ValueTask MarketDataAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var securityId = mdMsg.SecurityId;

		const int maxCount = 2000;

		if (mdMsg.DataType2 == DataType.Level1)
		{
			await SendSubscriptionReplyAsync(transId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				await _pusherClient.SubscribeTicker(securityId, transId, cancellationToken);
				await _pusherClient.SubscribeBest(securityId, AddExtraRequest(), cancellationToken);

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
			else
			{
				await _pusherClient.UnSubscribeTicker(securityId, transId, cancellationToken);
				await _pusherClient.UnSubscribeBest(securityId, AddExtraRequest(), cancellationToken);
			}
		}
		else if (mdMsg.DataType2 == DataType.MarketDepth)
		{
			await SendSubscriptionReplyAsync(transId, cancellationToken);

			var depth = mdMsg.MaxDepth ?? 20;

			if (mdMsg.IsSubscribe)
			{
				await _pusherClient.SubscribeOrderBook(securityId, depth, transId, cancellationToken);

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
			else
				await _pusherClient.UnSubscribeOrderBook(securityId, depth, transId, cancellationToken);
		}
		else if (mdMsg.DataType2 == DataType.Ticks)
		{
			await SendSubscriptionReplyAsync(transId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				if (mdMsg.To != null)
				{
					var trades = await _httpClient.GetTradesAsync(securityId.ToSymbol(), (int?)mdMsg.Count ?? maxCount, cancellationToken);

					foreach (var tick in trades.OrderBy(t => t.Timestamp))
					{
						await ProcessTickAsync(securityId, tick, tick.TradeId, transId, cancellationToken);
					}
				}
				else
					await _pusherClient.SubscribeTrades(securityId, transId, cancellationToken);

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
			else
				await _pusherClient.UnSubscribeTrades(securityId, transId, cancellationToken);
		}
		else if (mdMsg.DataType2.IsTFCandles)
		{
			await SendSubscriptionReplyAsync(transId, cancellationToken);

			var tf = mdMsg.GetTimeFrame();
			var tfName = tf.ToNative();

			if (mdMsg.IsSubscribe)
			{
				if (mdMsg.To != null)
					await _pusherClient.RequestCandles(securityId, tf, transId, (long?)mdMsg.From?.ToUnix(), (long?)mdMsg.To?.ToUnix(), cancellationToken);
				else
				{
					await _pusherClient.SubscribeCandles(securityId, tf, transId, cancellationToken);

					await SendSubscriptionResultAsync(mdMsg, cancellationToken);
				}
			}
			else
				await _pusherClient.UnSubscribeCandles(securityId, tf, transId, cancellationToken);
		}
		else
		{
			await SendSubscriptionNotSupportedAsync(transId, cancellationToken);
		}
	}

	public override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var symbol = regMsg.SecurityId.ToSymbol();
		var condition = (HuobiOrderCondition)regMsg.Condition;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (!condition.IsWithdraw)
				{
					break;
					//throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
				}

				var withdrawId = await _httpClient.WithdrawAsync(symbol, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderId = withdrawId,
					ServerTime = CurrentTime,
					OriginalTransactionId = regMsg.TransactionId,
					OrderState = OrderStates.Done,
					HasOrderInfo = true,
				}, cancellationToken);

				return;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = isMarket ? (decimal?)null : regMsg.Price;

		await _httpClient.RegisterOrderAsync(regMsg.TransactionId, DecodeAccountId(regMsg.PortfolioName), symbol, regMsg.OrderType.ToNative(regMsg.Side, regMsg.TimeInForce, regMsg.IsMarketMaker), AccountTypeToSource(_accountTypes.TryGetValue(regMsg.PortfolioName)), price, regMsg.Volume, condition?.StopPrice, condition?.IsGreaterThan.ToStopOperator(), cancellationToken);
	}

	public override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (IsAlgo(cancelMsg.OriginalTransactionId))
			await _httpClient.CancelAlgoOrderAsync(cancelMsg.OriginalTransactionId, cancellationToken);
		else
			await _httpClient.CancelOrderAsync(cancelMsg.OriginalTransactionId, cancellationToken);
	}

	public override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		await _httpClient.BatchCancelOrderAsync(DecodeAccountId(cancelMsg.PortfolioName), cancelMsg.SecurityId == default ? null : cancelMsg.SecurityId.ToSymbol(), cancelMsg.Side?.ToNative(), cancellationToken);
	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		if (!message.IsSubscribe)
		{
			await _pusherClient.UnSubscribeOrders(message.TransactionId, cancellationToken);
		}

		var orders = await _httpClient.GetOpenOrdersAsync(size: 500, cancellationToken: cancellationToken);

		foreach (var order in orders)
		{
			var transId = order.ClientOrderId.TryToLong() ?? Parent.TransactionIdGenerator.GetNextId();
			var tradeIds = new HashSet<long>();
			var volume = (decimal?)order.Amount;
			var balance = (decimal)(order.Amount - (order.FieldAmount ?? 0));

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				ServerTime = order.CreatedAt,
				SecurityId = order.Symbol.ToStockSharp(),
				TransactionId = transId,
				OriginalTransactionId = message.TransactionId,
				OrderId = order.Id,
				OrderVolume = volume,
				Balance = balance,
				OrderType = order.Type.ToOrderType(out var side, out var tif, out var isMaker),
				Side = side,
				TimeInForce = tif,
				IsMarketMaker = isMaker,
				OrderPrice = (decimal?)order.Price ?? 0,
				PortfolioName = EncodeAccountId(order.AccountId),
				OrderState = ToOrderState(order.State),
				Commission = (decimal?)order.FieldFees,
				Condition = order.StopPrice == null ? null : new HuobiOrderCondition
				{
					StopPrice = (decimal?)order.StopPrice,
					IsGreaterThan = order.Operator.ToStopOperator(),
				},
			}, cancellationToken);

			if (volume != balance)
			{
				var trades = await _httpClient.GetOrderMatchesAsync(order.Id, cancellationToken);
				await ProcessOwnTradesAsync(tradeIds, trades, transId, cancellationToken);
			}
		}

		var algoOrders = await _httpClient.GetOpenAlgoOrdersAsync(size: 500, cancellationToken: cancellationToken);

		foreach (var order in algoOrders)
		{
			var transId = order.ClientOrderId.TryToLong() ?? Parent.TransactionIdGenerator.GetNextId();

			AddAlgo(transId);

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				ServerTime = order.OrigTime ?? CurrentTime,
				SecurityId = order.Symbol.ToStockSharp(),
				TransactionId = transId,
				OriginalTransactionId = message.TransactionId,
				OrderVolume = (decimal?)order.Size,
				Balance = (decimal?)order.Size,
				OrderType = order.Type.ToOrderType(),
				Side = order.Side.ToSide(),
				TimeInForce = order.TimeInForce.ToTimeInForce(),
				PortfolioName = EncodeAccountId(order.AccountId),
				OrderState = ToOrderState(order.OrderStatus),
				Condition = new HuobiOrderCondition { StopPrice = (decimal?)order.StopPrice },
			}, cancellationToken);
		}

		if (message.To == null)
			await _pusherClient.SubscribeOrders(message.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	public override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		if (!message.IsSubscribe)
		{
			await _pusherClient.UnSubscribeAccounts(message.TransactionId, cancellationToken);
		}

		var accounts = await _httpClient.GetAccountsAsync(cancellationToken);

		foreach (var account in accounts)
		{
			var pfName = EncodeAccountId(account.Id);

			_accountTypes[pfName] = account.Type;

			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = pfName,
				BoardCode = BoardCodes.Huobi,
				OriginalTransactionId = message.TransactionId
			}, cancellationToken);

			var balance = await _httpClient.GetAccountAsync(account.Id, cancellationToken);

			foreach (var group in balance.Balances.GroupBy(e => e.Currency.ToLowerInvariant()))
			{
				var dict = group.ToDictionary(e => e.Type, e => e.Value, StringComparer.InvariantCultureIgnoreCase);

				var current = dict.TryGetValue("trade");
				var frozen = dict.TryGetValue("frozen");

				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = pfName,
					SecurityId = group.Key.ToStockSharp(true),
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)current, true)
				.TryAdd(PositionChangeTypes.BlockedValue, (decimal?)frozen, true), cancellationToken);
			}
		}

		if (message.To == null)
			await _pusherClient.SubscribeAccounts(message.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private ValueTask ProcessCandleAsync(SecurityId securityId, TimeSpan tf, Ohlc candle, long originalTransactionId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = securityId,
			TypedArg = tf,
			OpenPrice = (decimal?)candle.Open ?? 0,
			ClosePrice = (decimal?)candle.Close ?? 0,
			HighPrice = (decimal?)candle.High ?? 0,
			LowPrice = (decimal?)candle.Low ?? 0,
			TotalVolume = (decimal?)candle.Amount ?? 0,
			OpenTime = candle.OpenTime,
			TotalTicks = candle.Count,
			State = CandleStates.Finished,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask ProcessTickAsync(SecurityId securityId, TradeBase tick, long id, long originalTransactionId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = securityId,

			TradeId = id,

			TradePrice = (decimal?)tick.Price,
			TradeVolume = (decimal?)tick.Amount,
			ServerTime = tick.Timestamp,
			OriginSide = tick.Direction.ToSide(),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(DateTime timestamp, SecurityId securityId, Ticker l1, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = securityId,
			ServerTime = timestamp,
		}
		.TryAdd(Level1Fields.OpenPrice, (decimal?)l1.Open)
		.TryAdd(Level1Fields.HighPrice, (decimal?)l1.High)
		.TryAdd(Level1Fields.LowPrice, (decimal?)l1.Low)
		.TryAdd(Level1Fields.ClosePrice, (decimal?)l1.Close)
		.TryAdd(Level1Fields.Volume, (decimal?)l1.Amount)
		.TryAdd(Level1Fields.TradesCount, l1.Count)
		, cancellationToken);
	}

	private ValueTask SessionOnBestChanged(DateTime timestamp, SecurityId securityId, Best l1, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = securityId,
			ServerTime = l1.QuoteTime,
		}
		.TryAdd(Level1Fields.BestBidPrice, (decimal?)l1.Bid)
		.TryAdd(Level1Fields.BestBidVolume, (decimal?)l1.BidSize)
		.TryAdd(Level1Fields.BestAskPrice, (decimal?)l1.Ask)
		.TryAdd(Level1Fields.BestAskVolume, (decimal?)l1.AskSize)
		, cancellationToken);
	}

	private async ValueTask SessionOnNewTrades(DateTime timestamp, SecurityId securityId, SocketTrade[] trades, CancellationToken cancellationToken)
	{
		foreach (var tick in trades)
		{
			await ProcessTickAsync(securityId, tick, tick.TradeId, 0, cancellationToken);
		}
	}

	private async ValueTask SessionOnOrderBookChanged(DateTime timestamp, SecurityId securityId, int depth, bool snapshot, OrderBook book, CancellationToken cancellationToken)
	{
		var info = _spotOrderBooks.SafeAdd((securityId, depth), p => new OrderBookInfo(Parent, p.securityId, p.depth));

		foreach (var msg in info.Process(book, timestamp, snapshot))
			await SendOutMessageAsync(msg, cancellationToken);
	}

	private ValueTask SessionOnNewCandle(DateTime timestamp, SecurityId securityId, TimeSpan timeFrame, long transactionId, Ohlc candle, CancellationToken cancellationToken)
	{
		return ProcessCandleAsync(securityId, timeFrame, candle, transactionId, cancellationToken);
	}

	private async ValueTask SessionOnCandlesReceived(SecurityId securityId, TimeSpan timeFrame, long transactionId, Ohlc[] candles, CancellationToken cancellationToken)
	{
		foreach (var candle in candles)
		{
			await ProcessCandleAsync(securityId, timeFrame, candle, transactionId, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
	}

	private async ValueTask ProcessOwnTradesAsync(HashSet<long> tradeIds, IEnumerable<OwnTrade> trades, long origTransId, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			if (!tradeIds.Add(trade.MatchId))
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				ServerTime = trade.CreatedAt,
				OriginalTransactionId = origTransId,
				TradeId = trade.MatchId,
				TradePrice = (decimal?)trade.Price,
				TradeVolume = (decimal?)trade.FilledAmount,
				Commission = (decimal?)trade.FilledFees,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnBalanceChanged(SocketBalance balance, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = EncodeAccountId(balance.AccountId),
			SecurityId = balance.Currency.ToStockSharp(true),
			ServerTime = balance.ChangeTime ?? CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)(balance.Balance ?? balance.Available), true), cancellationToken);
	}

	private ValueTask SessionOnOrderChanged(SocketOrder order, CancellationToken cancellationToken)
	{
		var state = ToOrderState(order.OrderStatus);

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.TradeTime ?? order.LastActTime ?? order.CreateTime ?? CurrentTime,
			OriginalTransactionId = order.ClientOrderId.TryToLong() ?? 0,
			OrderId = order.OrderId,
			Balance = (decimal?)order.RemainAmt,
			OrderState = state,
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.ErrMessage) : null,
			TradeId = order.TradeId,
			TradePrice = (decimal?)order.TradePrice,
			TradeVolume = (decimal?)order.TradeVolume,
			Initiator = order.Aggressor,
		}, cancellationToken);
	}
}