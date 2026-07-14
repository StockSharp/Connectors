namespace StockSharp.Bitmart.Native.Spot;

using StockSharp.Bitmart.Native.Spot.Model;

class SpotAdapter : BaseNativeAdapter
{
	private HttpClient _httpClient;
	private PublicPusherClient _publicClient;
	private PrivatePusherClient _privateClient;
	private readonly IdGenerator _transIdGen;
	private readonly Authenticator _authenticator;
	private readonly ILogReceiver _logs;
	private readonly string _publicWsAddress;
	private readonly string _privateWsAddress;

	private readonly SynchronizedDictionary<(SecurityId secId, TimeSpan tf), long> _candleTransactions = [];
	private const int _maxCandlesCount = 500;
	private const int _defaultDepth = 5;

	private readonly SynchronizedDictionary<string, long> _subscribedOrdersSymbols = new(StringComparer.InvariantCultureIgnoreCase);
	private const int _maxOrdersCount = 200;
	private readonly string _portfolioName;

	public SpotAdapter(IdGenerator transIdGen, Authenticator authenticator, ILogReceiver logs, string publicWsAddress, string privateWsAddress)
    {
		_transIdGen = transIdGen ?? throw new ArgumentNullException(nameof(transIdGen));
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
		_logs = logs ?? throw new ArgumentNullException(nameof(logs));
		_publicWsAddress = publicWsAddress.ThrowIfEmpty(nameof(publicWsAddress));
		_privateWsAddress = privateWsAddress.ThrowIfEmpty(nameof(privateWsAddress));
		_portfolioName = $"{nameof(Bitmart)}_{nameof(Spot)}_{_authenticator.Key.ToId()}";
	}

