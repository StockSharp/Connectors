namespace StockSharp.EdgeX.Native.Common;

using StockSharp.EdgeX.Native.Derivatives.Model;
using StockSharp.EdgeX.Native.Spot.Model;

abstract class EdgeXSectionAdapter : BaseNativeAdapter
{
	private const int _maxHistoryLimit = 200;

	private EdgeXRestClient _restClient;
	private EdgeXWsClient _wsClient;
	private EdgeXWsClient _privateWsClient;
	private string _portfolioName;
	private readonly Lock _sync = new();
	private readonly Dictionary<long, (string Symbol, string ContractId)> _level1Realtime = [];
	private readonly Dictionary<long, (string Symbol, string ContractId, int? Depth)> _depthRealtime = [];
	private readonly Dictionary<long, TickState> _ticksRealtime = [];
	private readonly Dictionary<long, CandleState> _candlesRealtime = [];
	private readonly Dictionary<string, int> _level1Refs = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, int> _depthRefs = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, int> _ticksRefs = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<(string ContractId, TimeSpan TimeFrame), int> _candleRefs = [];

	private readonly Dictionary<string, Contract> _contractsById = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, Contract> _contractsBySymbol = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, Coin> _coinsById = new(StringComparer.InvariantCultureIgnoreCase);
	private string _collateralCoin = "USD";
	private readonly string _restEndpoint;
	private readonly string _clearingAccount;
	private readonly SecureString _passphrase;
	private readonly bool _isSpotSection;
	private readonly bool _supportsTrading;
	private readonly bool _supportsPrivateData = true;
	private readonly string _wsEndpoint;
	private readonly string _privateWsEndpoint;
	private static readonly string[] _privateChannels = ["order-event", "trade-event", "asset-event", "position-event"];

	private sealed class TickState
	{
		public string Symbol { get; init; }
		public string ContractId { get; init; }
		public long TransactionId { get; init; }
		public DateTime LastTime { get; set; }
		public long LastTradeId { get; set; }
	}

	private sealed class CandleState
	{
		public string Symbol { get; init; }
		public string ContractId { get; init; }
		public long TransactionId { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
	}

	protected EdgeXSectionAdapter(
		SecureString key,
		SecureString secret,
		string clearingAccount,
		SecureString passphrase,
		string boardCode,
		SecurityTypes securityType,
		string sectionName,
		bool isSpotSection,
		bool supportsTrading,
		string restEndpoint,
		string wsEndpoint,
		string privateWsEndpoint,
		WorkingTime workingTime)
		: base(key, secret, boardCode, securityType, sectionName)
	{
		_clearingAccount = clearingAccount;
		_passphrase = passphrase;
		_isSpotSection = isSpotSection;
		_supportsTrading = supportsTrading;
		_restEndpoint = restEndpoint.ThrowIfEmpty(nameof(restEndpoint));
		_wsEndpoint = wsEndpoint.ThrowIfEmpty(nameof(wsEndpoint));
		_privateWsEndpoint = privateWsEndpoint;
		_workingTime = workingTime;
	}

	private readonly WorkingTime _workingTime;

	protected bool IsSpotSection => _isSpotSection;
	protected bool SupportsTrading => _supportsTrading;
	protected bool SupportsPrivateData => _supportsPrivateData;

	protected EdgeXRestClient RestClient => _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	protected string PortfolioName => _portfolioName;
	protected IReadOnlyDictionary<string, Contract> ContractsById => _contractsById;
	protected IReadOnlyDictionary<string, Contract> ContractsBySymbol => _contractsBySymbol;
	protected IReadOnlyDictionary<string, Coin> CoinsById => _coinsById;
	protected string CollateralCoin => _collateralCoin;
	protected EdgeXWsClient WsClient => _wsClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	public override async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await base.ConnectAsync(cancellationToken);

		_restClient = new(_restEndpoint, Key, Secret, _clearingAccount, _passphrase) { Parent = this };
		_wsClient = new(_wsEndpoint, _workingTime) { Parent = this };
		_wsClient.TickerReceived += OnWsTickerAsync;
		_wsClient.DepthReceived += OnWsDepthAsync;
		_wsClient.TradeReceived += OnWsTradeAsync;
		_wsClient.CandleReceived += OnWsCandleAsync;
		_wsClient.Error += OnWsErrorAsync;
		_portfolioName = $"{nameof(EdgeX)}_{SectionName}_{(Key.IsEmpty() ? "Public" : Key.ToId())}";

		await EnsureMetadataAsync(cancellationToken);
		ClearRealtimeSubscriptions();
		await _wsClient.ConnectAsync(cancellationToken);

		if (CanUsePrivateWs())
			await ConnectPrivateWsAsync(cancellationToken);
	}

	public override void Disconnect()
	{
		base.Disconnect();

		DisconnectPrivateWs();

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

	public override ValueTask ResetAsync(CancellationToken cancellationToken)
	{
		Disconnect();
		ClearMetadataCache();
		return base.ResetAsync(cancellationToken);
	}

	public override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> default;

	public override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await EnsureMetadataAsync(cancellationToken);

		var secTypes = lookupMsg.GetSecurityTypes();
		if (secTypes.Count > 0 && !secTypes.Contains(SecurityType))
			return;

		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var contract in EnumerateSectionContracts())
		{
			var secMsg = new SecurityMessage
			{
				SecurityId = (contract.Name ?? contract.Id).ToStockSharp(BoardCode),
				Name = contract.Name,
				SecurityType = SecurityType,
				PriceStep = contract.TickSize.To<decimal?>(),
				VolumeStep = contract.StepSize.To<decimal?>(),
				MinVolume = contract.MinOrderSize.To<decimal?>(),
				MaxVolume = contract.MaxOrderSize.To<decimal?>(),
				OriginalTransactionId = lookupMsg.TransactionId,
			};

			if (_coinsById.TryGetValue(contract.BaseCoinId ?? string.Empty, out var baseCoin) && !baseCoin?.Name.IsEmpty() == true)
				secMsg.TryFillUnderlyingId(baseCoin.Name.ToUpperInvariant());

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
			await UnregisterLevel1Async(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var (symbol, contractId) = ResolveSymbolAndContract(mdMsg.SecurityId);
		await PublishLevel1Async(symbol, contractId, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await RegisterLevel1Async(mdMsg.TransactionId, symbol, contractId, cancellationToken);
	}

	public override async ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnregisterDepthAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var (symbol, contractId) = ResolveSymbolAndContract(mdMsg.SecurityId);
		await PublishDepthAsync(symbol, contractId, mdMsg.MaxDepth, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await RegisterDepthAsync(mdMsg.TransactionId, symbol, contractId, mdMsg.MaxDepth, cancellationToken);
	}

	public override async ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnregisterTicksAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var (symbol, contractId) = ResolveSymbolAndContract(mdMsg.SecurityId);
		var from = mdMsg.From ?? (DateTime.UtcNow - TimeSpan.FromHours(3));
		var to = mdMsg.To ?? DateTime.UtcNow;
		var left = mdMsg.Count ?? long.MaxValue;
		var lastTime = from;
		var lastTradeId = 0L;

		var kline = await RestClient.GetKlineAsync(contractId, TimeSpan.FromMinutes(1).ToNativeKlineType(), "LAST_PRICE", _maxHistoryLimit, from, to, cancellationToken);
		foreach (var row in GetKlineRows(kline).OrderBy(static r => r["klineTime"]?.Value<long>() ?? 0))
		{
			var time = ParseUnixMs(row["klineTime"]) ?? ParseUnixMs(row["endTime"]) ?? CurrentTime;
			if (time < from)
				continue;
			if (time > to)
				break;

			var tradeId = row["klineId"]?.Value<long?>() ?? (long)time.ToUnix(false);
			if (tradeId <= lastTradeId)
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = time,
				TradeId = tradeId,
				TradePrice = row["close"]?.Value<string>().To<decimal?>(),
				TradeVolume = row["size"]?.Value<string>().To<decimal?>(),
				OriginalTransactionId = mdMsg.TransactionId,
			}, cancellationToken);

			lastTime = time;
			lastTradeId = tradeId;

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await RegisterTicksAsync(mdMsg.TransactionId, symbol, contractId, lastTime, lastTradeId, cancellationToken);
	}

