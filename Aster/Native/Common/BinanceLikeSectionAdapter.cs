namespace StockSharp.Aster.Native.Common;

using StockSharp.Aster.Native.Common.Model;

abstract class BinanceLikeSectionAdapter : BaseNativeAdapter
{
	private const int _maxHistoryLimit = 1000;

	private AsterRestClient _restClient;
	private AsterWsClient _wsClient;
	private AsterWsClient _privateWsClient;
	private string _portfolioName;
	private string _listenKey;
	private DateTime _nextListenKeyRefresh;
	private readonly Lock _sync = new();
	private readonly Dictionary<long, string> _level1Realtime = [];
	private readonly Dictionary<long, (string Symbol, int? MaxDepth)> _depthRealtime = [];
	private readonly Dictionary<long, TickRealtimeState> _ticksRealtime = [];
	private readonly Dictionary<long, CandleRealtimeState> _candlesRealtime = [];
	private readonly Dictionary<string, int> _level1Refs = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, int> _depthRefs = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, int> _ticksRefs = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<(string Symbol, TimeSpan TimeFrame), int> _candleRefs = [];
	private static readonly TimeSpan _listenKeyRefreshInterval = TimeSpan.FromMinutes(25);

	private sealed class TickRealtimeState
	{
		public string Symbol { get; init; }
		public long TransactionId { get; init; }
		public long? LastTradeId { get; set; }
		public DateTime LastTime { get; set; }
	}

	private sealed class CandleRealtimeState
	{
		public string Symbol { get; init; }
		public long TransactionId { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
	}

	protected BinanceLikeSectionAdapter(SecureString key, SecureString secret, string boardCode, SecurityTypes securityType, string sectionName, string restEndpoint, string wsEndpoint, string apiPrefix, WorkingTime workingTime)
		: base(key, secret, boardCode, securityType, sectionName)
	{
		RestEndpoint = restEndpoint.ThrowIfEmpty(nameof(restEndpoint));
		WsEndpoint = wsEndpoint.ThrowIfEmpty(nameof(wsEndpoint));
		ApiPrefix = apiPrefix.ThrowIfEmpty(nameof(apiPrefix));
		_workingTime = workingTime;
	}

	private readonly WorkingTime _workingTime;

	protected string RestEndpoint { get; }
	protected string WsEndpoint { get; }
	protected string ApiPrefix { get; }
	protected virtual bool IsDerivativesSection => SecurityType == SecurityTypes.Future;

	protected AsterRestClient RestClient => _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	protected AsterWsClient WsClient => _wsClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	protected string PortfolioName => _portfolioName;

	public override async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await base.ConnectAsync(cancellationToken);

		_restClient = new(CreateApiBaseUri(RestEndpoint, ApiPrefix), Key, Secret) { Parent = this };
		_wsClient = new(WsEndpoint, _workingTime) { Parent = this };
		_wsClient.TickerReceived += OnWsTickerAsync;
		_wsClient.DepthReceived += OnWsDepthAsync;
		_wsClient.TradeReceived += OnWsTradeAsync;
		_wsClient.CandleReceived += OnWsCandleAsync;
		_wsClient.Error += OnWsErrorAsync;
		_portfolioName = $"{nameof(Aster)}_{SectionName}_{(Key.IsEmpty() ? "Public" : Key.ToId())}";
		ClearRealtimeSubscriptions();
		await _wsClient.ConnectAsync(cancellationToken);

		if (CanUsePrivateStream())
			await ConnectPrivateStreamAsync(cancellationToken);
	}

	public override void Disconnect()
	{
		base.Disconnect();

		if (!_listenKey.IsEmpty() && _restClient is not null)
		{
			try
			{
				RestClient.DeleteListenKeyAsync(IsDerivativesSection, _listenKey, default).AsTask().GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				this.AddErrorLog(ex);
			}
		}

		DisconnectPrivateStream();

		ClearRealtimeSubscriptions();
		if (_wsClient is not null)
		{
			_wsClient.TickerReceived -= OnWsTickerAsync;
			_wsClient.DepthReceived -= OnWsDepthAsync;
			_wsClient.TradeReceived -= OnWsTradeAsync;
			_wsClient.CandleReceived -= OnWsCandleAsync;
			_wsClient.Error -= OnWsErrorAsync;
			_wsClient.Disconnect();
			_wsClient.Dispose();
			_wsClient = null;
		}
		_restClient?.Dispose();
		_restClient = null;
	}

	public override async ValueTask ResetAsync(CancellationToken cancellationToken)
	{
		await ReleaseListenKeyAsync(cancellationToken);
		await base.ResetAsync(cancellationToken);
	}

	public override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		_ = timeMsg;

		if (!CanUsePrivateStream() || _listenKey.IsEmpty() || DateTime.UtcNow < _nextListenKeyRefresh)
			return;