	public override async ValueTask ConnectAsync(string address, bool isMarketData, bool isTransactional, int attemptsCount, WorkingTime workingTime, CancellationToken cancellationToken)
	{
		if (_httpClient is not null || _publicClient is not null || _privateClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new(address, _authenticator) { Parent = _logs };
		_publicClient = isMarketData ? new(_publicWsAddress, attemptsCount, workingTime) { Parent = _logs } : null;
		_privateClient = isTransactional ? new(_privateWsAddress, _authenticator, attemptsCount, workingTime) { Parent = _logs } : null;

		if (_publicClient is null && _privateClient is null)
		{
			await SendOutMessageAsync(new ConnectMessage(), cancellationToken);
		}
		else
		{
			SubscribePusher((PusherClient)_privateClient ?? _publicClient);

			if (_publicClient is not null)
				await _publicClient.Connect(cancellationToken);

			if (_privateClient is not null)
				await _privateClient.Connect(cancellationToken);
		}
	}

	public override async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		if (_httpClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_httpClient.Dispose();
		_httpClient = null;

		if (_publicClient is null && _privateClient is null)
		{
			await SendOutMessageAsync(new DisconnectMessage(), cancellationToken);
		}
		else
		{
			_publicClient?.Disconnect();
			_privateClient?.Disconnect();
		}
	}

	public override async ValueTask ResetAsync(CancellationToken cancellationToken)
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

		var control = (PusherClient)_privateClient ?? _publicClient;
		if (control is not null)
			UnsubscribePusher(control);

		try
		{
			_publicClient?.Disconnect();
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}

		_publicClient = null;

		try
		{
			_privateClient?.Disconnect();
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}

		_privateClient = null;

		_candleTransactions.Clear();
		_subscribedOrdersSymbols.Clear();

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	public override async ValueTask TimeAsync(CancellationToken cancellationToken)
	{
		if (_publicClient is not null)
			await _publicClient.Ping(cancellationToken);

		if (_privateClient is not null)
			await _privateClient.Ping(cancellationToken);
	}

	public override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var symbols = await _httpClient.GetSymbolsDetails(cancellationToken);
		var left = lookupMsg.Count ?? long.MaxValue;

		var secTypes = lookupMsg.GetSecurityTypes();

		foreach (var sym in symbols)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var pmp = sym.PriceMaxPrecision < 0 ? (int?)null : sym.PriceMaxPrecision;

			var secMsg = new SecurityMessage
			{
				SecurityId = sym.Symbol.ToStockSharp(),
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityType = SecurityTypes.CryptoCurrency,
				PriceStep = pmp?.GetPriceStep(),
				Decimals = pmp,
				MinVolume = sym.MinBuyAmount?.ToDecimal(),
				VolumeStep = sym.QuoteIncrement?.ToDecimal(),
			};

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	public override async ValueTask Level1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToNativeSymbol();

		if (mdMsg.IsSubscribe)
		{
			await PublicClient.SubscribeLevel1(mdMsg.TransactionId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await PublicClient.UnsubscribeLevel1(mdMsg.OriginalTransactionId, symbol, cancellationToken);
	}

	public override async ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToNativeSymbol();

		var depth = mdMsg.MaxDepth ?? _defaultDepth;

		if (mdMsg.IsSubscribe)
		{
			await PublicClient.SubscribeDepth(mdMsg.TransactionId, symbol, depth, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await PublicClient.UnsubscribeDepth(mdMsg.OriginalTransactionId, symbol, depth, cancellationToken);
	}

	public override async ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToNativeSymbol();

		if (mdMsg.IsSubscribe)
		{
			await PublicClient.SubscribeTicks(mdMsg.TransactionId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await PublicClient.UnsubscribeTicks(mdMsg.OriginalTransactionId, symbol, cancellationToken);
	}

	public override async ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToNativeSymbol();

		var tf = mdMsg.GetTimeFrame();
		var key = (mdMsg.SecurityId, tf);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is not null)
			{
				var from = mdMsg.From.Value;
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;
				var step = tf.ToNative(false).To<int>();

				while (true)
				{
					var candles = await _httpClient.GetCandles(symbol, step, from, null, (int)left.Min(_maxCandlesCount), cancellationToken);

					var noData = true;

					foreach (var candle in candles.OrderBy(c => c.Time))
					{
						cancellationToken.ThrowIfCancellationRequested();

						var time = candle.Time.FromUnix();

						if (time < from)
							continue;

						if (time > to)
						{
							noData = true;
							break;
						}

						noData = false;
						await ProcessCandleAsync(time, candle, mdMsg.SecurityId, tf, mdMsg.TransactionId, cancellationToken);

						if (--left <= 0)
							break;

						from = time;
					}

					if (noData || left <= 0)
						break;
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_candleTransactions[key] = mdMsg.TransactionId;
				await PublicClient.SubscribeCandles(mdMsg.TransactionId, symbol, tf, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_candleTransactions.Remove(key);
			await PublicClient.UnsubscribeCandles(mdMsg.OriginalTransactionId, symbol, tf, cancellationToken);
		}
	}

	private ValueTask TrySubscribeOrders(string symbol, CancellationToken cancellationToken)
	{
		var transId = _subscribedOrdersSymbols.SafeAdd(symbol, k => _transIdGen.GetNextId(), out var isNew);

		if (!isNew)
			return default;

		return PrivateClient.SubscribeOrders(transId, symbol, cancellationToken);
	}

	public override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var symbol = regMsg.SecurityId.ToNativeSymbol();
		var condition = (BitmartOrderCondition)regMsg.Condition;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (!condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				var withdrawId = await _httpClient.Withdraw(symbol, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderId = withdrawId,
					ServerTime = DateTime.UtcNow,
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

		await TrySubscribeOrders(symbol, cancellationToken);

		var orderId = await _httpClient.SubmitOrder(symbol, ToNative(regMsg.Side), ToNative(regMsg.OrderType, regMsg.PostOnly, regMsg.TimeInForce), regMsg.Volume, price, regMsg.TransactionId, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			SecurityId = regMsg.SecurityId,
			DataTypeEx = DataType.Transactions,
			OrderId = orderId,
			ServerTime = DateTime.UtcNow,
			OriginalTransactionId = regMsg.TransactionId,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			HasOrderInfo = true,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var symbol = cancelMsg.SecurityId.ToNativeSymbol();

		if (!await _httpClient.CancelOrder(symbol, cancelMsg.OrderId, cancelMsg.OriginalTransactionId, cancellationToken))
			throw new InvalidOperationException("CancelOrder: server returned false. probably order was already canceled/executed");
	}

	public override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.IsAllSecurity())
			throw new InvalidOperationException("security id must be specified for group cancel");

		var symbol = cancelMsg.SecurityId.ToNativeSymbol();

		await _httpClient.CancelOrders(symbol, cancelMsg.Side is Sides side ? ToNative(side) : null, cancellationToken);
	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		var client = PrivateClient;

		if (!statusMsg.IsSubscribe)
		{
			foreach (var (symbol, transId) in _subscribedOrdersSymbols.CopyAndClear())
				await client.UnsubscribeOrders(transId, symbol, cancellationToken);

			return;
		}

		var orders = await _httpClient.GetOpenOrders(null, _maxOrdersCount, cancellationToken);

		foreach (var order in orders)
		{
			if (!long.TryParse(order.ClientOrderId, out var transId))
				continue;

			var (type, postOnly, tif) = ToOrderType(order.Type);

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				ServerTime = order.CreateTime,
				SecurityId = order.Symbol.ToStockSharp(),
				TransactionId = transId,
				OriginalTransactionId = statusMsg.TransactionId,
				OrderVolume = (decimal?)order.Size,
				Balance = (decimal?)(order.Size - order.FilledSize),
				Side = ToSide(order.Side) ?? Sides.Buy,
				OrderType = type,
				TimeInForce = tif,
				PostOnly = postOnly,
				OrderPrice = (decimal)(order.Price ?? 0),
				PortfolioName = _portfolioName,
				OrderState = OrderStates.Active,
				AveragePrice = (decimal?)order.PriceAvg,
			}, cancellationToken);
		}

		if (!statusMsg.IsHistoryOnly())
		{
			foreach (var symbol in orders.Select(o => o.Symbol))
				await TrySubscribeOrders(symbol, cancellationToken);
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	public override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var client = PrivateClient;

		if (!lookupMsg.IsSubscribe)
		{
			await client.UnsubscribeBalance(lookupMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.Bitmart,
			OriginalTransactionId = lookupMsg.TransactionId
		}, cancellationToken);

		foreach (var balance in await _httpClient.GetBalances(cancellationToken))
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = balance.Currency.ToStockSharp(),
				PortfolioName = _portfolioName,
				ServerTime = DateTime.UtcNow,
				OriginalTransactionId = lookupMsg.TransactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Available?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, balance.Frozen?.ToDecimal(), true), cancellationToken);
		}

		if (!lookupMsg.IsHistoryOnly())
			await client.SubscribeBalance(lookupMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private PublicPusherClient PublicClient
		=> _publicClient ?? throw new InvalidOperationException("public socket is null");

	private PrivatePusherClient PrivateClient
		=> _privateClient ?? throw new InvalidOperationException("private socket is null");

	#region subscribe pushers

	private void SubscribePusher(PusherClient control)
	{
		if (control is null)
			throw new ArgumentNullException(nameof(control));

		control.StateChanged					+= SendOutConnectionStateAsync;

		if (_publicClient is PublicPusherClient publicClient)
		{
			publicClient.Error					+= SessionOnPusherErrorAsync;
			publicClient.TickersReceived		+= SessionOnTickersReceived;
			publicClient.OrderBooksReceived		+= SessionOnOrderBooksReceived;
			publicClient.TicksReceived			+= SessionOnTicksReceived;
			publicClient.CandlesReceived		+= SessionOnCandlesReceived;
		}

		if (_privateClient is PrivatePusherClient privateClient)
		{
			privateClient.Error					+= SessionOnPusherErrorAsync;
			privateClient.OrdersReceived		+= SessionOnOrdersReceived;
			privateClient.BalancesReceived		+= SessionOnBalancesReceived;
		}
	}

	private void UnsubscribePusher(PusherClient control)
	{
		if (control is null)
			throw new ArgumentNullException(nameof(control));

		control.StateChanged					-= SendOutConnectionStateAsync;

		var publicClient = PublicClient;

		if (publicClient is not null)
		{
			publicClient.Error					-= SessionOnPusherErrorAsync;
			publicClient.TickersReceived		-= SessionOnTickersReceived;
			publicClient.OrderBooksReceived		-= SessionOnOrderBooksReceived;
			publicClient.TicksReceived			-= SessionOnTicksReceived;
			publicClient.CandlesReceived		-= SessionOnCandlesReceived;
		}

		var privateClient = PrivateClient;

		if (privateClient is not null)
		{
			privateClient.Error					-= SessionOnPusherErrorAsync;
			privateClient.OrdersReceived		-= SessionOnOrdersReceived;
			privateClient.BalancesReceived		-= SessionOnBalancesReceived;
		}
	}

	#endregion

	private ValueTask ProcessCandleAsync(DateTime time, Ohlc candle, SecurityId securityId, TimeSpan timeFrame, long originTransId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = securityId,
			TypedArg = timeFrame,
			OpenPrice = (decimal)candle.Open,
			ClosePrice = (decimal)candle.Close,
			HighPrice = (decimal)candle.High,
			LowPrice = (decimal)candle.Low,
			TotalVolume = (decimal)candle.Volume,
			OpenTime = time,
			State = CandleStates.Active,
			OriginalTransactionId = originTransId
		}, cancellationToken);
	}

