namespace StockSharp.Mexc.Native.Spot;

using System.Runtime.CompilerServices;

using StockSharp.Mexc.Native.Spot.Model;

class SpotAdapter : NativeAdapter
{
	private readonly HttpClient _httpClient;
	private readonly SocketClient _socketClient;
	private readonly SynchronizedDictionary<string, long> _candleTransIds = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<string, BookInfo<OrderBookUpdate>> _bookInfos = new(StringComparer.InvariantCultureIgnoreCase);
	private bool _portfolioSubscribed;
	private bool _ordersSubscribed;
	private bool _accountStreamSubscribed;
	private bool _ordersStreamSubscribed;
	private bool _dealsStreamSubscribed;
	private string _listenKey;
	private DateTime _listenKeyExpiresAt;
	private CancellationTokenSource _listenKeyCts;

	public SpotAdapter(MexcMessageAdapter adapter, Authenticator authenticator)
		: base(authenticator, adapter.TransactionIdGenerator, BoardCodes.Mexc, SecurityTypes.CryptoCurrency)
	{
		_httpClient = new(adapter, authenticator) { Parent = this };
		_socketClient = new(adapter, authenticator, adapter.ReConnectionSettings.WorkingTime) { Parent = this };

		SubscribePusherClient();
	}

	protected override void DisposeManaged()
	{
		UnsubscribePusherClient();
		StopListenKeyLoop();
		_socketClient.DisconnectPrivate();

		_httpClient.Dispose();
		_socketClient.Dispose();

		base.DisposeManaged();
	}

	private void SubscribePusherClient()
	{
		_socketClient.StateChanged += SessionOnStateChanged;
		_socketClient.Error += SessionOnPusherError;
		_socketClient.TickerReceived += SessionOnTickerReceived;
		_socketClient.TradeReceived += SessionOnTradeReceived;
		_socketClient.OrderBookReceived += SessionOnOrderBookReceived;
		_socketClient.CandleReceived += SessionOnCandleReceived;
		_socketClient.AccountReceived += SessionOnAccountReceived;
		_socketClient.UserTradeReceived += SessionOnUserTradeReceived;
		_socketClient.OrderReceived += SessionOnPrivateOrderReceived;
	}

	private void UnsubscribePusherClient()
	{
		_socketClient.StateChanged -= SessionOnStateChanged;
		_socketClient.Error -= SessionOnPusherError;
		_socketClient.TickerReceived -= SessionOnTickerReceived;
		_socketClient.TradeReceived -= SessionOnTradeReceived;
		_socketClient.OrderBookReceived -= SessionOnOrderBookReceived;
		_socketClient.CandleReceived -= SessionOnCandleReceived;
		_socketClient.AccountReceived -= SessionOnAccountReceived;
		_socketClient.UserTradeReceived -= SessionOnUserTradeReceived;
		_socketClient.OrderReceived -= SessionOnPrivateOrderReceived;
	}

	private ValueTask SessionOnStateChanged(ConnectionStates state, CancellationToken cancellationToken)
	{
		return SendOutConnectionStateAsync(state, cancellationToken);
	}