	public override async ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnregisterCandlesAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var (symbol, contractId) = ResolveSymbolAndContract(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var from = mdMsg.From ?? (DateTime.UtcNow - TimeSpan.FromDays(1));
		var to = mdMsg.To ?? DateTime.UtcNow;
		var left = mdMsg.Count ?? long.MaxValue;
		var lastOpenTime = from;

		var kline = await RestClient.GetKlineAsync(contractId, timeFrame.ToNativeKlineType(), "LAST_PRICE", _maxHistoryLimit, from, to, cancellationToken);
		foreach (var row in GetKlineRows(kline).OrderBy(static r => r["klineTime"]?.Value<long>() ?? 0))
		{
			var openTime = ParseUnixMs(row["klineTime"]) ?? CurrentTime;
			if (openTime < from)
				continue;
			if (openTime > to)
				break;

			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = symbol.ToStockSharp(BoardCode),
				TypedArg = timeFrame,
				OpenTime = openTime,
				OpenPrice = row["open"]?.Value<string>().To<decimal?>() ?? 0m,
				HighPrice = row["high"]?.Value<string>().To<decimal?>() ?? 0m,
				LowPrice = row["low"]?.Value<string>().To<decimal?>() ?? 0m,
				ClosePrice = row["close"]?.Value<string>().To<decimal?>() ?? 0m,
				TotalVolume = row["size"]?.Value<string>().To<decimal?>() ?? 0m,
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
			await RegisterCandlesAsync(mdMsg.TransactionId, symbol, contractId, timeFrame, lastOpenTime, cancellationToken);
	}

	public override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		var (symbol, contractId) = ResolveSymbolAndContract(regMsg.SecurityId);
		var payload = BuildRegisterPayload(regMsg, contractId);
		var response = await RestClient.CreateOrderAsync(payload, cancellationToken);
		var data = response["data"] as JObject ?? response;

		var orderId = data["orderId"]?.Value<long?>();
		var orderStringId = data["clientOrderId"]?.Value<string>() ?? payload["clientOrderId"]?.Value<string>();
		var status = data["status"]?.Value<string>();
		var serverTime = ParseUnixMs(data["createdTime"] ?? data["transactTime"]) ?? CurrentTime;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = serverTime,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = regMsg.Volume.Abs(),
			Balance = regMsg.Volume.Abs(),
			OrderPrice = payload["price"]?.Value<string>().To<decimal?>() ?? regMsg.Price,
			OrderType = regMsg.OrderType,
			OrderState = status.ToOrderState(),
			OrderId = orderId,
			OrderStringId = orderStringId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = (payload["timeInForce"]?.Value<string>()).ToTimeInForce(),
			Condition = regMsg.Condition,
			PositionEffect = regMsg.PositionEffect,
		}, cancellationToken);
	}

	public override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		await CancelOrderAsync(new OrderCancelMessage
		{
			TransactionId = replaceMsg.TransactionId,
			SecurityId = replaceMsg.SecurityId,
			OrderId = replaceMsg.OldOrderId,
			OrderStringId = replaceMsg.OldOrderStringId,
		}, cancellationToken);

		await RegisterOrderAsync(new OrderRegisterMessage
		{
			TransactionId = replaceMsg.TransactionId,
			SecurityId = replaceMsg.SecurityId,
			Side = replaceMsg.Side,
			Price = replaceMsg.Price,
			Volume = replaceMsg.Volume,
			OrderType = replaceMsg.OrderType,
			TimeInForce = replaceMsg.TimeInForce,
			PostOnly = replaceMsg.PostOnly,
			Condition = replaceMsg.Condition,
			PositionEffect = replaceMsg.PositionEffect,
			UserOrderId = replaceMsg.UserOrderId,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		var accountId = GetAccountId();
		var (symbol, _) = ResolveSymbolAndContract(cancelMsg.SecurityId);

		if (cancelMsg.OrderId is long orderId)
			await RestClient.CancelOrderByIdAsync(accountId, [orderId.ToString()], cancellationToken);
		else if (!cancelMsg.OrderStringId.IsEmpty())
			await RestClient.CancelOrderByClientIdAsync(accountId, [cancelMsg.OrderStringId], cancellationToken);
		else
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.TransactionId));

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = CurrentTime,
			PortfolioName = _portfolioName,
			OrderId = cancelMsg.OrderId,
			OrderStringId = cancelMsg.OrderStringId,
			OrderState = OrderStates.Done,
			Balance = 0,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();

		if ((cancelMsg.Mode & OrderGroupCancelModes.ClosePositions) == OrderGroupCancelModes.ClosePositions)
			throw new NotSupportedException("ClosePositions mode is not supported by edgeX REST API.");

		var accountId = GetAccountId();
		var contractId = cancelMsg.SecurityId.SecurityCode.IsEmpty() ? null : ResolveContractId(cancelMsg.SecurityId);
		var active = await RestClient.GetActiveOrdersAsync(accountId, contractId, 200, null, null, cancellationToken);

		foreach (var item in GetOrderItems(active))
		{
			var side = item["side"]?.Value<string>();
			if (cancelMsg.Side is Sides sf && !side.IsEmpty() && side.ToSide() != sf)
				continue;

			var type = item["type"]?.Value<string>();
			if (cancelMsg.IsStop is bool isStop)
			{
				var stop = type?.ContainsIgnoreCase("STOP") == true || type?.ContainsIgnoreCase("TAKE_PROFIT") == true;
				if (stop != isStop)
					continue;
			}

			var orderId = item["orderId"]?.Value<string>();
			if (orderId.IsEmpty())
				continue;

			await RestClient.CancelOrderByIdAsync(accountId, [orderId], cancellationToken);
		}
	}

	public override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (!lookupMsg.IsSubscribe)
			return;

		EnsurePrivateDataReady();

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCode,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);

		var asset = await RestClient.GetAccountAssetAsync(GetAccountId(), cancellationToken);
		var data = asset["data"] as JObject ?? asset;
		var serverTime = ParseUnixMs(asset["responseTime"] ?? data["updateTime"]) ?? CurrentTime;

		foreach (var collateral in ExtractObjects(data, "collateralAssetList", "assetList", "collateralList", "balances"))
		{
			var coinName = ResolveCoinName(collateral["coinName"]?.Value<string>(), collateral["coinId"]?.Value<string>(), collateral["asset"]?.Value<string>());
			if (coinName.IsEmpty())
				continue;

			var total = collateral["totalBalance"]?.Value<string>().To<decimal?>()
				?? collateral["balance"]?.Value<string>().To<decimal?>()
				?? collateral["walletBalance"]?.Value<string>().To<decimal?>();
			var available = collateral["availableBalance"]?.Value<string>().To<decimal?>()
				?? collateral["free"]?.Value<string>().To<decimal?>();

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = coinName.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, total, true)
			.TryAdd(PositionChangeTypes.BlockedValue, total is decimal t && available is decimal a ? (t - a).Max(0m) : null, true),
			cancellationToken);
		}

		if (!IsSpotSection)
		{
			foreach (var position in ExtractObjects(data, "positionAssetList", "positionList", "positions"))
			{
				var symbol = ResolveOrderSymbol(position);
				if (symbol.IsEmpty())
					continue;

				var qty = position["position"]?.Value<string>().To<decimal?>()
					?? position["size"]?.Value<string>().To<decimal?>()
					?? position["positionSize"]?.Value<string>().To<decimal?>();

				var side = position["side"]?.Value<string>();
				if (qty is decimal q && q < 0)
					side = "SELL";

				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = _portfolioName,
					SecurityId = symbol.ToStockSharp(BoardCode),
					ServerTime = serverTime,
					OriginalTransactionId = lookupMsg.TransactionId,
					Side = side.IsEmpty() ? null : side.ToSide(),
				}
				.TryAdd(PositionChangeTypes.CurrentValue, qty, true)
				.TryAdd(PositionChangeTypes.AveragePrice, position["entryPrice"]?.Value<string>().To<decimal?>() ?? position["openPrice"]?.Value<string>().To<decimal?>(), true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, position["unrealizedPnl"]?.Value<string>().To<decimal?>() ?? position["unrealizedProfit"]?.Value<string>().To<decimal?>(), true)
				.TryAdd(PositionChangeTypes.Leverage, position["leverage"]?.Value<string>().To<decimal?>(), true),
				cancellationToken);
			}
		}

	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (!statusMsg.IsSubscribe)
			return;

		EnsurePrivateDataReady();
		var accountId = GetAccountId();
		var contractId = statusMsg.SecurityId.SecurityCode.IsEmpty() ? null : ResolveContractId(statusMsg.SecurityId);
		var active = await RestClient.GetActiveOrdersAsync(accountId, contractId, statusMsg.Count?.To<int?>(), statusMsg.From, statusMsg.To, cancellationToken);
		var fills = await RestClient.GetFillTransactionsAsync(accountId, contractId, statusMsg.Count?.To<int?>(), statusMsg.From, statusMsg.To, cancellationToken);

		foreach (var order in GetOrderItems(active).OrderBy(static o => o["createdTime"]?.Value<long>() ?? 0))
		{
			var symbol = ResolveOrderSymbol(order);
			if (symbol.IsEmpty())
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = ParseUnixMs(order["createdTime"] ?? order["updatedTime"]) ?? CurrentTime,
				PortfolioName = _portfolioName,
				Side = (order["side"]?.Value<string>() ?? "BUY").ToSide(),
				OrderVolume = order["size"]?.Value<string>().To<decimal?>() ?? order["origSize"]?.Value<string>().To<decimal?>(),
				Balance = order["remainSize"]?.Value<string>().To<decimal?>(),
				OrderPrice = order["price"]?.Value<string>().To<decimal?>() ?? 0m,
				OrderType = (order["type"]?.Value<string>()).ToOrderType(),
				OrderState = (order["status"]?.Value<string>()).ToOrderState(),
				OrderId = order["orderId"]?.Value<long?>(),
				OrderStringId = order["clientOrderId"]?.Value<string>(),
				TimeInForce = (order["timeInForce"]?.Value<string>()).ToTimeInForce(),
				OriginalTransactionId = statusMsg.TransactionId,
			}, cancellationToken);
		}

		foreach (var fill in GetFillItems(fills).OrderBy(static f => f["createdTime"]?.Value<long>() ?? 0))
		{
			var symbol = ResolveOrderSymbol(fill);
			if (symbol.IsEmpty())
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = ParseUnixMs(fill["createdTime"] ?? fill["tradeTime"]) ?? CurrentTime,
				PortfolioName = _portfolioName,
				OrderId = fill["orderId"]?.Value<long?>(),
				TradeId = fill["fillId"]?.Value<long?>() ?? fill["tradeId"]?.Value<long?>(),
				TradePrice = fill["price"]?.Value<string>().To<decimal?>(),
				TradeVolume = fill["size"]?.Value<string>().To<decimal?>() ?? fill["fillSize"]?.Value<string>().To<decimal?>(),
				Commission = fill["fee"]?.Value<string>().To<decimal?>(),
				CommissionCurrency = ResolveCoinName(fill["feeCoinName"]?.Value<string>(), fill["feeCoinId"]?.Value<string>(), null),
				Side = (fill["side"]?.Value<string>() ?? "BUY").ToSide(),
				OriginalTransactionId = statusMsg.TransactionId,
			}, cancellationToken);
		}

	}

	protected abstract IEnumerable<Contract> EnumerateSectionContracts();

	private async ValueTask PublishLevel1Async(string symbol, string contractId, long transactionId, CancellationToken cancellationToken)
	{
		var ticker = await RestClient.GetTickerAsync(contractId, cancellationToken);
		var row = (ticker["data"] as JArray)?.FirstOrDefault() as JObject ?? ticker["data"] as JObject;
		if (row is null)
			return;

		var last = row["lastPrice"]?.Value<string>().To<decimal?>() ?? row["close"]?.Value<string>().To<decimal?>();
		var open = row["open"]?.Value<string>().To<decimal?>();

		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, last)
		.TryAdd(Level1Fields.OpenPrice, open)
		.TryAdd(Level1Fields.HighPrice, row["high"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.LowPrice, row["low"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.Volume, row["size"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.BestBidPrice, row["bidPrice"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.BestAskPrice, row["askPrice"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.Change, last is decimal lp && open is decimal op ? lp - op : null),
		cancellationToken);
	}

	private async ValueTask PublishDepthAsync(string symbol, string contractId, int? depth, long transactionId, CancellationToken cancellationToken)
	{
		var level = NormalizeDepth(depth);
		var snapshot = await RestClient.GetDepthAsync(contractId, level, cancellationToken);
		var book = (snapshot["data"] as JArray)?.FirstOrDefault() as JObject ?? snapshot["data"] as JObject;
		if (book is null)
			return;

		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = book["endVersion"]?.Value<long>() ?? 0,
			Bids = ToQuotes(book["bids"], depth),
			Asks = ToQuotes(book["asks"], depth),
		}, cancellationToken);
	}

	private async ValueTask PollTicksAsync(TickState state, CancellationToken cancellationToken)
	{
		var from = state.LastTime == default ? DateTime.UtcNow - TimeSpan.FromMinutes(30) : state.LastTime.AddMinutes(-1);
		var to = DateTime.UtcNow;
		var response = await RestClient.GetKlineAsync(state.ContractId, TimeSpan.FromMinutes(1).ToNativeKlineType(), "LAST_PRICE", 30, from, to, cancellationToken);
		var lastTime = state.LastTime;
		var lastTradeId = state.LastTradeId;

		foreach (var row in GetKlineRows(response).OrderBy(static r => r["klineTime"]?.Value<long>() ?? 0))
		{
			var time = ParseUnixMs(row["klineTime"]) ?? CurrentTime;
			if (time <= state.LastTime)
				continue;

			var tradeId = row["klineId"]?.Value<long?>() ?? (long)time.ToUnix(false);
			if (tradeId <= state.LastTradeId)
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = state.Symbol.ToStockSharp(BoardCode),
				ServerTime = time,
				TradeId = tradeId,
				TradePrice = row["close"]?.Value<string>().To<decimal?>(),
				TradeVolume = row["size"]?.Value<string>().To<decimal?>(),
				OriginalTransactionId = state.TransactionId,
			}, cancellationToken);

			lastTime = time;
			lastTradeId = tradeId;
		}

		using (_sync.EnterScope())
		{
			if (_ticksRealtime.TryGetValue(state.TransactionId, out var stored))
			{
				stored.LastTime = lastTime;
				stored.LastTradeId = lastTradeId;
			}
		}
	}

	private async ValueTask PollCandlesAsync(CandleState state, CancellationToken cancellationToken)
	{
		var from = state.LastOpenTime == default ? DateTime.UtcNow - TimeSpan.FromHours(6) : state.LastOpenTime - state.TimeFrame;
		var to = DateTime.UtcNow;
		var response = await RestClient.GetKlineAsync(state.ContractId, state.TimeFrame.ToNativeKlineType(), "LAST_PRICE", 60, from, to, cancellationToken);
		var lastOpen = state.LastOpenTime;

		foreach (var row in GetKlineRows(response).OrderBy(static r => r["klineTime"]?.Value<long>() ?? 0))
		{
			var openTime = ParseUnixMs(row["klineTime"]) ?? CurrentTime;
			if (openTime <= state.LastOpenTime)
				continue;

			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = state.Symbol.ToStockSharp(BoardCode),
				TypedArg = state.TimeFrame,
				OpenTime = openTime,
				OpenPrice = row["open"]?.Value<string>().To<decimal?>() ?? 0m,
				HighPrice = row["high"]?.Value<string>().To<decimal?>() ?? 0m,
				LowPrice = row["low"]?.Value<string>().To<decimal?>() ?? 0m,
				ClosePrice = row["close"]?.Value<string>().To<decimal?>() ?? 0m,
				TotalVolume = row["size"]?.Value<string>().To<decimal?>() ?? 0m,
				State = CandleStates.Finished,
				OriginalTransactionId = state.TransactionId,
			}, cancellationToken);

			lastOpen = openTime;
		}

		using (_sync.EnterScope())
		{
			if (_candlesRealtime.TryGetValue(state.TransactionId, out var stored))
				stored.LastOpenTime = lastOpen;
		}
	}

	private async ValueTask EnsureMetadataAsync(CancellationToken cancellationToken)
	{
		if (_contractsById.Count > 0)
			return;

		var root = await RestClient.GetMetaDataAsync(cancellationToken);
		var data = root["data"] as JObject ?? root;
		var contracts = data["contractList"]?.ToObject<Contract[]>() ?? [];
		var coins = data["coinList"]?.ToObject<Coin[]>() ?? [];
		var collateral = data["global"]?["starkExCollateralCoin"]?["coinName"]?.Value<string>();

		if (!collateral.IsEmpty())
			_collateralCoin = collateral.ToUpperInvariant();

		foreach (var coin in coins)
		{
			if (coin?.Id.IsEmpty() != false)
				continue;

			_coinsById[coin.Id] = coin;
		}

		foreach (var contract in contracts)
		{
			if (contract?.Id.IsEmpty() != false)
				continue;

			var symbol = (contract.Name ?? contract.Id).ToUpperInvariant();
			contract.Name = symbol;
			_contractsById[contract.Id] = contract;
			_contractsBySymbol[symbol] = contract;
		}
	}

	private void ClearMetadataCache()
	{
		_contractsById.Clear();
		_contractsBySymbol.Clear();
		_coinsById.Clear();
		_collateralCoin = "USD";
	}

	private (string Symbol, string ContractId) ResolveSymbolAndContract(SecurityId securityId)
	{
		var secCode = securityId.SecurityCode.ToUpperInvariant();
		if (secCode.IsEmpty())
			throw new InvalidOperationException("Security code is empty.");

		if (_contractsBySymbol.TryGetValue(secCode, out var bySymbol))
			return (bySymbol.Name, bySymbol.Id);

		if (long.TryParse(secCode, out _) && _contractsById.TryGetValue(secCode, out var byId))
			return (byId.Name, byId.Id);

		var normalized = secCode.Replace("/", string.Empty, StringComparison.Ordinal);
		if (_contractsBySymbol.TryGetValue(normalized, out var byNormalized))
			return (byNormalized.Name, byNormalized.Id);

		throw new InvalidOperationException($"edgeX market '{secCode}' is not found in metadata cache.");
	}

	private string ResolveContractId(SecurityId securityId)
		=> ResolveSymbolAndContract(securityId).ContractId;

	private string ResolveOrderSymbol(JObject entry)
	{
		var contractName = entry["contractName"]?.Value<string>();
		if (!contractName.IsEmpty())
			return contractName.ToUpperInvariant();

		var contractId = entry["contractId"]?.Value<string>();
		if (!contractId.IsEmpty() && _contractsById.TryGetValue(contractId, out var contract))
			return contract.Name;

		return null;
	}

	private JObject BuildRegisterPayload(OrderRegisterMessage regMsg, string contractId)
	{
		var condition = regMsg.Condition as EdgeXOrderCondition;
		EnsureL2Signature(condition);

		var type = regMsg.OrderType.ToNative(condition);
		var payload = new JObject
		{
			["accountId"] = GetAccountId(),
			["contractId"] = contractId,
			["price"] = (regMsg.OrderType ?? OrderTypes.Limit) == OrderTypes.Market ? "0" : regMsg.Price.To<string>(),
			["size"] = regMsg.Volume.Abs().To<string>(),
			["type"] = type,
			["timeInForce"] = regMsg.TimeInForce.ToNative(),
			["side"] = regMsg.Side.ToNative(),
			["clientOrderId"] = regMsg.UserOrderId.IsEmpty() ? $"x-ss-{regMsg.TransactionId}" : regMsg.UserOrderId,
			["reduceOnly"] = regMsg.PositionEffect == OrderPositionEffects.CloseOnly || condition?.ReduceOnly == true,
			["l2Signature"] = condition?.L2Signature,
			["l2Nonce"] = condition?.L2Nonce,
			["l2ExpireTime"] = condition?.L2ExpireTime,
			["l2Value"] = condition?.L2Value,
			["l2Size"] = condition?.L2Size,
			["l2LimitFee"] = condition?.L2LimitFee,
		};

		if (condition?.ActivationPrice is decimal ap && ap > 0)
			payload["triggerPrice"] = ap.To<string>();

		if (!condition?.PositionSide.IsEmpty() == true)
			payload["positionSide"] = condition.PositionSide;

		return payload;
	}

	private static IEnumerable<JObject> GetKlineRows(JObject response)
		=> ((response?["data"] as JObject)?["dataList"] as JArray)?.OfType<JObject>() ?? [];

	private static int NormalizeDepth(int? depth)
		=> depth is int d && d > 15 ? 200 : 15;

	private static DateTime? ParseUnixMs(JToken token)
	{
		var value = token?.Value<long?>() ?? token?.Value<string>().To<long?>();
		return value is long unix && unix > 0 ? unix.FromUnix(false) : null;
	}

	private static decimal? ParseDecimal(JToken token)
		=> token?.ToString().To<decimal?>();

	private static long? ParseLong(JToken token)
		=> token?.ToString().To<long?>();

	private static string NormalizeWsEndpoint(string endpoint)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		endpoint = endpoint.Trim();

		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";

		return endpoint;
	}

	private QuoteChange[] ToQuotes(JToken entries, int? depth)
	{
		if (entries is not JArray array || array.Count == 0)
			return [];

		var maxDepth = 1.Max(depth ?? int.MaxValue);
		var list = new List<QuoteChange>(maxDepth.Min(array.Count));

		foreach (var item in array.Take(maxDepth))
		{
			decimal? price = null;
			decimal? volume = null;

			switch (item)
			{
				case JObject obj:
					price = obj["price"]?.Value<string>().To<decimal?>();
					volume = obj["size"]?.Value<string>().To<decimal?>();
					break;

				case JArray tuple when tuple.Count >= 2:
					price = tuple[0]?.Value<string>().To<decimal?>();
					volume = tuple[1]?.Value<string>().To<decimal?>();
					break;
			}

			if (price is decimal p && volume is decimal v)
				list.Add(new QuoteChange(p, v));
		}

		return [.. list];
	}

	private IEnumerable<JObject> GetOrderItems(JObject response)
	{
		var data = response?["data"];
		return ExtractObjects(data, "orderList", "dataList", "list", "records");
	}

	private IEnumerable<JObject> GetFillItems(JObject response)
	{
		var data = response?["data"];
		return ExtractObjects(data, "fillList", "transactionList", "dataList", "list", "records");
	}

	private static IEnumerable<JObject> ExtractObjects(JToken token, params string[] keys)
	{
		if (token is null)
			return [];

		if (token is JArray arr)
			return arr.OfType<JObject>();

		foreach (var key in keys)
		{
			if (token[key] is JArray keyed)
				return keyed.OfType<JObject>();
		}

		return [];
	}

	private string ResolveCoinName(string name, string id, string fallback)
	{
		if (!name.IsEmpty())
			return name.ToUpperInvariant();

		if (!id.IsEmpty() && _coinsById.TryGetValue(id, out var coin) && !coin?.Name.IsEmpty() == true)
			return coin.Name.ToUpperInvariant();

		return fallback?.ToUpperInvariant();
	}

	private async ValueTask RegisterLevel1Async(long transactionId, string symbol, string contractId, CancellationToken cancellationToken)
	{
		var shouldSubscribe = false;

		using (_sync.EnterScope())
		{
			_level1Realtime[transactionId] = (symbol, contractId);
			shouldSubscribe = AddRef(_level1Refs, contractId);
		}

		if (shouldSubscribe)
			await WsClient.SubscribeAsync($"ticker.{contractId}", cancellationToken);
	}

	private async ValueTask UnregisterLevel1Async(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		string contractId = null;
		var shouldUnsubscribe = false;

		using (_sync.EnterScope())
		{
			if (_level1Realtime.TryGetValue(originalTransactionId, out var state))
			{
				_level1Realtime.Remove(originalTransactionId);
				contractId = state.ContractId;
				shouldUnsubscribe = ReleaseRef(_level1Refs, contractId);
			}
		}

		if (shouldUnsubscribe && _wsClient is not null)
			await WsClient.UnsubscribeAsync($"ticker.{contractId}", cancellationToken);
	}

	private async ValueTask RegisterDepthAsync(long transactionId, string symbol, string contractId, int? depth, CancellationToken cancellationToken)
	{
		var shouldSubscribe = false;

		using (_sync.EnterScope())
		{
			_depthRealtime[transactionId] = (symbol, contractId, depth);
			shouldSubscribe = AddRef(_depthRefs, contractId);
		}

		if (shouldSubscribe)
			await WsClient.SubscribeAsync($"depth.{contractId}.200", cancellationToken);
	}

	private async ValueTask UnregisterDepthAsync(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		string contractId = null;
		var shouldUnsubscribe = false;

		using (_sync.EnterScope())
		{
			if (_depthRealtime.TryGetValue(originalTransactionId, out var state))
			{
				_depthRealtime.Remove(originalTransactionId);
				contractId = state.ContractId;
				shouldUnsubscribe = ReleaseRef(_depthRefs, contractId);
			}
		}

		if (shouldUnsubscribe && _wsClient is not null)
			await WsClient.UnsubscribeAsync($"depth.{contractId}.200", cancellationToken);
	}

	private async ValueTask RegisterTicksAsync(long transactionId, string symbol, string contractId, DateTime lastTime, long lastTradeId, CancellationToken cancellationToken)
	{
		var shouldSubscribe = false;

		using (_sync.EnterScope())
		{
			_ticksRealtime[transactionId] = new()
			{
				Symbol = symbol,
				ContractId = contractId,
				TransactionId = transactionId,
				LastTime = lastTime,
				LastTradeId = lastTradeId,
			};

			shouldSubscribe = AddRef(_ticksRefs, contractId);
		}

		if (shouldSubscribe)
			await WsClient.SubscribeAsync($"trade.{contractId}", cancellationToken);
	}

	private async ValueTask UnregisterTicksAsync(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		string contractId = null;
		var shouldUnsubscribe = false;

		using (_sync.EnterScope())
		{
			if (_ticksRealtime.TryGetValue(originalTransactionId, out var state))
			{
				_ticksRealtime.Remove(originalTransactionId);
				contractId = state.ContractId;
				shouldUnsubscribe = ReleaseRef(_ticksRefs, contractId);
			}
		}

		if (shouldUnsubscribe && _wsClient is not null)
			await WsClient.UnsubscribeAsync($"trade.{contractId}", cancellationToken);
	}

	private async ValueTask RegisterCandlesAsync(long transactionId, string symbol, string contractId, TimeSpan timeFrame, DateTime lastOpenTime, CancellationToken cancellationToken)
	{
		var shouldSubscribe = false;
		var key = (contractId, timeFrame);

		using (_sync.EnterScope())
		{
			_candlesRealtime[transactionId] = new()
			{
				Symbol = symbol,
				ContractId = contractId,
				TransactionId = transactionId,
				TimeFrame = timeFrame,
				LastOpenTime = lastOpenTime,
			};

			shouldSubscribe = AddRef(_candleRefs, key);
		}

		if (shouldSubscribe)
			await WsClient.SubscribeAsync($"kline.LAST_PRICE.{contractId}.{timeFrame.ToNativeKlineType()}", cancellationToken);
	}

	private async ValueTask UnregisterCandlesAsync(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		(string ContractId, TimeSpan TimeFrame)? key = null;
		var shouldUnsubscribe = false;

		using (_sync.EnterScope())
		{
			if (_candlesRealtime.TryGetValue(originalTransactionId, out var state))
			{
				_candlesRealtime.Remove(originalTransactionId);
				key = (state.ContractId, state.TimeFrame);
				shouldUnsubscribe = ReleaseRef(_candleRefs, key.Value);
			}
		}

		if (shouldUnsubscribe && key is { } candleKey && _wsClient is not null)
			await WsClient.UnsubscribeAsync($"kline.LAST_PRICE.{candleKey.ContractId}.{candleKey.TimeFrame.ToNativeKlineType()}", cancellationToken);
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

	private bool CanUsePrivateWs()
	{
		if (IsSpotSection)
			return false;

		if (_privateWsEndpoint.IsEmpty())
			return false;

		if (Key.IsEmpty() || Secret.IsEmpty())
			return false;

		if (_clearingAccount.IsEmpty())
		{
			this.AddWarningLog("edgeX private websocket is disabled because clearing account is empty.");
			return false;
		}

		return true;
	}

	private async ValueTask ConnectPrivateWsAsync(CancellationToken cancellationToken)
	{
		var endpoint = NormalizeWsEndpoint(_privateWsEndpoint);
		var uri = endpoint.To<Uri>();
		var pathAndQuery = uri.PathAndQuery.IsEmpty() ? uri.AbsolutePath : uri.PathAndQuery;
		var headers = RestClient.CreateWebSocketAuthHeaders(pathAndQuery);

		_privateWsClient = new(endpoint, _workingTime, headers) { Parent = this };
		_privateWsClient.PrivatePayloadReceived += OnPrivateWsPayloadAsync;
		_privateWsClient.Error += OnWsErrorAsync;

		await _privateWsClient.ConnectAsync(cancellationToken);

		foreach (var channel in _privateChannels)
			await _privateWsClient.SubscribeAsync(channel, cancellationToken);
	}

	private void DisconnectPrivateWs()
	{
		if (_privateWsClient is null)
			return;

		_privateWsClient.PrivatePayloadReceived -= OnPrivateWsPayloadAsync;
		_privateWsClient.Error -= OnWsErrorAsync;
		_privateWsClient.Disconnect();
		_privateWsClient.Dispose();
		_privateWsClient = null;
	}

	private ValueTask OnPrivateWsPayloadAsync(string channel, JObject payload, CancellationToken cancellationToken)
	{
		channel = channel?.ToLowerInvariant();

		return channel switch
		{
			"order-event" => SendPrivateOrderEventAsync(payload, cancellationToken),
			"trade-event" => SendPrivateTradeEventAsync(payload, cancellationToken),
			"asset-event" => SendPrivateAssetEventAsync(payload, cancellationToken),
			"position-event" => SendPrivatePositionEventAsync(payload, cancellationToken),
			_ => default,
		};
	}

	private async ValueTask SendPrivateOrderEventAsync(JObject payload, CancellationToken cancellationToken)
	{
		var symbol = ResolveOrderSymbol(payload);

		if (symbol.IsEmpty())
			return;

		var sideValue = payload["side"]?.Value<string>();

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

		var orderStringId = payload["clientOrderId"]?.Value<string>() ?? payload["clientId"]?.Value<string>();
		var transId = TryExtractTransactionId(orderStringId) ?? 0;
		var orderVolume = ParseDecimal(payload["size"] ?? payload["orderSize"] ?? payload["origQty"]);
		var filled = ParseDecimal(payload["filledSize"] ?? payload["executedSize"] ?? payload["cumFilledSize"] ?? payload["cumQty"]) ?? 0m;
		var tradeVolume = ParseDecimal(payload["lastFilledSize"] ?? payload["lastTradeSize"] ?? payload["filledQty"]);
		var tradePrice = ParseDecimal(payload["lastFilledPrice"] ?? payload["tradePrice"] ?? payload["avgPrice"] ?? payload["price"]);
		var serverTime = ParseUnixMs(payload["updatedTime"] ?? payload["timestamp"] ?? payload["createdTime"] ?? payload["time"]) ?? CurrentTime;
		var reduceOnly = payload["reduceOnly"]?.Value<bool?>();

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = serverTime,
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = orderVolume,
			Balance = orderVolume is decimal ov ? (ov - filled).Max(0m) : null,
			OrderPrice = ParseDecimal(payload["price"]) ?? 0m,
			OrderType = payload["type"]?.Value<string>().ToOrderType(),
			OrderState = payload["status"]?.Value<string>().ToOrderState(),
			TimeInForce = payload["timeInForce"]?.Value<string>().ToTimeInForce(),
			OrderId = ParseLong(payload["orderId"]),
			OrderStringId = orderStringId,
			TransactionId = transId,
			OriginalTransactionId = transId,
			PositionEffect = reduceOnly is bool ro ? (ro ? OrderPositionEffects.CloseOnly : OrderPositionEffects.Default) : null,
			TradeId = tradeVolume > 0 ? ParseLong(payload["fillId"] ?? payload["tradeId"]) : null,
			TradePrice = tradeVolume > 0 ? tradePrice : null,
			TradeVolume = tradeVolume > 0 ? tradeVolume : null,
			Commission = tradeVolume > 0 ? ParseDecimal(payload["fee"] ?? payload["commission"]) : null,
			CommissionCurrency = tradeVolume > 0 ? payload["feeCoinName"]?.Value<string>() ?? payload["commissionAsset"]?.Value<string>() : null,
		}, cancellationToken);
	}

	private async ValueTask SendPrivateTradeEventAsync(JObject payload, CancellationToken cancellationToken)
	{
		var symbol = ResolveOrderSymbol(payload);

		if (symbol.IsEmpty())
			return;

		Sides? side = null;

		if (payload["side"]?.Value<string>() is { } sideValue && !sideValue.IsEmpty())
		{
			try
			{
				side = sideValue.ToSide();
			}
			catch
			{
				side = null;
			}
		}

		if (side is null)
			return;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = symbol.ToStockSharp(BoardCode),
			ServerTime = ParseUnixMs(payload["createdTime"] ?? payload["updatedTime"] ?? payload["time"] ?? payload["timestamp"]) ?? CurrentTime,
			PortfolioName = _portfolioName,
			Side = side.Value,
			OrderId = ParseLong(payload["orderId"]),
			TradeId = ParseLong(payload["fillId"] ?? payload["tradeId"]),
			TradePrice = ParseDecimal(payload["price"] ?? payload["fillPrice"] ?? payload["tradePrice"]),
			TradeVolume = ParseDecimal(payload["size"] ?? payload["fillSize"] ?? payload["qty"]),
			Commission = ParseDecimal(payload["fee"] ?? payload["commission"]),
			CommissionCurrency = payload["feeCoinName"]?.Value<string>() ?? payload["commissionAsset"]?.Value<string>(),
		}, cancellationToken);
	}

	private async ValueTask SendPrivateAssetEventAsync(JObject payload, CancellationToken cancellationToken)
	{
		var rows = ExtractObjects(payload, "collateralAssetList", "assetList", "collateralList", "balances").ToArray();

		if (rows.Length == 0)
			rows = [payload];

		var serverTime = ParseUnixMs(payload["updatedTime"] ?? payload["time"] ?? payload["timestamp"]) ?? CurrentTime;

		foreach (var item in rows)
		{
			var coin = ResolveCoinName(item["coinName"]?.Value<string>(), item["coinId"]?.Value<string>(), item["asset"]?.Value<string>());

			if (coin.IsEmpty())
				continue;

			var total = ParseDecimal(item["totalBalance"] ?? item["balance"] ?? item["walletBalance"]);
			var available = ParseDecimal(item["availableBalance"] ?? item["free"]);

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = coin.ToStockSharp(BoardCode),
				ServerTime = serverTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, total, true)
			.TryAdd(PositionChangeTypes.BlockedValue, total is decimal t && available is decimal a ? (t - a).Max(0m) : null, true),
			cancellationToken);
		}
	}

	private async ValueTask SendPrivatePositionEventAsync(JObject payload, CancellationToken cancellationToken)
	{
		var rows = ExtractObjects(payload, "positionAssetList", "positionList", "positions").ToArray();

		if (rows.Length == 0)
			rows = [payload];

		var serverTime = ParseUnixMs(payload["updatedTime"] ?? payload["time"] ?? payload["timestamp"]) ?? CurrentTime;

		foreach (var item in rows)
		{
			var symbol = ResolveOrderSymbol(item);

			if (symbol.IsEmpty())
				continue;

			Sides? side = null;

			if (item["side"]?.Value<string>() is { } sideValue && !sideValue.IsEmpty())
			{
				try
				{
					side = sideValue.ToSide();
				}
				catch
				{
					side = null;
				}
			}

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				Side = side,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, ParseDecimal(item["position"] ?? item["size"] ?? item["positionSize"]), true)
			.TryAdd(PositionChangeTypes.AveragePrice, ParseDecimal(item["entryPrice"] ?? item["openPrice"]), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, ParseDecimal(item["unrealizedPnl"] ?? item["unrealizedProfit"]), true)
			.TryAdd(PositionChangeTypes.Leverage, ParseDecimal(item["leverage"]), true),
			cancellationToken);
		}
	}

	private async ValueTask OnWsTickerAsync(string channel, JObject ticker, CancellationToken cancellationToken)
	{
		var contractId = ExtractContractId(channel);

		if (contractId.IsEmpty())
			return;

		(long TransactionId, string Symbol)[] subscriptions;

		using (_sync.EnterScope())
		{
			subscriptions = [.. _level1Realtime
				.Where(p => p.Value.ContractId.EqualsIgnoreCase(contractId))
				.Select(p => (p.Key, p.Value.Symbol))];
		}

		if (subscriptions.Length == 0)
			return;

		var last = ticker["lastPrice"]?.Value<string>().To<decimal?>() ?? ticker["close"]?.Value<string>().To<decimal?>();
		var open = ticker["open"]?.Value<string>().To<decimal?>();
		var serverTime = ParseUnixMs(ticker["time"] ?? ticker["ts"] ?? ticker["updatedTime"] ?? ticker["timestamp"]) ?? CurrentTime;

		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = subscription.Symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				OriginalTransactionId = subscription.TransactionId,
			}
			.TryAdd(Level1Fields.LastTradePrice, last)
			.TryAdd(Level1Fields.OpenPrice, open)
			.TryAdd(Level1Fields.HighPrice, ticker["high"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.LowPrice, ticker["low"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.Volume, ticker["size"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.BestBidPrice, ticker["bidPrice"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.BestAskPrice, ticker["askPrice"]?.Value<string>().To<decimal?>())
			.TryAdd(Level1Fields.Change, last is decimal l && open is decimal o ? l - o : null),
			cancellationToken);
		}
	}

	private async ValueTask OnWsDepthAsync(string channel, JObject depth, CancellationToken cancellationToken)
	{
		var contractId = ExtractContractId(channel);

		if (contractId.IsEmpty())
			return;

		(long TransactionId, string Symbol, int? Depth)[] subscriptions;

		using (_sync.EnterScope())
		{
			subscriptions = [.. _depthRealtime
				.Where(p => p.Value.ContractId.EqualsIgnoreCase(contractId))
				.Select(p => (p.Key, p.Value.Symbol, p.Value.Depth))];
		}

		if (subscriptions.Length == 0)
			return;

		var serverTime = ParseUnixMs(depth["time"] ?? depth["ts"] ?? depth["updatedTime"] ?? depth["timestamp"]) ?? CurrentTime;
		var seqNum = depth["endVersion"]?.Value<long?>() ?? depth["seqNum"]?.Value<long?>() ?? depth["version"]?.Value<long?>() ?? 0;

		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = subscription.Symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				OriginalTransactionId = subscription.TransactionId,
				State = QuoteChangeStates.SnapshotComplete,
				SeqNum = seqNum,
				Bids = ToQuotes(depth["bids"], subscription.Depth),
				Asks = ToQuotes(depth["asks"], subscription.Depth),
			}, cancellationToken);
		}
	}

	private async ValueTask OnWsTradeAsync(string channel, JObject trade, CancellationToken cancellationToken)
	{
		var contractId = ExtractContractId(channel);

		if (contractId.IsEmpty())
			return;

		(long TransactionId, string Symbol)[] subscriptions;

		using (_sync.EnterScope())
		{
			subscriptions = [.. _ticksRealtime
				.Where(p => p.Value.ContractId.EqualsIgnoreCase(contractId))
				.Select(p => (p.Key, p.Value.Symbol))];
		}

		if (subscriptions.Length == 0)
			return;

		var serverTime = ParseUnixMs(trade["time"] ?? trade["createdTime"] ?? trade["tradeTime"] ?? trade["timestamp"]) ?? CurrentTime;
		var tradeId = trade["ticketId"]?.Value<long?>() ?? trade["tradeId"]?.Value<long?>();
		var originSide = trade["isBuyerMaker"]?.Value<bool?>() == true ? Sides.Sell : Sides.Buy;

		if (trade["side"]?.Value<string>() is { } sideValue && !sideValue.IsEmpty())
			originSide = sideValue.ToSide();

		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = subscription.Symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				TradeId = tradeId,
				TradePrice = trade["price"]?.Value<string>().To<decimal?>(),
				TradeVolume = trade["size"]?.Value<string>().To<decimal?>(),
				OriginSide = originSide,
				OriginalTransactionId = subscription.TransactionId,
			}, cancellationToken);
		}
	}

	private async ValueTask OnWsCandleAsync(string channel, JObject row, CancellationToken cancellationToken)
	{
		var (contractId, timeFrame) = ExtractCandleKey(channel);

		if (contractId.IsEmpty() || timeFrame == default)
			return;

		(long TransactionId, string Symbol)[] subscriptions;

		using (_sync.EnterScope())
		{
			subscriptions = [.. _candlesRealtime
				.Where(p => p.Value.ContractId.EqualsIgnoreCase(contractId) && p.Value.TimeFrame == timeFrame)
				.Select(p => (p.Key, p.Value.Symbol))];
		}

		if (subscriptions.Length == 0)
			return;

		var openTime = ParseUnixMs(row["klineTime"] ?? row["openTime"]) ?? CurrentTime;
		var closeTime = ParseUnixMs(row["endTime"]) ?? openTime + timeFrame;

		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = subscription.Symbol.ToStockSharp(BoardCode),
				TypedArg = timeFrame,
				OpenTime = openTime,
				CloseTime = closeTime,
				OpenPrice = row["open"]?.Value<string>().To<decimal?>() ?? 0m,
				HighPrice = row["high"]?.Value<string>().To<decimal?>() ?? 0m,
				LowPrice = row["low"]?.Value<string>().To<decimal?>() ?? 0m,
				ClosePrice = row["close"]?.Value<string>().To<decimal?>() ?? 0m,
				TotalVolume = row["size"]?.Value<string>().To<decimal?>() ?? 0m,
				State = CandleStates.Finished,
				OriginalTransactionId = subscription.TransactionId,
			}, cancellationToken);
		}
	}

	private static string ExtractContractId(string channel)
	{
		if (channel.IsEmpty())
			return null;

		var parts = channel.SplitByDot();
		return parts.Length > 1 ? parts[1] : null;
	}

	private static (string ContractId, TimeSpan TimeFrame) ExtractCandleKey(string channel)
	{
		if (channel.IsEmpty())
			return default;

		var parts = channel.SplitByDot();

		if (parts.Length < 4)
			return default;

		var contractId = parts[2];
		var interval = parts[3];

		if (contractId.IsEmpty() || interval.IsEmpty())
			return default;

		try
		{
			return (contractId, interval.ToTimeFrame());
		}
		catch
		{
			return default;
		}
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

	private string GetAccountId()
	{
		if (_clearingAccount.IsEmpty())
			throw new InvalidOperationException("Clearing account is not specified.");

		return _clearingAccount;
	}

	private static long? TryExtractTransactionId(string clientOrderId)
	{
		if (clientOrderId.IsEmpty())
			return null;

		var idx = clientOrderId.LastIndexOf('-');
		var value = idx >= 0 ? clientOrderId[(idx + 1)..] : clientOrderId;
		return long.TryParse(value, out var transId) ? transId : null;
	}

	private void EnsureTradingReady()
	{
		if (!SupportsTrading)
			throw new NotSupportedException($"edgeX {SectionName} trading is not available for this section.");

		EnsurePrivateDataReady();
	}

	private void EnsurePrivateDataReady()
	{
		if (!SupportsPrivateData)
			throw new NotSupportedException($"edgeX {SectionName} private operations are not available for this section.");

		if (Key.IsEmpty() || Secret.IsEmpty())
			throw new InvalidOperationException("Key/Secret are required for private edgeX operations.");

		_ = GetAccountId();
	}

	private static void EnsureL2Signature(EdgeXOrderCondition condition)
	{
		if (condition is null)
			throw new InvalidOperationException("EdgeXOrderCondition is required for edgeX order placement.");

		if (condition.L2Signature.IsEmpty()
			|| condition.L2Nonce.IsEmpty()
			|| condition.L2ExpireTime.IsEmpty()
			|| condition.L2Value.IsEmpty()
			|| condition.L2Size.IsEmpty()
			|| condition.L2LimitFee.IsEmpty())
		{
			throw new InvalidOperationException("edgeX requires pre-signed L2 fields (L2Signature, L2Nonce, L2ExpireTime, L2Value, L2Size, L2LimitFee) in EdgeXOrderCondition.");
		}
	}
}