	private async ValueTask SessionOnTickersReceived(IEnumerable<Ticker> tickers, CancellationToken cancellationToken)
	{
		foreach (var ticker in tickers)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = ticker.Symbol.ToStockSharp(),
				ServerTime = ticker.Timestamp,
			}
			.TryAdd(Level1Fields.OpenPrice, ticker.Open24h?.ToDecimal())
			.TryAdd(Level1Fields.HighPrice, ticker.High24h?.ToDecimal())
			.TryAdd(Level1Fields.LowPrice, ticker.Low24h?.ToDecimal())
			.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice?.ToDecimal())
			.TryAdd(Level1Fields.Volume, ticker.BaseVolume24h?.ToDecimal()), cancellationToken);
		}
	}

	private async ValueTask SessionOnTicksReceived(IEnumerable<Tick> ticks, CancellationToken cancellationToken)
	{
		foreach (var tick in ticks)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = tick.Symbol.ToStockSharp(),
				TradePrice = (decimal)tick.Price,
				TradeVolume = (decimal)tick.Size,
				ServerTime = tick.Time,
				OriginSide = ToSide(tick.Side),
			}, cancellationToken);
		}
	}

	private async ValueTask SessionOnOrderBooksReceived(IEnumerable<OrderBook> books, CancellationToken cancellationToken)
	{
		foreach (var book in books)
		{
			var secId = book.Symbol.ToStockSharp();
			var state = book.Type.IsEmpty() ? (QuoteChangeStates?)null : (book.Type == "update" ? QuoteChangeStates.Increment : QuoteChangeStates.SnapshotComplete);

			static QuoteChange ToChange(OrderBookEntry entry)
				=> new((decimal)entry.Price, (decimal)entry.Size);

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = secId,
				Bids = book.Bids?.Select(ToChange).ToArray() ?? Array.Empty<QuoteChange>(),
				Asks = book.Asks?.Select(ToChange).ToArray() ?? Array.Empty<QuoteChange>(),
				State = state,
				ServerTime = book.Timestamp,
			}, cancellationToken);
		}
	}

	private async ValueTask SessionOnCandlesReceived(TimeSpan timeFrame, IEnumerable<OhlcData> values, CancellationToken cancellationToken)
	{
		foreach (var value in values)
		{
			var secId = value.Symbol.ToStockSharp();
			var candle = value.Candle;

			if (_candleTransactions.TryGetValue((secId, timeFrame), out var transId))
				await ProcessCandleAsync(candle.Time.FromUnix(), candle, secId, timeFrame, transId, cancellationToken);
		}
	}

	private async ValueTask SessionOnOrdersReceived(IEnumerable<SocketOrder> orders, CancellationToken cancellationToken)
	{
		foreach (var order in orders)
		{
			if (!long.TryParse(order.ClientOrderId, out var origTransId))
				continue;

			var (type, postOnly, tif) = ToOrderType(order.Type);

			var execMsg = new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				ServerTime = order.LastFillTime ?? order.Timestamp,
				SecurityId = order.Symbol.ToStockSharp(),
				OriginalTransactionId = origTransId,
				OrderVolume = (decimal?)order.Size,
				Balance = (decimal?)(order.Size - order.FilledSize),
				Side = ToSide(order.Side) ?? Sides.Buy,
				OrderType = type,
				TimeInForce = tif,
				PostOnly = postOnly,
				OrderPrice = (decimal)(order.Price ?? 0),
				PortfolioName = _portfolioName,
				OrderState = ToOrderState(order.State),
			};

			if (order.LastFillPrice is not null)
			{
				execMsg.TradeId = order.DetailId;
				execMsg.TradePrice = (decimal)order.LastFillPrice.Value;
				execMsg.TradeVolume = (decimal?)order.LastFillSize;
				execMsg.IsMarketMaker = ToMaker(order.IsMakerOrTaker);
			}

			await SendOutMessageAsync(execMsg, cancellationToken);
		}
	}

	private async ValueTask SessionOnBalancesReceived(IEnumerable<SocketBalanceData> datas, CancellationToken cancellationToken)
	{
		foreach (var data in datas)
		{
			foreach (var balance in data.Balances)
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					SecurityId = balance.Currency.ToStockSharp(),
					PortfolioName = _portfolioName,
					ServerTime = data.EventTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, balance.Available?.ToDecimal(), true)
				.TryAdd(PositionChangeTypes.BlockedValue, balance.Frozen?.ToDecimal(), true), cancellationToken);
			}
		}
	}

	private static string ToNative(Sides side)
		=> side switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue)
		};

	private static Sides? ToSide(string side)
		=> side?.ToLowerInvariant() switch
		{
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			null or "" => null,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue)
		};

	private static string ToNative(OrderTypes? type, bool? postOnly, TimeInForce? tif)
	{
		if (postOnly == true)
			return "limit_maker";
		else if (tif == TimeInForce.MatchOrCancel)
			return "ioc";
		else if (type == OrderTypes.Market)
			return "market";
		else
			return "limit";
	}

	private static (OrderTypes type, bool? postOnly, TimeInForce? tif) ToOrderType(string ordType)
		=> ordType?.ToLowerInvariant() switch
		{
			"limit" => (OrderTypes.Limit, null, null),
			"market" => (OrderTypes.Market, null, null),
			"limit_maker" => (OrderTypes.Limit, true, null),
			"ioc" => (OrderTypes.Limit, null, TimeInForce.MatchOrCancel),
			_ => throw new ArgumentOutOfRangeException(nameof(ordType), ordType, LocalizedStrings.InvalidValue)
		};

	private static OrderStates ToOrderState(int status)
	{
		switch (status)
		{
			case 4: // Order success, Pending for fulfilment
			case 5: // Partially filled
				return OrderStates.Active;

			case 6: // Fully filled
			case 8: // Canceled
			case 12: // Canceled after Partially filled
				return OrderStates.Done;

			default:
				throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue);
		}
	}

	private static bool? ToMaker(string maker) =>
		maker?.ToUpperInvariant() switch
		{
			null or "" => null,
			"M" => true,
			"T" => false,
			_ => throw new ArgumentOutOfRangeException(nameof(maker), maker, LocalizedStrings.InvalidValue)
		};
}