		await RestClient.KeepAliveListenKeyAsync(IsDerivativesSection, _listenKey, cancellationToken);
		_nextListenKeyRefresh = DateTime.UtcNow + _listenKeyRefreshInterval;
	}

	public override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var exchangeInfo = await RestClient.GetExchangeInfoAsync(cancellationToken);
		var symbols = exchangeInfo.Symbols ?? [];

		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		if (secTypes.Count > 0 && !secTypes.Contains(SecurityType))
			return;

		foreach (var symbol in symbols)
		{
			if (symbol?.Symbol.IsEmpty() != false)
				continue;

			if (!symbol.Status.IsEmpty() && !symbol.Status.EqualsIgnoreCase("TRADING"))
				continue;

			var priceFilter = symbol.Filters?.FirstOrDefault(static f => f.FilterType.EqualsIgnoreCase("PRICE_FILTER"));
			var volumeFilter = symbol.Filters?.FirstOrDefault(static f => f.FilterType.EqualsIgnoreCase("LOT_SIZE"));
			var notionalFilter = symbol.Filters?.FirstOrDefault(static f => f.FilterType.EqualsIgnoreCase("MIN_NOTIONAL"));

			var secMsg = new SecurityMessage
			{
				SecurityId = symbol.Symbol.ToStockSharp(BoardCode),
				Name = symbol.Symbol,
				SecurityType = SecurityType,
				OriginalTransactionId = lookupMsg.TransactionId,
				Decimals = symbol.PricePrecision > 0 ? symbol.PricePrecision : symbol.QuantityPrecision,
				PriceStep = priceFilter?.TickSize.To<decimal?>(),
				VolumeStep = volumeFilter?.StepSize.To<decimal?>(),
				MinVolume = volumeFilter?.MinQty.To<decimal?>(),
				MaxVolume = volumeFilter?.MaxQty.To<decimal?>(),
				UnderlyingSecurityMinVolume = notionalFilter?.MinNotional.To<decimal?>(),
				ExpiryDate = symbol.DeliveryDate?.FromUnix(false),
			}.TryFillUnderlyingId((symbol.BaseAsset ?? Extensions.ExtractBaseAsset(symbol.Symbol)).ToUpperInvariant());

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

	}

	public override async ValueTask Level1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnregisterLevel1SubscriptionAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToNative();
		await PublishLevel1Async(symbol, mdMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await RegisterLevel1SubscriptionAsync(mdMsg.TransactionId, symbol, cancellationToken);
	}

	public override async ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnregisterDepthSubscriptionAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToNative();
		await PublishDepthAsync(symbol, mdMsg.MaxDepth, mdMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await RegisterDepthSubscriptionAsync(mdMsg.TransactionId, symbol, mdMsg.MaxDepth, cancellationToken);
	}

	public override async ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnregisterTicksSubscriptionAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToNative();
		var from = mdMsg.From;
		var to = mdMsg.To ?? DateTime.UtcNow;
		var left = mdMsg.Count ?? long.MaxValue;
		long? lastTradeId = null;

		Trade[] trades;

		if (from is null)
		{
			trades = await RestClient.GetTradesAsync(symbol, _maxHistoryLimit, cancellationToken);
		}
		else
		{
			trades = await RestClient.GetAggTradesAsync(symbol, null, from, to, _maxHistoryLimit, cancellationToken);
		}

		foreach (var trade in trades.OrderBy(static t => t.TradeTime ?? t.Time ?? 0))
		{
			var unix = trade.TradeTime ?? trade.Time;

			if (unix is null)
				continue;

			var tradeTime = unix.Value.FromUnix(false);

			if (from is DateTime fromTime && tradeTime < fromTime)
				continue;

			if (tradeTime > to)
				break;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = tradeTime,
				TradeId = trade.AggregateId ?? trade.Id,
				TradePrice = trade.Price.To<decimal?>(),
				TradeVolume = trade.Quantity.To<decimal?>(),
				OriginSide = trade.IsBuyerMaker == true ? Sides.Sell : Sides.Buy,
				OriginalTransactionId = mdMsg.TransactionId,
			}, cancellationToken);

			lastTradeId = trade.AggregateId ?? trade.Id;

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await RegisterTicksSubscriptionAsync(mdMsg.TransactionId, symbol, to, lastTradeId, cancellationToken);
	}

	public override async ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnregisterCandlesSubscriptionAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToNative();
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToNative();
		var from = mdMsg.From ?? (DateTime.UtcNow - TimeSpan.FromDays(1));
		var to = mdMsg.To ?? DateTime.UtcNow;
		var left = mdMsg.Count ?? long.MaxValue;
		var lastOpenTime = from;

		var candles = await RestClient.GetCandlesAsync(symbol, interval, from, to, _maxHistoryLimit, cancellationToken);

		foreach (var item in candles.OfType<JArray>().OrderBy(static c => c[0]?.Value<long>() ?? 0))
		{
			var openTimeUnix = item[0]?.Value<long?>() ?? 0;
			var openTime = openTimeUnix.FromUnix(false);

			if (openTime < from)
				continue;

			if (openTime > to)
				break;

			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = symbol.ToStockSharp(BoardCode),
				TypedArg = timeFrame,
				OpenTime = openTime,
				OpenPrice = item[1]?.Value<string>().To<decimal?>() ?? 0m,
				HighPrice = item[2]?.Value<string>().To<decimal?>() ?? 0m,
				LowPrice = item[3]?.Value<string>().To<decimal?>() ?? 0m,
				ClosePrice = item[4]?.Value<string>().To<decimal?>() ?? 0m,
				TotalVolume = item[5]?.Value<string>().To<decimal?>() ?? 0m,
				State = CandleStates.Finished,
				OriginalTransactionId = mdMsg.TransactionId,
			}, cancellationToken);

			lastOpenTime = openTime;

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await RegisterCandlesSubscriptionAsync(mdMsg.TransactionId, symbol, timeFrame, lastOpenTime, cancellationToken);
	}

	public override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();

		var symbol = regMsg.SecurityId.ToNative();
		var clientOrderId = CreateClientId(regMsg.TransactionId, regMsg.UserOrderId);
		var order = await RegisterInternalAsync(
			symbol,
			regMsg.Side,
			regMsg.OrderType,
			regMsg.Price,
			regMsg.Volume.Abs(),
			regMsg.TimeInForce,
			regMsg.PostOnly,
			regMsg.Condition as AsterOrderCondition,
			regMsg.PositionEffect,
			clientOrderId,
			cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = (order.TransactTime ?? order.Time)?.FromUnix(false) ?? CurrentTime,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = regMsg.Volume.Abs(),
			Balance = order.GetBalance() ?? regMsg.Volume.Abs(),
			OrderPrice = order.Price.To<decimal?>() ?? regMsg.Price,
			OrderType = regMsg.OrderType ?? OrderTypes.Limit,
			OrderState = order.Status.ToOrderState(),
			OrderId = order.OrderId,
			OrderStringId = order.ClientOrderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = order.TimeInForce.ToTimeInForce(out var postOnly),
			PostOnly = regMsg.PostOnly ?? postOnly,
			Condition = regMsg.Condition,
			PositionEffect = regMsg.PositionEffect,
		}, cancellationToken);
	}

	public override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();

		var symbol = replaceMsg.SecurityId.ToNative();
		var oldId = replaceMsg.OldOrderId;
		var oldClientId = replaceMsg.OldOrderStringId;

		if (oldId is null && oldClientId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.TransactionId));

		await RestClient.CancelOrderAsync(symbol, oldId, oldClientId, CreateClientId(replaceMsg.TransactionId, null), cancellationToken);

		var clientOrderId = CreateClientId(replaceMsg.TransactionId, replaceMsg.UserOrderId);
		var order = await RegisterInternalAsync(
			symbol,
			replaceMsg.Side,
			replaceMsg.OrderType,
			replaceMsg.Price,
			replaceMsg.Volume.Abs(),
			replaceMsg.TimeInForce,
			replaceMsg.PostOnly,
			replaceMsg.Condition as AsterOrderCondition,
			replaceMsg.PositionEffect,
			clientOrderId,
			cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = (order.TransactTime ?? order.Time)?.FromUnix(false) ?? CurrentTime,
			PortfolioName = _portfolioName,
			Side = replaceMsg.Side,
			OrderVolume = replaceMsg.Volume.Abs(),
			Balance = order.GetBalance() ?? replaceMsg.Volume.Abs(),
			OrderPrice = order.Price.To<decimal?>() ?? replaceMsg.Price,
			OrderType = replaceMsg.OrderType ?? OrderTypes.Limit,
			OrderState = order.Status.ToOrderState(),
			OrderId = order.OrderId,
			OrderStringId = order.ClientOrderId,
			TransactionId = replaceMsg.TransactionId,
			OriginalTransactionId = replaceMsg.TransactionId,
			TimeInForce = order.TimeInForce.ToTimeInForce(out var postOnly),
			PostOnly = replaceMsg.PostOnly ?? postOnly,
			Condition = replaceMsg.Condition,
			PositionEffect = replaceMsg.PositionEffect,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();

		var symbol = cancelMsg.SecurityId.ToNative();
		var orderId = cancelMsg.OrderId;
		var orderStringId = cancelMsg.OrderStringId;

		if (orderId is null && orderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.TransactionId));

		if (orderId is null && long.TryParse(orderStringId, out var parsed))
			orderId = parsed;

		var canceled = await RestClient.CancelOrderAsync(symbol, orderId, orderId is null ? orderStringId : null, CreateClientId(cancelMsg.TransactionId, null), cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = (canceled.TransactTime ?? canceled.Time)?.FromUnix(false) ?? CurrentTime,
			PortfolioName = _portfolioName,
			OrderId = canceled.OrderId ?? orderId,
			OrderStringId = canceled.ClientOrderId.IsEmpty() ? orderStringId : canceled.ClientOrderId,
			OrderState = OrderStates.Done,
			Balance = 0,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();

		if ((cancelMsg.Mode & OrderGroupCancelModes.ClosePositions) == OrderGroupCancelModes.ClosePositions)
			throw new NotSupportedException("ClosePositions mode is not supported.");

		var symbol = cancelMsg.SecurityId.SecurityCode;
		var sideFilter = cancelMsg.Side;
		var stopFilter = cancelMsg.IsStop;
		var orders = await RestClient.GetOpenOrdersAsync(symbol, cancellationToken);

		foreach (var order in orders)
		{
			if (order?.Symbol.IsEmpty() != false)
				continue;

			if (sideFilter is not null && order.Side.ToSide() != sideFilter.Value)
				continue;

			if (stopFilter is bool isStop && IsStopOrderType(order.Type) != isStop)
				continue;

			await RestClient.CancelOrderAsync(order.Symbol, order.OrderId, null, null, cancellationToken);

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = order.Symbol.ToStockSharp(BoardCode),
				ServerTime = CurrentTime,
				PortfolioName = _portfolioName,
				OrderId = order.OrderId,
				OrderStringId = order.ClientOrderId,
				OrderState = OrderStates.Done,
				Balance = 0,
				OriginalTransactionId = cancelMsg.TransactionId,
			}, cancellationToken);
		}
	}

	public override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (!lookupMsg.IsSubscribe)
			return;

		EnsurePrivateReady();

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCode,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);

		await SendSectionPortfolioAsync(lookupMsg, cancellationToken);
	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (!statusMsg.IsSubscribe)
			return;

		EnsurePrivateReady();

		var symbolFilter = statusMsg.SecurityId.SecurityCode;
		var orders = await RestClient.GetOpenOrdersAsync(symbolFilter, cancellationToken);

		foreach (var order in orders.OrderBy(static o => o.Time ?? o.TransactTime ?? 0))
		{
			if (order?.Symbol.IsEmpty() != false)
				continue;

			var orderType = order.Type.ToOrderType(out var postOnly, out var condition);

			if (condition is not null && order.StopPrice.To<decimal?>() is decimal sp)
				condition.ActivationPrice = sp;

			var transId = TryExtractTransactionId(order.ClientOrderId) ?? 0;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = order.Symbol.ToStockSharp(BoardCode),
				ServerTime = (order.Time ?? order.TransactTime)?.FromUnix(false) ?? CurrentTime,
				PortfolioName = _portfolioName,
				Side = order.Side.ToSide(),
				OrderVolume = order.OrigQty.To<decimal?>(),
				Balance = order.GetBalance() ?? order.OrigQty.To<decimal?>() ?? 0m,
				OrderPrice = order.Price.To<decimal?>() ?? 0m,
				OrderType = orderType,
				OrderState = order.Status.ToOrderState(),
				Condition = condition,
				TimeInForce = order.TimeInForce.ToTimeInForce(out var postOnly2),
				PostOnly = postOnly ?? postOnly2,
				OrderId = order.OrderId,
				OrderStringId = order.ClientOrderId,
				TransactionId = transId,
				OriginalTransactionId = statusMsg.TransactionId,
			}, cancellationToken);
		}

		var symbols = orders
			.Select(static o => o.Symbol)
			.Where(static s => !s.IsEmpty())
			.Distinct(StringComparer.InvariantCultureIgnoreCase)
			.ToArray();

		if (!symbolFilter.IsEmpty() && !symbols.Contains(symbolFilter, StringComparer.InvariantCultureIgnoreCase))
			symbols = [.. symbols, symbolFilter];

		foreach (var symbol in symbols)
		{
			var trades = await RestClient.GetMyTradesAsync(symbol, statusMsg.From, statusMsg.To, statusMsg.Count?.To<int?>(), cancellationToken);
			var left = statusMsg.Count ?? long.MaxValue;

			foreach (var trade in trades.OrderBy(static t => t.Time ?? 0))
			{
				if (trade?.Symbol.IsEmpty() != false)
					continue;

				var tradeTime = (trade.Time ?? 0).FromUnix(false);

				if (statusMsg.From is DateTime from && tradeTime < from)
					continue;

				if (statusMsg.To is DateTime to && tradeTime > to)
					break;

				var side = trade.GetSide();

				if (side is null)
					continue;

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					SecurityId = trade.Symbol.ToStockSharp(BoardCode),
					ServerTime = tradeTime,
					OrderId = trade.OrderId,
					TradeId = trade.TradeId,
					TradePrice = trade.Price.To<decimal?>(),
					TradeVolume = trade.Quantity.To<decimal?>(),
					Commission = trade.Commission.To<decimal?>(),
					CommissionCurrency = trade.CommissionAsset,
					Side = side.Value,
					PortfolioName = _portfolioName,
					OriginalTransactionId = statusMsg.TransactionId,
				}, cancellationToken);

				if (--left <= 0)
					break;
			}
		}

	}

	private async ValueTask RegisterLevel1SubscriptionAsync(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		var shouldSubscribe = false;

		using (_sync.EnterScope())
		{
			_level1Realtime[transactionId] = symbol;
			shouldSubscribe = AddRef(_level1Refs, symbol);
		}

		if (shouldSubscribe)
			await WsClient.SubscribeTickerAsync(symbol, cancellationToken);
	}

	private async ValueTask UnregisterLevel1SubscriptionAsync(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		string symbol = null;
		var shouldUnsubscribe = false;

		using (_sync.EnterScope())
		{
			if (_level1Realtime.TryGetValue(originalTransactionId, out symbol))
			{
				_level1Realtime.Remove(originalTransactionId);
				shouldUnsubscribe = ReleaseRef(_level1Refs, symbol);
			}
		}

		if (shouldUnsubscribe && _wsClient is not null)
			await WsClient.UnsubscribeTickerAsync(symbol, cancellationToken);
	}

	private async ValueTask RegisterDepthSubscriptionAsync(long transactionId, string symbol, int? maxDepth, CancellationToken cancellationToken)
	{
		var shouldSubscribe = false;

		using (_sync.EnterScope())
		{
			_depthRealtime[transactionId] = (symbol, maxDepth);
			shouldSubscribe = AddRef(_depthRefs, symbol);
		}

		if (shouldSubscribe)
			await WsClient.SubscribeDepthAsync(symbol, cancellationToken);
	}

	private async ValueTask UnregisterDepthSubscriptionAsync(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		string symbol = null;
		var shouldUnsubscribe = false;

		using (_sync.EnterScope())
		{
			if (_depthRealtime.TryGetValue(originalTransactionId, out var state))
			{
				_depthRealtime.Remove(originalTransactionId);
				symbol = state.Symbol;
				shouldUnsubscribe = ReleaseRef(_depthRefs, symbol);
			}
		}

		if (shouldUnsubscribe && _wsClient is not null)
			await WsClient.UnsubscribeDepthAsync(symbol, cancellationToken);
	}

	private async ValueTask RegisterTicksSubscriptionAsync(long transactionId, string symbol, DateTime lastTime, long? lastTradeId, CancellationToken cancellationToken)
	{
		var shouldSubscribe = false;

		using (_sync.EnterScope())
		{
			_ticksRealtime[transactionId] = new()
			{
				Symbol = symbol,
				TransactionId = transactionId,
				LastTime = lastTime,
				LastTradeId = lastTradeId,
			};

			shouldSubscribe = AddRef(_ticksRefs, symbol);
		}

		if (shouldSubscribe)
			await WsClient.SubscribeTradesAsync(symbol, cancellationToken);
	}

	private async ValueTask UnregisterTicksSubscriptionAsync(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		string symbol = null;
		var shouldUnsubscribe = false;

		using (_sync.EnterScope())
		{
			if (_ticksRealtime.TryGetValue(originalTransactionId, out var state))
			{
				_ticksRealtime.Remove(originalTransactionId);
				symbol = state.Symbol;
				shouldUnsubscribe = ReleaseRef(_ticksRefs, symbol);
			}
		}

		if (shouldUnsubscribe && _wsClient is not null)
			await WsClient.UnsubscribeTradesAsync(symbol, cancellationToken);
	}

	private async ValueTask RegisterCandlesSubscriptionAsync(long transactionId, string symbol, TimeSpan timeFrame, DateTime lastOpenTime, CancellationToken cancellationToken)
	{
		var shouldSubscribe = false;
		var key = (symbol, timeFrame);

		using (_sync.EnterScope())
		{
			_candlesRealtime[transactionId] = new()
			{
				Symbol = symbol,
				TransactionId = transactionId,
				TimeFrame = timeFrame,
				LastOpenTime = lastOpenTime,
			};

			shouldSubscribe = AddRef(_candleRefs, key);
		}

		if (shouldSubscribe)
			await WsClient.SubscribeCandlesAsync(symbol, timeFrame, cancellationToken);
	}

	private async ValueTask UnregisterCandlesSubscriptionAsync(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		(string Symbol, TimeSpan TimeFrame)? key = null;
		var shouldUnsubscribe = false;

		using (_sync.EnterScope())
		{
			if (_candlesRealtime.TryGetValue(originalTransactionId, out var state))
			{
				_candlesRealtime.Remove(originalTransactionId);
				key = (state.Symbol, state.TimeFrame);
				shouldUnsubscribe = ReleaseRef(_candleRefs, key.Value);
			}
		}

		if (shouldUnsubscribe && key is { } candleKey && _wsClient is not null)
			await WsClient.UnsubscribeCandlesAsync(candleKey.Symbol, candleKey.TimeFrame, cancellationToken);
	}

	private void ClearRealtimeSubscriptions()
	{
		using (_sync.EnterScope())
		{
			_level1Realtime.Clear();
			_depthRealtime.Clear();
			_ticksRealtime.Clear();
			_candlesRealtime.Clear();

			_level1Refs.Clear();
			_depthRefs.Clear();
			_ticksRefs.Clear();
			_candleRefs.Clear();
		}
	}

	private ValueTask OnWsErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private bool CanUsePrivateStream()
		=> !Key.IsEmpty() && !Secret.IsEmpty();

	private async ValueTask ConnectPrivateStreamAsync(CancellationToken cancellationToken)
	{
		_listenKey = await RestClient.CreateListenKeyAsync(IsDerivativesSection, cancellationToken);
		_nextListenKeyRefresh = DateTime.UtcNow + _listenKeyRefreshInterval;

		_privateWsClient = new(BuildPrivateWsEndpoint(WsEndpoint, _listenKey), _workingTime) { Parent = this };
		_privateWsClient.PrivateEventReceived += OnPrivateWsEventAsync;
		_privateWsClient.Error += OnWsErrorAsync;
		await _privateWsClient.ConnectAsync(cancellationToken);
	}

	private void DisconnectPrivateStream()
	{
		if (_privateWsClient is null)
			return;

		_privateWsClient.PrivateEventReceived -= OnPrivateWsEventAsync;
		_privateWsClient.Error -= OnWsErrorAsync;
		_privateWsClient.Disconnect();
		_privateWsClient.Dispose();
		_privateWsClient = null;
		_listenKey = null;
		_nextListenKeyRefresh = default;
	}

	private async ValueTask ReleaseListenKeyAsync(CancellationToken cancellationToken)
	{
		if (_listenKey.IsEmpty() || _restClient is null)
			return;

		try
		{
			await RestClient.DeleteListenKeyAsync(IsDerivativesSection, _listenKey, cancellationToken);
		}
		catch (Exception ex)
		{
			if (!cancellationToken.IsCancellationRequested)
				await SendOutErrorAsync(ex, cancellationToken);
		}
		finally
		{
			_listenKey = null;
			_nextListenKeyRefresh = default;
		}
	}

	private async ValueTask OnPrivateWsEventAsync(JObject payload, CancellationToken cancellationToken)
	{
		var evt = (payload["e"]?.Value<string>() ?? payload["event"]?.Value<string>() ?? payload["eventType"]?.Value<string>())?.ToLowerInvariant();

		switch (evt)
		{
			case "executionreport":
			case "order_trade_update":
				await SendPrivateOrderUpdateAsync(payload, cancellationToken);
				return;

			case "outboundaccountposition":
			case "balanceupdate":
				if (!IsDerivativesSection)
					await SendSpotBalanceUpdateAsync(payload, cancellationToken);
				return;

			case "account_update":
				if (IsDerivativesSection)
					await SendDerivativesAccountUpdateAsync(payload, cancellationToken);
				return;
		}
	}

	private async ValueTask SendPrivateOrderUpdateAsync(JObject payload, CancellationToken cancellationToken)
	{
		var order = payload["o"] as JObject ?? payload["order"] as JObject ?? payload;
		var symbol = order["s"]?.Value<string>() ?? order["symbol"]?.Value<string>();

		if (symbol.IsEmpty())
			return;

		var sideValue = order["S"]?.Value<string>() ?? order["side"]?.Value<string>();

		if (sideValue.IsEmpty())
			return;

		Sides side;

		try
		{
			side = sideValue.ToSide();
		}
		catch
		{
			return;
		}

		var typeValue = order["o"]?.Value<string>() ?? order["ot"]?.Value<string>() ?? order["type"]?.Value<string>();
		var orderType = typeValue.ToOrderType(out var postOnly, out var condition);
		var stopPrice = ParseDecimal(order["sp"] ?? order["stopPrice"] ?? order["triggerPrice"]);

		if (condition is not null && stopPrice is decimal stop)
			condition.ActivationPrice = stop;

		if (condition is not null)
		{
			if (order["R"]?.Value<bool?>() == true || order["reduceOnly"]?.Value<bool?>() == true)
				condition.ReduceOnly = true;

			condition.PositionSide = order["ps"]?.Value<string>() ?? order["positionSide"]?.Value<string>();
		}

		var orderVolume = ParseDecimal(order["q"] ?? order["origQty"] ?? order["quantity"]);
		var filledVolume = ParseDecimal(order["z"] ?? order["executedQty"] ?? order["cumQty"]) ?? 0m;
		var tradeVolume = ParseDecimal(order["l"] ?? order["lastFilledQty"] ?? order["lastQty"]);
		var tradePrice = ParseDecimal(order["L"] ?? order["lastFilledPrice"] ?? order["lastPrice"] ?? order["ap"] ?? order["avgPrice"]);
		var commission = ParseDecimal(order["n"] ?? order["commission"] ?? order["fee"]);
		var orderId = ParseLong(order["i"] ?? order["orderId"]);
		var orderStringId = order["c"]?.Value<string>() ?? order["clientOrderId"]?.Value<string>();
		var status = order["X"]?.Value<string>() ?? order["status"]?.Value<string>();
		var serverTime = ParseWsTime(order["T"] ?? order["t"] ?? order["updateTime"] ?? payload["E"]) ?? CurrentTime;
		var transId = TryExtractTransactionId(orderStringId) ?? 0;
		var reduceOnly = order["R"]?.Value<bool?>() ?? order["reduceOnly"]?.Value<bool?>();

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = serverTime,
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = orderVolume,
			Balance = orderVolume is decimal ov ? (ov - filledVolume).Max(0m) : null,
			OrderPrice = ParseDecimal(order["p"] ?? order["price"]) ?? 0m,
			OrderType = orderType,
			OrderState = status.ToOrderState(),
			Condition = condition,
			TimeInForce = (order["f"]?.Value<string>() ?? order["timeInForce"]?.Value<string>()).ToTimeInForce(out var postOnly2),
			PostOnly = postOnly ?? postOnly2,
			OrderId = orderId,
			OrderStringId = orderStringId,
			TransactionId = transId,
			OriginalTransactionId = transId,
			PositionEffect = reduceOnly is bool ro ? (ro ? OrderPositionEffects.CloseOnly : OrderPositionEffects.Default) : null,
			TradeId = tradeVolume > 0 ? ParseLong(order["t"] ?? order["tradeId"]) : null,
			TradePrice = tradeVolume > 0 ? tradePrice : null,
			TradeVolume = tradeVolume > 0 ? tradeVolume : null,
			Commission = tradeVolume > 0 ? commission : null,
			CommissionCurrency = tradeVolume > 0 ? order["N"]?.Value<string>() ?? order["commissionAsset"]?.Value<string>() ?? order["feeCoin"]?.Value<string>() : null,
		}, cancellationToken);
	}

	private async ValueTask SendSpotBalanceUpdateAsync(JObject payload, CancellationToken cancellationToken)
	{
		var balances = payload["B"] as JArray ?? payload["balances"] as JArray;

		if (balances is null)
			return;

		var serverTime = ParseWsTime(payload["E"] ?? payload["u"]) ?? CurrentTime;

		foreach (var item in balances.OfType<JObject>())
		{
			var asset = item["a"]?.Value<string>() ?? item["asset"]?.Value<string>();

			if (asset.IsEmpty())
				continue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = asset.ToStockSharp(BoardCode),
				ServerTime = serverTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, ParseDecimal(item["f"] ?? item["free"]), true)
			.TryAdd(PositionChangeTypes.BlockedValue, ParseDecimal(item["l"] ?? item["locked"]), true),
			cancellationToken);
		}
	}

	private async ValueTask SendDerivativesAccountUpdateAsync(JObject payload, CancellationToken cancellationToken)
	{
		var account = payload["a"] as JObject ?? payload["account"] as JObject ?? payload;
		var serverTime = ParseWsTime(payload["E"] ?? payload["T"]) ?? CurrentTime;

		foreach (var item in (account["B"] as JArray ?? account["balances"] as JArray ?? new JArray()))
		{
			if (item is not JObject balance)
				continue;

			var asset = balance["a"]?.Value<string>() ?? balance["asset"]?.Value<string>();

			if (asset.IsEmpty())
				continue;

			var wallet = ParseDecimal(balance["wb"] ?? balance["walletBalance"] ?? balance["balance"]);
			var available = ParseDecimal(balance["cw"] ?? balance["crossWalletBalance"] ?? balance["availableBalance"]);

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = asset.ToStockSharp(BoardCode),
				ServerTime = serverTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, wallet, true)
			.TryAdd(PositionChangeTypes.BlockedValue, wallet is decimal wb && available is decimal av ? (wb - av).Max(0m) : null, true),
			cancellationToken);
		}

		foreach (var item in (account["P"] as JArray ?? account["positions"] as JArray ?? new JArray()))
		{
			if (item is not JObject position)
				continue;

			var symbol = position["s"]?.Value<string>() ?? position["symbol"]?.Value<string>();

			if (symbol.IsEmpty())
				continue;

			var positionSide = position["ps"]?.Value<string>() ?? position["positionSide"]?.Value<string>();

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				Side = positionSide.EqualsIgnoreCase("BOTH") || positionSide.IsEmpty() ? null : positionSide.ToSide(),
			}
			.TryAdd(PositionChangeTypes.CurrentValue, ParseDecimal(position["pa"] ?? position["positionAmt"] ?? position["size"]), true)
			.TryAdd(PositionChangeTypes.AveragePrice, ParseDecimal(position["ep"] ?? position["entryPrice"] ?? position["openPrice"]), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, ParseDecimal(position["up"] ?? position["unrealizedPnl"] ?? position["unrealizedProfit"]), true),
			cancellationToken);
		}
	}

	private async ValueTask OnWsTickerAsync(JObject ticker, CancellationToken cancellationToken)
	{
		var symbol = ticker["s"]?.Value<string>() ?? ticker["symbol"]?.Value<string>();

		if (symbol.IsEmpty())
			return;

		long[] subscriptions;

		using (_sync.EnterScope())
		{
			subscriptions = [.. _level1Realtime
				.Where(p => p.Value.EqualsIgnoreCase(symbol))
				.Select(static p => p.Key)];
		}

		if (subscriptions.Length == 0)
			return;

		var last = ticker["c"]?.Value<string>().To<decimal?>() ?? ticker["lastPrice"]?.Value<string>().To<decimal?>();
		var open = ticker["o"]?.Value<string>().To<decimal?>() ?? ticker["openPrice"]?.Value<string>().To<decimal?>();
		var serverTime = ParseWsTime(ticker["E"] ?? ticker["T"]) ?? CurrentTime;

		foreach (var transactionId in subscriptions)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(Level1Fields.LastTradePrice, last)
			.TryAdd(Level1Fields.OpenPrice, open)
			.TryAdd(Level1Fields.HighPrice, ticker["h"]?.Value<string>().To<decimal?>() ?? ticker["highPrice"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.LowPrice, ticker["l"]?.Value<string>().To<decimal?>() ?? ticker["lowPrice"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.Volume, ticker["v"]?.Value<string>().To<decimal?>() ?? ticker["volume"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.BestBidPrice, ticker["b"]?.Value<string>().To<decimal?>() ?? ticker["bidPrice"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.BestBidVolume, ticker["B"]?.Value<string>().To<decimal?>() ?? ticker["bidQty"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.BestAskPrice, ticker["a"]?.Value<string>().To<decimal?>() ?? ticker["askPrice"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.BestAskVolume, ticker["A"]?.Value<string>().To<decimal?>() ?? ticker["askQty"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.Change, last is decimal l && open is decimal o ? l - o : null),
			cancellationToken);
		}
	}

	private async ValueTask OnWsDepthAsync(JObject depth, CancellationToken cancellationToken)
	{
		var symbol = depth["s"]?.Value<string>() ?? depth["symbol"]?.Value<string>();

		if (symbol.IsEmpty())
			return;

		(long TransactionId, int? MaxDepth)[] subscriptions;

		using (_sync.EnterScope())
		{
			subscriptions = [.. _depthRealtime
				.Where(p => p.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(p => (p.Key, p.Value.MaxDepth))];
		}

		if (subscriptions.Length == 0)
			return;

		var bids = ToDepthEntries(depth["b"] ?? depth["bids"]);
		var asks = ToDepthEntries(depth["a"] ?? depth["asks"]);
		var serverTime = ParseWsTime(depth["E"] ?? depth["T"]) ?? CurrentTime;
		var seqNum = depth["u"]?.Value<long?>() ?? depth["lastUpdateId"]?.Value<long?>() ?? 0;

		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				OriginalTransactionId = subscription.TransactionId,
				State = QuoteChangeStates.SnapshotComplete,
				Bids = ToQuotes(bids, subscription.MaxDepth),
				Asks = ToQuotes(asks, subscription.MaxDepth),
				SeqNum = seqNum,
			}, cancellationToken);
		}
	}

	private async ValueTask OnWsTradeAsync(JObject trade, CancellationToken cancellationToken)
	{
		var symbol = trade["s"]?.Value<string>() ?? trade["symbol"]?.Value<string>();

		if (symbol.IsEmpty())
			return;

		long[] subscriptions;

		using (_sync.EnterScope())
		{
			subscriptions = [.. _ticksRealtime
				.Where(p => p.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static p => p.Key)];
		}

		if (subscriptions.Length == 0)
			return;

		var serverTime = ParseWsTime(trade["T"] ?? trade["E"]) ?? CurrentTime;
		var tradeId = trade["a"]?.Value<long?>() ?? trade["id"]?.Value<long?>() ?? trade["tradeId"]?.Value<long?>();

		foreach (var transactionId in subscriptions)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				TradeId = tradeId,
				TradePrice = trade["p"]?.Value<string>().To<decimal?>() ?? trade["price"]?.Value<string>().To<decimal?>(),
				TradeVolume = trade["q"]?.Value<string>().To<decimal?>() ?? trade["quantity"]?.Value<string>().To<decimal?>() ?? trade["v"]?.Value<string>().To<decimal?>(),
				OriginSide = trade["m"]?.Value<bool?>() == true ? Sides.Sell : Sides.Buy,
				OriginalTransactionId = transactionId,
			}, cancellationToken);
		}
	}

	private async ValueTask OnWsCandleAsync(JObject payload, CancellationToken cancellationToken)
	{
		var kline = payload["k"] as JObject ?? payload;
		var symbol = kline["s"]?.Value<string>() ?? payload["s"]?.Value<string>();
		var interval = kline["i"]?.Value<string>() ?? kline["interval"]?.Value<string>();

		if (symbol.IsEmpty() || interval.IsEmpty())
			return;

		TimeSpan timeFrame;

		try
		{
			timeFrame = interval.ToTimeFrame();
		}
		catch
		{
			return;
		}

		long[] subscriptions;

		using (_sync.EnterScope())
		{
			subscriptions = [.. _candlesRealtime
				.Where(p => p.Value.Symbol.EqualsIgnoreCase(symbol) && p.Value.TimeFrame == timeFrame)
				.Select(static p => p.Key)];
		}

		if (subscriptions.Length == 0)
			return;

		var openTime = ParseWsTime(kline["t"] ?? kline["openTime"]) ?? CurrentTime;
		var closeTime = ParseWsTime(kline["T"] ?? kline["closeTime"]) ?? openTime + timeFrame;
		var state = kline["x"]?.Value<bool?>() == true ? CandleStates.Finished : CandleStates.Active;

		foreach (var transactionId in subscriptions)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = symbol.ToStockSharp(BoardCode),
				TypedArg = timeFrame,
				OpenTime = openTime,
				CloseTime = closeTime,
				OpenPrice = kline["o"]?.Value<string>().To<decimal?>() ?? kline["open"]?.Value<string>().To<decimal?>() ?? 0m,
				HighPrice = kline["h"]?.Value<string>().To<decimal?>() ?? kline["high"]?.Value<string>().To<decimal?>() ?? 0m,
				LowPrice = kline["l"]?.Value<string>().To<decimal?>() ?? kline["low"]?.Value<string>().To<decimal?>() ?? 0m,
				ClosePrice = kline["c"]?.Value<string>().To<decimal?>() ?? kline["close"]?.Value<string>().To<decimal?>() ?? 0m,
				TotalVolume = kline["v"]?.Value<string>().To<decimal?>() ?? kline["volume"]?.Value<string>().To<decimal?>() ?? 0m,
				State = state,
				OriginalTransactionId = transactionId,
			}, cancellationToken);
		}
	}

	private static DateTime? ParseWsTime(JToken token)
	{
		var unix = token?.Value<long?>();
		return unix is long ts && ts > 0 ? ts.FromUnix(false) : null;
	}

	private static decimal? ParseDecimal(JToken token)
	{
		if (token is null)
			return null;

		return token.ToString().To<decimal?>();
	}

	private static long? ParseLong(JToken token)
	{
		if (token is null)
			return null;

		return token.ToString().To<long?>();
	}

	private static string[][] ToDepthEntries(JToken token)
	{
		if (token is not JArray array || array.Count == 0)
			return [];

		var result = new List<string[]>(array.Count);

		foreach (var item in array)
		{
			switch (item)
			{
				case JArray tuple when tuple.Count >= 2:
					result.Add([tuple[0]?.ToString(), tuple[1]?.ToString()]);
					break;

				case JObject obj:
				{
					var price = obj["price"]?.Value<string>() ?? obj["p"]?.Value<string>();
					var volume = obj["size"]?.Value<string>() ?? obj["qty"]?.Value<string>() ?? obj["q"]?.Value<string>();

					if (!price.IsEmpty() && !volume.IsEmpty())
						result.Add([price, volume]);

					break;
				}
			}
		}

		return [.. result];
	}

	private static bool AddRef<TKey>(IDictionary<TKey, int> refs, TKey key)
	{
		if (refs.TryGetValue(key, out var count))
		{
			refs[key] = count + 1;
			return false;
		}

		refs[key] = 1;
		return true;
	}

	private static bool ReleaseRef<TKey>(IDictionary<TKey, int> refs, TKey key)
	{
		if (!refs.TryGetValue(key, out var count))
			return false;

		if (count <= 1)
		{
			refs.Remove(key);
			return true;
		}

		refs[key] = count - 1;
		return false;
	}

	private async ValueTask PublishLevel1Async(string symbol, long transactionId, CancellationToken cancellationToken)
	{
		var bookTicker = await RestClient.GetBookTickerAsync(symbol, cancellationToken);
		var ticker24 = await RestClient.GetTicker24HrAsync(symbol, cancellationToken);
		var last = ticker24?["lastPrice"]?.Value<string>().To<decimal?>();
		var open = ticker24?["openPrice"]?.Value<string>().To<decimal?>();

		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, last)
		.TryAdd(Level1Fields.OpenPrice, open)
		.TryAdd(Level1Fields.HighPrice, ticker24?["highPrice"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.LowPrice, ticker24?["lowPrice"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.Volume, ticker24?["volume"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.BestBidPrice, bookTicker?["bidPrice"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.BestBidVolume, bookTicker?["bidQty"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.BestAskPrice, bookTicker?["askPrice"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.BestAskVolume, bookTicker?["askQty"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.Change, last is decimal l && open is decimal o ? l - o : null),
		cancellationToken);
	}

	private async ValueTask PublishDepthAsync(string symbol, int? maxDepth, long transactionId, CancellationToken cancellationToken)
	{
		var depth = await RestClient.GetDepthAsync(symbol, maxDepth ?? 50, cancellationToken);

		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(depth?.Bids, maxDepth),
			Asks = ToQuotes(depth?.Asks, maxDepth),
			SeqNum = depth?.LastUpdateId ?? 0,
		}, cancellationToken);
	}

	private async ValueTask PollTicksAsync(TickRealtimeState state, CancellationToken cancellationToken)
	{
		var from = state.LastTime == default ? DateTime.UtcNow - TimeSpan.FromMinutes(5) : state.LastTime.AddMilliseconds(-1);
		var to = DateTime.UtcNow;
		var trades = await RestClient.GetAggTradesAsync(state.Symbol, null, from, to, 200, cancellationToken);
		var lastTime = state.LastTime;
		var lastTradeId = state.LastTradeId;

		foreach (var trade in trades.OrderBy(static t => t.TradeTime ?? t.Time ?? 0))
		{
			var unix = trade.TradeTime ?? trade.Time;

			if (unix is null)
				continue;

			var tradeId = trade.AggregateId ?? trade.Id;

			if (lastTradeId is long lid && tradeId is long tid && tid <= lid)
				continue;

			var tradeTime = unix.Value.FromUnix(false);

			if (tradeTime <= lastTime)
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = state.Symbol.ToStockSharp(BoardCode),
				ServerTime = tradeTime,
				TradeId = tradeId,
				TradePrice = trade.Price.To<decimal?>(),
				TradeVolume = trade.Quantity.To<decimal?>(),
				OriginSide = trade.IsBuyerMaker == true ? Sides.Sell : Sides.Buy,
				OriginalTransactionId = state.TransactionId,
			}, cancellationToken);

			lastTime = tradeTime;
			lastTradeId = tradeId;
		}

		using (_sync.EnterScope())
		{
			if (_ticksRealtime.TryGetValue(state.TransactionId, out var saved))
			{
				saved.LastTime = lastTime;
				saved.LastTradeId = lastTradeId;
			}
		}
	}

	private async ValueTask PollCandlesAsync(CandleRealtimeState state, CancellationToken cancellationToken)
	{
		var interval = state.TimeFrame.ToNative();
		var from = state.LastOpenTime == default
			? DateTime.UtcNow - TimeSpan.FromTicks(state.TimeFrame.Ticks * 5)
			: state.LastOpenTime - state.TimeFrame;
		var to = DateTime.UtcNow;
		var candles = await RestClient.GetCandlesAsync(state.Symbol, interval, from, to, 200, cancellationToken);
		var lastOpenTime = state.LastOpenTime;

		foreach (var item in candles.OfType<JArray>().OrderBy(static c => c[0]?.Value<long>() ?? 0))
		{
			var openTimeUnix = item[0]?.Value<long?>() ?? 0;
			var openTime = openTimeUnix.FromUnix(false);

			if (openTime <= state.LastOpenTime)
				continue;

			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = state.Symbol.ToStockSharp(BoardCode),
				TypedArg = state.TimeFrame,
				OpenTime = openTime,
				OpenPrice = item[1]?.Value<string>().To<decimal?>() ?? 0m,
				HighPrice = item[2]?.Value<string>().To<decimal?>() ?? 0m,
				LowPrice = item[3]?.Value<string>().To<decimal?>() ?? 0m,
				ClosePrice = item[4]?.Value<string>().To<decimal?>() ?? 0m,
				TotalVolume = item[5]?.Value<string>().To<decimal?>() ?? 0m,
				State = CandleStates.Finished,
				OriginalTransactionId = state.TransactionId,
			}, cancellationToken);

			lastOpenTime = openTime;
		}

		using (_sync.EnterScope())
		{
			if (_candlesRealtime.TryGetValue(state.TransactionId, out var saved))
				saved.LastOpenTime = lastOpenTime;
		}
	}

	protected abstract ValueTask SendSectionPortfolioAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken);

	private static QuoteChange[] ToQuotes(string[][] entries, int? maxDepth)
	{
		if (entries is null || entries.Length == 0)
			return [];

		var depth = 1.Max(maxDepth ?? int.MaxValue);
		var list = new List<QuoteChange>(depth.Min(entries.Length));

		foreach (var entry in entries.Take(depth))
		{
			if (entry is not { Length: >= 2 })
				continue;

			var price = entry[0].To<decimal?>();
			var volume = entry[1].To<decimal?>();

			if (price is decimal p && volume is decimal v)
				list.Add(new QuoteChange(p, v));
		}

		return [.. list];
	}

	private static string CreateApiBaseUri(string endpoint, string prefix)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		var normalized = endpoint.Trim().TrimEnd('/');
		var normalizedPrefix = prefix.IsEmpty() ? string.Empty : "/" + prefix.Trim('/');
		return normalized + normalizedPrefix;
	}

	private static string BuildPrivateWsEndpoint(string publicWsEndpoint, string listenKey)
	{
		if (publicWsEndpoint.IsEmpty())
			throw new ArgumentNullException(nameof(publicWsEndpoint));

		if (listenKey.IsEmpty())
			throw new ArgumentNullException(nameof(listenKey));

		var endpoint = publicWsEndpoint.Trim();

		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";

		var uri = endpoint.To<Uri>();
		var path = uri.AbsolutePath.IsEmpty() || uri.AbsolutePath == "/"
			? "/ws"
			: uri.AbsolutePath.TrimEnd('/');

		if (!path.EndsWith("/ws", StringComparison.OrdinalIgnoreCase))
			path += "/ws";

		path += "/" + listenKey;

		return new UriBuilder(uri)
		{
			Path = path,
			Query = string.Empty,
		}.Uri.ToString().TrimEnd('/');
	}

	protected void EnsurePrivateReady()
	{
		if (Key.IsEmpty() || Secret.IsEmpty())
			throw new InvalidOperationException("Key/Secret are required for private operations.");
	}

	private Task<OrderInfo> RegisterInternalAsync(
		string symbol,
		Sides side,
		OrderTypes? orderType,
		decimal price,
		decimal volume,
		TimeInForce? timeInForce,
		bool? postOnly,
		AsterOrderCondition condition,
		OrderPositionEffects? positionEffect,
		string clientOrderId,
		CancellationToken cancellationToken)
	{
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");

		string type;
		decimal? orderPrice;
		decimal? stopPrice = null;

		switch (orderType ?? OrderTypes.Limit)
		{
			case OrderTypes.Limit:
				type = "LIMIT";
				orderPrice = price;

				if (orderPrice is null || orderPrice <= 0)
					throw new InvalidOperationException("Limit order price must be positive.");
				break;

			case OrderTypes.Market:
				type = "MARKET";
				orderPrice = null;
				break;

			case OrderTypes.Conditional:
			{
				if (condition is null)
					throw new InvalidOperationException("Conditional order requires AsterOrderCondition.");

				stopPrice = condition.ActivationPrice ?? price;

				if (stopPrice is null || stopPrice <= 0)
					throw new InvalidOperationException("Conditional order activation price must be positive.");

				var isMarket = condition.IsMarket || condition.ClosePositionPrice is null;
				orderPrice = condition.ClosePositionPrice;

				type = condition.Type == AsterOrderConditionTypes.TakeProfit
					? (isMarket ? "TAKE_PROFIT_MARKET" : "TAKE_PROFIT")
					: (isMarket ? "STOP_MARKET" : "STOP");

				break;
			}

			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		}

		if (postOnly == true && type == "LIMIT")
			type = "LIMIT_MAKER";

		var tif = type is "LIMIT" or "TAKE_PROFIT" or "STOP"
			? timeInForce.ToNative(postOnly)
			: null;

		var reduceOnly = positionEffect == OrderPositionEffects.CloseOnly || condition?.ReduceOnly == true;
		var positionSide = condition?.PositionSide;

		return RestClient.RegisterOrderAsync(
			symbol,
			side.ToNative(),
			type,
			tif,
			orderPrice,
			volume,
			clientOrderId,
			stopPrice,
			IsDerivativesSection ? reduceOnly : null,
			IsDerivativesSection ? positionSide : null,
			cancellationToken);
	}

	private static string CreateClientId(long transactionId, string explicitClientId)
	{
		if (!explicitClientId.IsEmpty())
			return explicitClientId;

		return $"x-ss-{transactionId}";
	}

	private static bool IsStopOrderType(string type)
	{
		var upper = type?.ToUpperInvariant();

		return upper is "STOP" or "STOP_LOSS" or "STOP_LOSS_LIMIT" or "STOP_MARKET" or "TAKE_PROFIT" or "TAKE_PROFIT_LIMIT" or "TAKE_PROFIT_MARKET";
	}

	private static long? TryExtractTransactionId(string clientOrderId)
	{
		if (clientOrderId.IsEmpty())
			return null;

		var idx = clientOrderId.LastIndexOf('-');
		var value = idx >= 0 ? clientOrderId[(idx + 1)..] : clientOrderId;

		return long.TryParse(value, out var transId) ? transId : null;
	}
}
