namespace StockSharp.Bitmart.Native.Futures;

using StockSharp.Bitmart.Native.Futures.Model;

class FuturesAdapter : BaseNativeAdapter
{
	private HttpClient _httpClient;
	private PublicPusherClient _publicClient;
	private PrivatePusherClient _privateClient;
	private readonly IdGenerator _transIdGen;
	private readonly Authenticator _authenticator;
	private readonly ILogReceiver _logs;
	private readonly string _publicWsAddress;
	private readonly string _privateWsAddress;

	private long _assetTransId;

	private readonly SynchronizedDictionary<(SecurityId secId, TimeSpan tf), long> _candleTransactions = [];
	private readonly SynchronizedDictionary<SecurityId, QuoteChangeMessage> _snapshots = [];
	private const int _maxCandlesCount = 500;
	private const int _defaultDepth = 5;

	private const int _maxOrdersCount = 200;
	private readonly string _portfolioName;

	public FuturesAdapter(IdGenerator transIdGen, Authenticator authenticator, ILogReceiver logs, string publicWsAddress, string privateWsAddress)
	{
		_transIdGen = transIdGen ?? throw new ArgumentNullException(nameof(transIdGen));
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
		_logs = logs ?? throw new ArgumentNullException(nameof(logs));
		_publicWsAddress = publicWsAddress.ThrowIfEmpty(nameof(publicWsAddress));
		_privateWsAddress = privateWsAddress.ThrowIfEmpty(nameof(privateWsAddress));
		_portfolioName = $"{nameof(Bitmart)}_{nameof(Futures)}_{_authenticator.Key.ToId()}";
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
		_snapshots.Clear();

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
		var contracts = await _httpClient.GetContracts(cancellationToken);
		var left = lookupMsg.Count ?? long.MaxValue;

		var secTypes = lookupMsg.GetSecurityTypes();

		foreach (var contract in contracts)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var secMsg = new SecurityMessage
			{
				SecurityId = contract.Symbol.ToStockSharp(),
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityType = SecurityTypes.Future,
				PriceStep = (decimal?)contract.PricePrecision,
				IssueDate = contract.OpenTimestamp,
				ExpiryDate = contract.ExpireTimestamp,
				SettlementDate = contract.SettleTimestamp,
				MinVolume = (decimal?)contract.ContractSize,
				VolumeStep = (decimal?)contract.VolPrecision,
			}.TryFillUnderlyingId(contract.IndexName);

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
				var step = (int)tf.TotalMinutes;

				var last = from;

				while (last < to)
				{
					var end = last.AddMinutes(step * _maxCandlesCount);
					var candles = await _httpClient.GetCandles(symbol, step, last, end, cancellationToken);

					var needBreak = false;

					foreach (var candle in candles.OrderBy(c => c.Time))
					{
						cancellationToken.ThrowIfCancellationRequested();

						var time = candle.Time;

						if (time < last)
							continue;

						if (time > to)
						{
							needBreak = true;
							break;
						}

						await ProcessCandleAsync(mdMsg.SecurityId, tf, time, candle, mdMsg.TransactionId, CandleStates.Finished, cancellationToken);

						if (--left <= 0)
						{
							needBreak = true;
							break;
						}

						last = time;
					}

					if (needBreak)
						break;

					last = end;
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
				if (condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				break;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = isMarket ? (decimal?)null : regMsg.Price;

		var orderId = await _httpClient.SubmitOrder(symbol, ToNative(regMsg.Side),
			ToNative(regMsg.OrderType), regMsg.Leverage ?? 1,
			"cross", regMsg.IsMarketMaker == true ? 4 : ToNative(regMsg.TimeInForce), price,
			regMsg.Volume, null, null, regMsg.TransactionId, cancellationToken);

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

		await _httpClient.CancelOrder(symbol, cancelMsg.OrderId ?? throw new ArgumentException(LocalizedStrings.NoOrderIds, nameof(cancelMsg)), cancellationToken);
	}

	public override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.IsAllSecurity())
			throw new InvalidOperationException("security id must be specified for group cancel");

		await _httpClient.CancelOrders(cancelMsg.SecurityId.ToNativeSymbol(), cancellationToken);
	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		var client = PrivateClient;

		if (!statusMsg.IsSubscribe)
		{
			await client.UnsubscribeOrders(statusMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var orders = await _httpClient.GetOpenOrders(null, cancellationToken);

		if (orders is not null)
		{
			foreach (var order in orders)
			{
				if (!long.TryParse(order.ClientOrderId, out var transId))
					continue;

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					ServerTime = order.CreateTime,
					SecurityId = order.Symbol.ToStockSharp(),
					TransactionId = transId,
					OriginalTransactionId = statusMsg.TransactionId,
					OrderVolume = (decimal)order.Size,
					Leverage = order.Leverage,
					Balance = (decimal)(order.Size - (order.DealSize ?? 0)),
					Side = ToSide(order.Side),
					OrderType = ToOrderType(order.Type),
					OrderPrice = (decimal)(order.Price ?? 0),
					PortfolioName = _portfolioName,
					OrderState = OrderStates.Active,
					AveragePrice = (decimal?)order.DealAvgPrice,
				}, cancellationToken);
			}
		}

		if (!statusMsg.IsHistoryOnly())
			await client.SubscribeOrders(statusMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	public override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var assetCode = "BTC";

		var client = PrivateClient;

		if (!lookupMsg.IsSubscribe)
		{
			await client.UnsubscribeAssets(_assetTransId, assetCode, cancellationToken);
			await client.UnsubscribePositions(lookupMsg.OriginalTransactionId, cancellationToken);

			_assetTransId = default;

			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.Bitmart,
			OriginalTransactionId = lookupMsg.TransactionId
		}, cancellationToken);

		foreach (var asset in await _httpClient.GetAssets(cancellationToken))
			await ProcessAssetAsync(asset, cancellationToken);

		foreach (var position in await _httpClient.GetPositions(null, cancellationToken))
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = position.Symbol.ToStockSharp(),
				PortfolioName = _portfolioName,
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (position.PositionType == 2 ? -1 : 1) * position.PositionValue?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.VariationMargin, position.MaintenanceMargin?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.Commission, position.CurrentFee?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedValue?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedValue?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.EntryPrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.OpenAvgPrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.LiquidationPrice, position.CloseAvgPrice?.ToDecimal(), true)
			, cancellationToken);
		}

		if (!lookupMsg.IsHistoryOnly())
		{
			_assetTransId = _transIdGen.GetNextId();
			await client.SubscribeAssets(_assetTransId, assetCode, cancellationToken);
			await client.SubscribePositions(lookupMsg.TransactionId, cancellationToken);
		}

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
			publicClient.TickerReceived			+= SessionOnTickerReceived;
			publicClient.OrderBookReceived		+= SessionOnOrderBookReceived;
			publicClient.TicksReceived			+= SessionOnTicksReceived;
			publicClient.CandlesReceived		+= SessionOnCandlesReceived;
		}

		if (_privateClient is PrivatePusherClient privateClient)
		{
			privateClient.Error					+= SessionOnPusherErrorAsync;
			privateClient.OrdersReceived		+= SessionOnOrdersReceived;
			privateClient.AssetReceived			+= SessionOnAssetReceived;
			privateClient.PositionsReceived		+= SessionOnPositionsReceived;
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
			publicClient.TickerReceived			-= SessionOnTickerReceived;
			publicClient.OrderBookReceived		-= SessionOnOrderBookReceived;
			publicClient.TicksReceived			-= SessionOnTicksReceived;
			publicClient.CandlesReceived		-= SessionOnCandlesReceived;
		}

		var privateClient = PrivateClient;

		if (privateClient is not null)
		{
			privateClient.Error					-= SessionOnPusherErrorAsync;
			privateClient.OrdersReceived		-= SessionOnOrdersReceived;
			privateClient.AssetReceived			-= SessionOnAssetReceived;
			privateClient.PositionsReceived		-= SessionOnPositionsReceived;
		}
	}

	#endregion

	private ValueTask ProcessCandleAsync(SecurityId securityId, TimeSpan timeFrame, DateTime time, IOhlc candle, long originTransId, CandleStates state, CancellationToken cancellationToken)
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
			State = state,
			OriginalTransactionId = originTransId
		}, cancellationToken);
	}

	private ValueTask SessionOnTickerReceived(Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(),
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice?.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidVol?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice?.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskVol?.ToDecimal())
		.TryAdd(Level1Fields.Index, ticker.FairPrice?.ToDecimal())
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice?.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume24h?.ToDecimal()), cancellationToken);
	}

	private async ValueTask SessionOnTicksReceived(IEnumerable<Tick> ticks, CancellationToken cancellationToken)
	{
		foreach (var tick in ticks)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = tick.Symbol.ToStockSharp(),
				TradeId = tick.TradeId,
				TradePrice = (decimal)tick.Price,
				TradeVolume = (decimal)tick.Size,
				ServerTime = tick.Time,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderBookReceived(OrderBook book, CancellationToken cancellationToken)
	{
		static QuoteChange ToChange(OrderBookEntry entry)
			=> new((decimal)entry.Price, (decimal)entry.Size);

		var quotes = book.Depths.Select(ToChange).ToArray();

		var snapshot = _snapshots.SafeAdd(book.Symbol.ToStockSharp(), key => new() { SecurityId = key });

		if (book.Way == 1)
			snapshot.Bids = quotes;
		else if (book.Way == 2)
			snapshot.Asks = quotes;

		var copy = snapshot.TypedClone();
		copy.ServerTime = book.Timestamp;

		return SendOutMessageAsync(copy, cancellationToken);
	}

	private async ValueTask SessionOnCandlesReceived(TimeSpan timeFrame, string symbol, IEnumerable<Ohlc> candles, CancellationToken cancellationToken)
	{
		var secId = symbol.ToStockSharp();

		if (!_candleTransactions.TryGetValue((secId, timeFrame), out var transId))
			return;

		foreach (var candle in candles)
			await ProcessCandleAsync(secId, timeFrame, candle.Time, candle, transId, CandleStates.Active, cancellationToken);
	}

	private async ValueTask SessionOnOrdersReceived(IEnumerable<SocketOrderData> datas, CancellationToken cancellationToken)
	{
		foreach (var data in datas)
		{
			var order = data.Order;

			if (!long.TryParse(data.Order.ClientOrderId, out var transId))
				continue;

			var execMsg = new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				ServerTime = order.UpdateTime,
				SecurityId = order.Symbol.ToStockSharp(),
				OriginalTransactionId = transId,
				OrderVolume = (decimal)order.Size,
				Leverage = order.Leverage,
				Balance = (decimal)(order.Size - (order.DealSize ?? 0)),
				Side = ToSide(order.Side),
				OrderType = ToOrderType(order.Type),
				OrderPrice = (decimal)(order.Price ?? 0),
				PortfolioName = _portfolioName,
				OrderState = OrderStates.Active,
				AveragePrice = (decimal?)order.DealAvgPrice,
			};

			if (order.LastTrade is SocketTrade trade)
			{
				execMsg.TradeId = trade.LastTradeId;
				execMsg.TradePrice = (decimal)trade.FillPrice;
				execMsg.TradeVolume = (decimal)trade.FillQty;
				execMsg.Commission = (decimal)trade.Fee;
				execMsg.CommissionCurrency = trade.FeeCcy;
			}

			await SendOutMessageAsync(execMsg, cancellationToken);
		}
	}

	private ValueTask SessionOnAssetReceived(Asset asset, CancellationToken cancellationToken)
	{
		return ProcessAssetAsync(asset, cancellationToken);
	}

	private async ValueTask SessionOnPositionsReceived(IEnumerable<SocketPosition> positions, CancellationToken cancellationToken)
	{
		foreach (var position in positions)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = position.Symbol.ToStockSharp(),
				PortfolioName = _portfolioName,
				ServerTime = position.UpdateTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (position.PositionType == 2 ? -1 : 1) * position.HoldVolume?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, position.FrozenVolume?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.HoldAvgPrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.OpenAvgPrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.LiquidationPrice, position.CloseAvgPrice?.ToDecimal(), true)
			, cancellationToken);
		}
	}

	private ValueTask ProcessAssetAsync(Asset asset, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			SecurityId = asset.Currency.ToStockSharp(),
			PortfolioName = _portfolioName,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, asset.Available?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, asset.Frozen?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, asset.Unrealized?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, asset.Equity?.ToDecimal(), true)
		, cancellationToken);
	}

	private static OrderTypes? ToOrderType(string type)
		=> type?.ToLowerInvariant() switch
		{
			"" or null => null,
			"limit" => OrderTypes.Limit,
			"market" => OrderTypes.Market,
			"trailing" => OrderTypes.Conditional,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue)
		};

	private static string ToNative(OrderTypes? type)
		=> type switch
		{
			null => null,
			OrderTypes.Limit => "limit",
			OrderTypes.Market => "market",
			OrderTypes.Conditional => "trailing",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue)
		};

	private static int ToNative(Sides side)
		=> side switch
		{
			Sides.Buy => 1,
			Sides.Sell => 3,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue)
		};

	private static Sides ToSide(int side)
		=> side switch
		{
			1 or 2 => Sides.Buy,
			3 or 4 => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue)
		};

	private static int? ToNative(TimeInForce? tif)
		=> tif switch
		{
			null => null,
			TimeInForce.PutInQueue => 1,
			TimeInForce.MatchOrCancel => 2,
			TimeInForce.CancelBalance => 3,
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue)
		};
}