	public override ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		_portfolioSubscribed = false;
		_ordersSubscribed = false;
		_accountStreamSubscribed = false;
		_ordersStreamSubscribed = false;
		_dealsStreamSubscribed = false;
		_listenKey = null;
		_listenKeyExpiresAt = default;
		return _socketClient.Connect(cancellationToken);
	}

	public override void Disconnect()
	{
		StopListenKeyLoop();
		_socketClient.Disconnect();
	}

	public override async IAsyncEnumerable<SecurityMessage> SecurityLookup(SecurityLookupMessage lookupMsg, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var exchangeInfo = await _httpClient.GetExchangeInfo(cancellationToken);

		foreach (var symbol in exchangeInfo.Symbols)
		{
			if (symbol.Status != "TRADING")
				continue;

			var lotFilter = symbol.Filters?.FirstOrDefault(f => f.FilterType == "LOT_SIZE");
			var priceFilter = symbol.Filters?.FirstOrDefault(f => f.FilterType == "PRICE_FILTER");
			var notionalFilter = symbol.Filters?.FirstOrDefault(f => f.FilterType == "MIN_NOTIONAL");

			yield return new SecurityMessage
			{
				SecurityId = symbol.SymbolName.ToStockSharp(BoardCode),
				MinVolume = lotFilter?.MinQty?.ToDecimal(),
				MaxVolume = lotFilter?.MaxQty?.ToDecimal(),
				VolumeStep = lotFilter?.StepSize?.ToDecimal(),
				PriceStep = priceFilter?.TickSize?.ToDecimal(),
				OriginalTransactionId = lookupMsg.TransactionId,
				Decimals = symbol.BaseAssetPrecision,
				SecurityType = SecType,
			}.TryFillUnderlyingId(symbol.BaseAsset);
		}
	}

	public override async ValueTask Level1(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeTicker(mdMsg.TransactionId, symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeTicker(mdMsg.OriginalTransactionId, symbol, cancellationToken);
		}
	}

	public override async ValueTask OrderBook(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();
		var depth = mdMsg.MaxDepth ?? 20;

		if (mdMsg.IsSubscribe)
		{
			_bookInfos.Add(symbol, new(depth));
			await _socketClient.SubscribeOrderBook(mdMsg.TransactionId, symbol, depth, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_bookInfos.Remove(symbol);
			await _socketClient.UnsubscribeOrderBook(mdMsg.OriginalTransactionId, symbol, depth, cancellationToken);
		}
	}

	public override async ValueTask Ticks(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeTrades(mdMsg.TransactionId, symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeTrades(mdMsg.OriginalTransactionId, symbol, cancellationToken);
		}
	}

	public override async ValueTask TFCandles(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();
		var tf = mdMsg.GetTimeFrame().ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				const int maxCount = 1000;

				while (from < to)
				{
					var needBreak = true;
					var candles = await _httpClient.GetCandles(symbol, tf, (long)from.ToUnix(false), (long)to.ToUnix(false), maxCount, cancellationToken);

					foreach (var candle in candles.OrderBy(c => c.OpenTime))
					{
						var time = candle.OpenTime;

						if (time <= from)
							continue;

						if (time > to)
						{
							needBreak = true;
							break;
						}

						await SendOutMessageAsync(new TimeFrameCandleMessage
						{
							OpenPrice = (decimal)candle.Open,
							ClosePrice = (decimal)candle.Close,
							HighPrice = (decimal)candle.High,
							LowPrice = (decimal)candle.Low,
							TotalVolume = (decimal)candle.Volume,
							OpenTime = time,
							State = CandleStates.Finished,
							OriginalTransactionId = mdMsg.TransactionId,
						}, cancellationToken);

						if (--left <= 0)
						{
							needBreak = true;
							break;
						}

						needBreak = false;
						from = candle.CloseTime;
					}

					if (needBreak || candles.Length < maxCount)
						break;
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_candleTransIds[$"{tf}_{symbol}"] = mdMsg.TransactionId;
				await _socketClient.SubscribeCandles(mdMsg.TransactionId, symbol, tf, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeCandles(mdMsg.OriginalTransactionId, symbol, tf, cancellationToken);
		}
	}

	public override async ValueTask RegisterOrder(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var symbol = regMsg.SecurityId.ToSymbol();

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		await _httpClient.PlaceOrder(symbol, regMsg.Side.ToNative(), regMsg.OrderType.ToNative(),
			regMsg.TimeInForce.ToNative(), regMsg.Volume, regMsg.Price, regMsg.TransactionId.ToRequestId(),
			cancellationToken);
	}

	public override async ValueTask CancelOrder(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		await _httpClient.CancelOrder(cancelMsg.SecurityId.ToSymbol(), cancelMsg.OrderId,
			cancelMsg.OriginalTransactionId.ToRequestId(), cancellationToken);
	}

	public override ValueTask CancelGroupOrder(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("MEXC doesn't support group order cancellation");
	}

	public override ValueTask ReplaceOrder(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("MEXC doesn't support order replacement");
	}

	public override async ValueTask PortfolioLookup(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscribed = false;
			await RefreshPrivateSubscriptions(lookupMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCode,
			OriginalTransactionId = lookupMsg.TransactionId
		}, cancellationToken);

		var accountInfo = await _httpClient.GetAccountInfo(cancellationToken);

		if (accountInfo?.Balances is not null)
			await ProcessBalances(lookupMsg.TransactionId, accountInfo.Balances, cancellationToken);

		if (!lookupMsg.IsHistoryOnly())
		{
			_portfolioSubscribed = true;
			await RefreshPrivateSubscriptions(lookupMsg.TransactionId, cancellationToken);
		}
	}

	public override async ValueTask OrderStatus(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (!statusMsg.IsSubscribe)
		{
			_ordersSubscribed = false;
			await RefreshPrivateSubscriptions(statusMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var orders = await _httpClient.GetOpenOrders(null, cancellationToken);
		await ProcessOrders(statusMsg.TransactionId, orders, cancellationToken);

		if (!statusMsg.IsHistoryOnly())
		{
			_ordersSubscribed = true;
			await RefreshPrivateSubscriptions(statusMsg.TransactionId, cancellationToken);
		}
	}

	private async ValueTask ProcessOrders(long originTransId, Order[] orders, CancellationToken cancellationToken)
	{
		foreach (var order in orders)
		{
			if (!order.ClientOrderId.TryToTransId(out var transId))
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = order.Symbol.ToStockSharp(BoardCode),
				ServerTime = originTransId > 0 ? order.Time : order.UpdateTime,
				TransactionId = originTransId > 0 ? transId : 0,
				OriginalTransactionId = originTransId > 0 ? originTransId : transId,
				OrderType = order.Type.ToOrderType(),
				OrderId = order.OrderId,
				OrderVolume = order.OrigQty?.ToDecimal(),
				Balance = (order.OrigQty - order.ExecutedQty)?.ToDecimal(),
				Side = order.Side.ToSide(),
				OrderPrice = order.Price?.ToDecimal() ?? 0,
				PortfolioName = PortfolioName,
				OrderState = order.Status.ToOrderState(),
				TimeInForce = order.TimeInForce?.ToTimeInForce(),
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessBalances(long transId, Balance[] balances, CancellationToken cancellationToken)
	{
		foreach (var balance in balances)
		{
			if ((balance.Free ?? 0) == 0 && (balance.Locked ?? 0) == 0)
				continue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = balance.Asset.ToStockSharp(BoardCode),
				ServerTime = CurrentTime,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Free?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, balance.Locked?.ToDecimal(), true), cancellationToken);
		}
	}

	private async ValueTask RefreshPrivateSubscriptions(long transId, CancellationToken cancellationToken)
	{
		if (!_portfolioSubscribed && !_ordersSubscribed)
		{
			if (_accountStreamSubscribed)
			{
				await _socketClient.UnsubscribePrivateAccount(transId, cancellationToken);
				_accountStreamSubscribed = false;
			}

			if (_ordersStreamSubscribed)
			{
				await _socketClient.UnsubscribePrivateOrders(transId, cancellationToken);
				_ordersStreamSubscribed = false;
			}

			if (_dealsStreamSubscribed)
			{
				await _socketClient.UnsubscribePrivateDeals(transId, cancellationToken);
				_dealsStreamSubscribed = false;
			}

			_socketClient.DisconnectPrivate();
			await DeleteListenKey(cancellationToken);
			StopListenKeyLoop();
			return;
		}

		await EnsureListenKey(cancellationToken);
		await _socketClient.EnsurePrivateConnected(_listenKey, cancellationToken);

		if (_portfolioSubscribed && !_accountStreamSubscribed)
		{
			await _socketClient.SubscribePrivateAccount(transId, cancellationToken);
			_accountStreamSubscribed = true;
		}
		else if (!_portfolioSubscribed && _accountStreamSubscribed)
		{
			await _socketClient.UnsubscribePrivateAccount(transId, cancellationToken);
			_accountStreamSubscribed = false;
		}

		if (_ordersSubscribed && !_ordersStreamSubscribed)
		{
			await _socketClient.SubscribePrivateOrders(transId, cancellationToken);
			_ordersStreamSubscribed = true;
		}
		else if (!_ordersSubscribed && _ordersStreamSubscribed)
		{
			await _socketClient.UnsubscribePrivateOrders(transId, cancellationToken);
			_ordersStreamSubscribed = false;
		}

		if (_ordersSubscribed && !_dealsStreamSubscribed)
		{
			await _socketClient.SubscribePrivateDeals(transId, cancellationToken);
			_dealsStreamSubscribed = true;
		}
		else if (!_ordersSubscribed && _dealsStreamSubscribed)
		{
			await _socketClient.UnsubscribePrivateDeals(transId, cancellationToken);
			_dealsStreamSubscribed = false;
		}
	}

	private async ValueTask EnsureListenKey(CancellationToken cancellationToken)
	{
		if (!_listenKey.IsEmpty() && DateTime.UtcNow < _listenKeyExpiresAt)
			return;

		await DeleteListenKey(cancellationToken);

		_listenKey = await _httpClient.CreateListenKey(cancellationToken);
		_listenKeyExpiresAt = DateTime.UtcNow + TimeSpan.FromMinutes(55);
		StartListenKeyLoop();
	}

	private async ValueTask DeleteListenKey(CancellationToken cancellationToken)
	{
		if (_listenKey.IsEmpty())
			return;

		var listenKey = _listenKey;
		_listenKey = null;
		_listenKeyExpiresAt = default;

		try
		{
			await _httpClient.DeleteListenKey(listenKey, cancellationToken);
		}
		catch (Exception ex)
		{
			this.AddWarningLog("Failed to close listenKey '{0}': {1}", listenKey, ex.Message);
		}
	}

	private void StartListenKeyLoop()
	{
		if (_listenKeyCts != null)
			return;

		_listenKeyCts = new CancellationTokenSource();
		_ = ListenKeyLoopAsync(_listenKeyCts.Token);
	}

	private void StopListenKeyLoop()
	{
		if (_listenKeyCts is not { } cts)
			return;

		_listenKeyCts = null;

		try
		{
			cts.Cancel();
		}
		finally
		{
			cts.Dispose();
		}
	}

	private async Task ListenKeyLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(TimeSpan.FromMinutes(30), cancellationToken);

				if (_listenKey.IsEmpty())
					continue;

				await _httpClient.PingListenKey(_listenKey, cancellationToken);
				_listenKeyExpiresAt = DateTime.UtcNow + TimeSpan.FromMinutes(55);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			this.AddErrorLog(ex);
			_listenKeyExpiresAt = default;
		}
	}

	private ValueTask SessionOnAccountReceived(PrivateAccountUpdate update, CancellationToken cancellationToken)
	{
		if (!_portfolioSubscribed || update.Asset.IsEmpty())
			return default;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = update.Asset.ToStockSharp(BoardCode),
			ServerTime = update.Time == default ? CurrentTime : update.Time,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, update.Balance?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, update.Frozen?.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SessionOnUserTradeReceived(PrivateDealUpdate trade, CancellationToken cancellationToken)
	{
		if (!_ordersSubscribed)
			return default;

		var execMsg = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(BoardCode),
			ServerTime = trade.Time == default ? CurrentTime : trade.Time,
			TradePrice = trade.Price?.ToDecimal(),
			TradeVolume = trade.Quantity?.ToDecimal(),
			OriginSide = trade.TradeType == 2 ? Sides.Sell : Sides.Buy,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			Commission = trade.FeeAmount?.ToDecimal(),
			CommissionCurrency = trade.FeeCurrency,
			PortfolioName = PortfolioName,
		};

		return SendOutMessageAsync(execMsg, cancellationToken);
	}

	private ValueTask SessionOnPrivateOrderReceived(PrivateOrderUpdate order, CancellationToken cancellationToken)
	{
		if (!_ordersSubscribed)
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(BoardCode),
			ServerTime = order.CreateTime == default ? CurrentTime : order.CreateTime,
			OrderStringId = order.Id,
			OrderType = ToOrderType(order.OrderType),
			OrderVolume = order.Quantity?.ToDecimal(),
			Balance = order.RemainQuantity?.ToDecimal(),
			Side = order.TradeType == 2 ? Sides.Sell : Sides.Buy,
			OrderPrice = order.Price?.ToDecimal() ?? 0,
			PortfolioName = PortfolioName,
			OrderState = ToOrderState(order.Status),
		}, cancellationToken);
	}

	private static OrderTypes? ToOrderType(int? orderType)
	{
		return orderType switch
		{
			1 => OrderTypes.Limit,
			2 => OrderTypes.Market,
			_ => null,
		};
	}

	private static OrderStates ToOrderState(int? status)
	{
		return status switch
		{
			1 => OrderStates.Active,
			2 => OrderStates.Done,
			3 => OrderStates.Done,
			4 => OrderStates.Done,
			5 => OrderStates.Done,
			_ => OrderStates.Active,
		};
	}

	private ValueTask SessionOnTickerReceived(Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(BoardCode),
			ServerTime = CurrentTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice?.ToDecimal())
		.TryAdd(Level1Fields.Change, ticker.PriceChangePercent?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice?.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume?.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice?.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidQty?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice?.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskQty?.ToDecimal())
		, cancellationToken);
	}

	private ValueTask SessionOnTradeReceived(TradeStream trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(BoardCode),
			ServerTime = trade.Time,
			TradeId = trade.Id,
			TradePrice = trade.Price?.ToDecimal(),
			TradeVolume = trade.Quantity?.ToDecimal(),
			OriginSide = trade.IsBuyerMaker ? Sides.Sell : Sides.Buy,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookReceived(OrderBookUpdate book, CancellationToken cancellationToken)
	{
		if (!_bookInfos.TryGetValue(book.Symbol, out var info))
			return default;

		bool canIncrement;

		using (info.Sync.EnterScope())
		{
			if (info.NextId > book.FirstUpdateId)
				return default;

			if (info.IsRestoring)
			{
				info.AddIncrement(book.FirstUpdateId, book.FinalUpdateId, book);
				return default;
			}

			canIncrement = info.NextId == book.FirstUpdateId;

			if (canIncrement)
			{
				info.NextId = book.FinalUpdateId + 1;
			}
			else
			{
				info.AddIncrement(book.FirstUpdateId, book.FinalUpdateId, book);
				info.IsRestoring = true;
			}
		}

		ValueTask sendInc(OrderBookUpdate inc, CancellationToken ct)
		{
			return SendOutMessageAsync(new QuoteChangeMessage
			{
				ServerTime = CurrentTime,
				SecurityId = inc.Symbol.ToStockSharp(BoardCode),
				Bids = [.. inc.Bids.Select(e => new QuoteChange(e.Price?.ToDecimal() ?? 0, e.Quantity?.ToDecimal() ?? 0))],
				Asks = [.. inc.Asks.Select(e => new QuoteChange(e.Price?.ToDecimal() ?? 0, e.Quantity?.ToDecimal() ?? 0))],
				State = QuoteChangeStates.Increment,
			}, ct);
		}

		if (canIncrement)
			return sendInc(book, cancellationToken);
		else
		{
			this.AddDebugLog("getting snapshot for {0}", book.Symbol);

			_httpClient
				.GetOrderBook(book.Symbol, info.Depth, default)
				.ContinueWith(t =>
				{
					if (t.IsFaulted)
					{
						this.AddErrorLog(t.Exception);

						using (info.Sync.EnterScope())
							info.FinishRestore();
					}
					else
					{
						this.AddDebugLog("got snapshot for {0}", book.Symbol);

						try
						{
							var snapshot = t.Result;

							SendOutMessageAsync(new QuoteChangeMessage
							{
								ServerTime = CurrentTime,
								SecurityId = book.Symbol.ToStockSharp(BoardCode),
								Bids = [.. snapshot.Bids.Select(e => new QuoteChange(e.Price?.ToDecimal() ?? 0, e.Quantity?.ToDecimal() ?? 0))],
								Asks = [.. snapshot.Asks.Select(e => new QuoteChange(e.Price?.ToDecimal() ?? 0, e.Quantity?.ToDecimal() ?? 0))],
								State = QuoteChangeStates.SnapshotComplete,
							}, default);

							var nextId = snapshot.LastUpdateId + 1;

							(long firstId, long lastId, OrderBookUpdate book)[] increments;

							using (info.Sync.EnterScope())
								increments = info.Increments.CopyAndClear();

							foreach (var (firstId, lastId, inc) in increments.OrderBy(i => i.firstId))
							{
								if (nextId > firstId)
									continue;
								else if (nextId < firstId)
									break;

								nextId = lastId + 1;

								sendInc(inc, default);
							}

							using (info.Sync.EnterScope())
							{
								info.NextId = nextId;
								info.FinishRestore();
							}
						}
						catch (Exception ex)
						{
							this.AddErrorLog(ex);

							using (info.Sync.EnterScope())
								info.FinishRestore();
						}
					}
				});

			return default;
		}
	}

	private ValueTask SessionOnCandleReceived(CandleStream candle, CancellationToken cancellationToken)
	{
		if (!_candleTransIds.TryGetValue($"{candle.Kline.Interval}_{candle.Symbol}", out var transId))
			return default;

		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OpenPrice = (decimal)candle.Kline.Open,
			ClosePrice = (decimal)candle.Kline.Close,
			HighPrice = (decimal)candle.Kline.High,
			LowPrice = (decimal)candle.Kline.Low,
			TotalVolume = (decimal)candle.Kline.Volume,
			OpenTime = candle.Kline.OpenTime,
			State = candle.Kline.IsClosed ? CandleStates.Finished : CandleStates.Active,
			OriginalTransactionId = transId,
		}, cancellationToken);
	}

	private ValueTask SessionOnPusherError(Exception error, CancellationToken cancellationToken)
	{
		return SendOutErrorAsync(error, cancellationToken);
	}
}
