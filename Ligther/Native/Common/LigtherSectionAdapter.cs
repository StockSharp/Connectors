namespace StockSharp.Ligther.Native.Common;

abstract class LigtherSectionAdapter : BaseNativeAdapter
{
	private const int _maxTradesLimit = 500;
	private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

	private LigtherRestClient _restClient;
	private LigtherWsClient _wsClient;
	private string _portfolioName;
	private readonly Lock _sync = new();
	private readonly Dictionary<long, int> _level1Realtime = [];
	private readonly Dictionary<long, (int MarketId, int? Depth)> _depthRealtime = [];
	private readonly Dictionary<long, TickState> _ticksRealtime = [];
	private readonly Dictionary<long, CandleState> _candlesRealtime = [];
	private readonly Dictionary<string, int> _orderBookRefs = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, int> _tradeRefs = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, int> _statsRefs = new(StringComparer.InvariantCultureIgnoreCase);
	private DateTime _lastPollTime;

	private readonly Dictionary<int, JObject> _marketsById = [];
	private readonly Dictionary<string, JObject> _marketsBySymbol = new(StringComparer.InvariantCultureIgnoreCase);

	private sealed class TickState
	{
		public int MarketId { get; init; }
		public long TransactionId { get; init; }
		public long LastTradeId { get; set; }
		public DateTime LastTime { get; set; }
	}

	private sealed class CandleState
	{
		public int MarketId { get; init; }
		public long TransactionId { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
		public long LastTradeId { get; set; }
		public DateTime CurrentOpenTime { get; set; }
		public decimal? OpenPrice { get; set; }
		public decimal? HighPrice { get; set; }
		public decimal? LowPrice { get; set; }
		public decimal? ClosePrice { get; set; }
		public decimal TotalVolume { get; set; }
	}

	protected LigtherSectionAdapter(LigtherMessageAdapter owner, string boardCode)
		: base(owner, boardCode)
	{
	}

	protected abstract bool IsSpotSection { get; }
	protected abstract SecurityTypes SectionSecurityType { get; }
	protected virtual bool SupportsTrading => true;
	protected virtual string SectionFilter => IsSpotSection ? "spot" : "perp";

	protected LigtherRestClient RestClient => _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	protected LigtherWsClient WsClient => _wsClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	protected string PortfolioName => _portfolioName;

	public override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		await base.ConnectAsync(connectMsg, cancellationToken);

		var restEndpoint = IsSpotSection ? Owner.SpotRestEndpoint : Owner.DerivativesRestEndpoint;
		var wsEndpoint = IsSpotSection ? Owner.SpotWsEndpoint : Owner.DerivativesWsEndpoint;

		if (restEndpoint.IsEmpty())
			throw new InvalidOperationException("REST endpoint is not configured.");

		if (wsEndpoint.IsEmpty())
			throw new InvalidOperationException("WS endpoint is not configured.");

		_restClient = new(restEndpoint, Owner.Key, Owner.Secret) { Parent = Owner };
		_wsClient = new(wsEndpoint, Owner.ReConnectionSettings.WorkingTime, Owner.UseWsReadOnlyMode) { Parent = this };
		_wsClient.OrderBookReceived += OnWsOrderBookAsync;
		_wsClient.TradeReceived += OnWsTradeAsync;
		_wsClient.MarketStatsReceived += OnWsMarketStatsAsync;
		_wsClient.SpotMarketStatsReceived += OnWsSpotMarketStatsAsync;
		_wsClient.Error += OnWsErrorAsync;

		_portfolioName = $"{nameof(Ligther)}_{SectionName}_{Owner.AccountIndex}";

		await EnsureMarketsAsync(cancellationToken);
		ClearRealtimeSubscriptions();
		await _wsClient.ConnectAsync(cancellationToken);
	}

	public override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		try
		{
			await base.DisconnectAsync(disconnectMsg, cancellationToken);
		}
		finally
		{
			ClearRealtimeSubscriptions();

			if (_wsClient is not null)
			{
				_wsClient.OrderBookReceived -= OnWsOrderBookAsync;
				_wsClient.TradeReceived -= OnWsTradeAsync;
				_wsClient.MarketStatsReceived -= OnWsMarketStatsAsync;
				_wsClient.SpotMarketStatsReceived -= OnWsSpotMarketStatsAsync;
				_wsClient.Error -= OnWsErrorAsync;
				_wsClient.Disconnect();
				_wsClient.Dispose();
				_wsClient = null;
			}

			_restClient?.Dispose();
			_restClient = null;
		}
	}

	public override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		ClearRealtimeSubscriptions();

		if (_wsClient is not null)
		{
			_wsClient.OrderBookReceived -= OnWsOrderBookAsync;
			_wsClient.TradeReceived -= OnWsTradeAsync;
			_wsClient.MarketStatsReceived -= OnWsMarketStatsAsync;
			_wsClient.SpotMarketStatsReceived -= OnWsSpotMarketStatsAsync;
			_wsClient.Error -= OnWsErrorAsync;
			_wsClient.Disconnect();
			_wsClient.Dispose();
			_wsClient = null;
		}

		_restClient?.Dispose();
		_restClient = null;
		_marketsById.Clear();
		_marketsBySymbol.Clear();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	public override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_wsClient is not null)
			return;

		var now = DateTime.UtcNow;

		if (now - _lastPollTime < _pollInterval)
			return;

		_lastPollTime = now;

		Dictionary<long, int> level1;
		Dictionary<long, (int MarketId, int? Depth)> depth;
		TickState[] ticks;
		CandleState[] candles;

		using (_sync.EnterScope())
		{
			level1 = new(_level1Realtime);
			depth = new(_depthRealtime);
			ticks = [.. _ticksRealtime.Values];
			candles = [.. _candlesRealtime.Values];
		}

		foreach (var pair in level1)
		{
			try
			{
				await PublishLevel1Async(pair.Value, pair.Key, cancellationToken);
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}
		}

		foreach (var pair in depth)
		{
			try
			{
				await PublishDepthAsync(pair.Value.MarketId, pair.Value.Depth, pair.Key, cancellationToken);
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}
		}

		foreach (var state in ticks)
		{
			try
			{
				await PollTicksAsync(state, cancellationToken);
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}
		}

		foreach (var state in candles)
		{
			try
			{
				await PollCandlesAsync(state, cancellationToken);
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}
		}
	}

	public override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await EnsureMarketsAsync(cancellationToken);

		var secTypes = lookupMsg.GetSecurityTypes();
		if (secTypes.Count > 0 && !secTypes.Contains(SectionSecurityType))
			return;

		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var market in _marketsById.Values)
		{
			var symbol = market["symbol"]?.Value<string>()?.ToUpperInvariant();
			if (symbol.IsEmpty())
				continue;

			var priceDecimals = market["supported_price_decimals"]?.Value<int?>() ?? market["price_decimals"]?.Value<int?>() ?? 0;
			var sizeDecimals = market["supported_size_decimals"]?.Value<int?>() ?? market["size_decimals"]?.Value<int?>() ?? 0;

			var secMsg = new SecurityMessage
			{
				SecurityId = symbol.ToStockSharp(BoardCode),
				Name = symbol,
				SecurityType = SectionSecurityType,
				PriceStep = Extensions.GetStepByDecimals(priceDecimals),
				VolumeStep = Extensions.GetStepByDecimals(sizeDecimals),
				MinVolume = market["min_base_amount"]?.Value<string>().To<decimal?>(),
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryFillUnderlyingId(GetUnderlying(symbol));

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

		var marketId = ResolveMarketId(mdMsg.SecurityId);
		await PublishLevel1Async(marketId, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await RegisterLevel1Async(mdMsg.TransactionId, marketId, cancellationToken);
	}

	public override async ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnregisterDepthAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var marketId = ResolveMarketId(mdMsg.SecurityId);
		await PublishDepthAsync(marketId, mdMsg.MaxDepth, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await RegisterDepthAsync(mdMsg.TransactionId, marketId, mdMsg.MaxDepth, cancellationToken);
	}

	public override async ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnregisterTicksAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var marketId = ResolveMarketId(mdMsg.SecurityId);
		var from = mdMsg.From ?? (DateTime.UtcNow - TimeSpan.FromHours(4));
		var to = mdMsg.To ?? DateTime.UtcNow;
		var left = mdMsg.Count ?? long.MaxValue;
		var lastTradeId = 0L;
		var lastTime = from;

		foreach (var trade in GetTradeRows(await RestClient.GetRecentTradesAsync(marketId, _maxTradesLimit, cancellationToken))
			.OrderBy(static t => t["timestamp"]?.Value<long>() ?? 0))
		{
			var serverTime = ParseUnixMs(trade["timestamp"]) ?? CurrentTime;
			if (serverTime < from)
				continue;
			if (serverTime > to)
				break;

			var tradeId = trade["trade_id"]?.Value<long?>() ?? (long)serverTime.ToUnix(false);
			if (tradeId <= lastTradeId)
				continue;

			await SendOutMessageAsync(CreateTickMessage(marketId, mdMsg.TransactionId, trade, tradeId, serverTime), cancellationToken);
			lastTradeId = tradeId;
			lastTime = serverTime;

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await RegisterTicksAsync(mdMsg.TransactionId, marketId, lastTime, lastTradeId, cancellationToken);
	}

	public override async ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await UnregisterCandlesAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var marketId = ResolveMarketId(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var from = mdMsg.From ?? (DateTime.UtcNow - TimeSpan.FromDays(1));
		var to = mdMsg.To ?? DateTime.UtcNow;
		var left = mdMsg.Count ?? long.MaxValue;
		var lastOpen = from;

		var rows = GetTradeRows(await RestClient.GetRecentTradesAsync(marketId, _maxTradesLimit, cancellationToken));
		foreach (var candle in BuildCandles(rows, marketId, timeFrame, from, to))
		{
			candle.OriginalTransactionId = mdMsg.TransactionId;
			await SendOutMessageAsync(candle, cancellationToken);
			lastOpen = candle.OpenTime;

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await RegisterCandlesAsync(mdMsg.TransactionId, marketId, timeFrame, lastOpen, cancellationToken);
	}

	public override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		var marketId = ResolveMarketId(regMsg.SecurityId);
		var condition = regMsg.Condition as LigtherOrderCondition;
		var payload = ParseRawPayload(condition?.RawTx, "LigtherOrderCondition.RawTx");
		var response = await RestClient.SendTxAsync(payload, condition?.AuthToken, cancellationToken);
		var txId = response["tx_hash"]?.Value<string>() ?? response["hash"]?.Value<string>() ?? response["id"]?.Value<string>();

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = GetSecurityId(marketId),
			ServerTime = CurrentTime,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = regMsg.Volume.Abs(),
			OrderPrice = regMsg.Price,
			OrderType = regMsg.OrderType,
			OrderState = OrderStates.Active,
			OrderStringId = txId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			Condition = regMsg.Condition,
			PositionEffect = regMsg.PositionEffect,
		}, cancellationToken);
	}

	public override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		var condition = replaceMsg.Condition as LigtherOrderCondition;

		if (!condition?.RawCancelTx.IsEmpty() == true)
			await RestClient.SendTxAsync(ParseRawPayload(condition.RawCancelTx, "LigtherOrderCondition.RawCancelTx"), condition.AuthToken, cancellationToken);

		await RegisterOrderAsync(new OrderRegisterMessage
		{
			TransactionId = replaceMsg.TransactionId,
			SecurityId = replaceMsg.SecurityId,
			Side = replaceMsg.Side,
			Price = replaceMsg.Price,
			Volume = replaceMsg.Volume,
			OrderType = replaceMsg.OrderType,
			TimeInForce = replaceMsg.TimeInForce,
			Condition = replaceMsg.Condition,
			PositionEffect = replaceMsg.PositionEffect,
			UserOrderId = replaceMsg.UserOrderId,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		var payload = ParseRawPayload(cancelMsg.OrderStringId, "OrderCancelMessage.OrderStringId");
		await RestClient.SendTxAsync(payload, null, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = cancelMsg.SecurityId,
			ServerTime = CurrentTime,
			PortfolioName = _portfolioName,
			OrderId = cancelMsg.OrderId,
			OrderStringId = cancelMsg.OrderStringId,
			OrderState = OrderStates.Done,
			Balance = 0,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	public override ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException("Ligther group cancel requires signed batch transaction payload via /api/v1/sendTxBatch.");
	}

	public override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (!lookupMsg.IsSubscribe)
			return;

		var account = await RestClient.GetAccountAsync("index", Owner.AccountIndex.ToString(CultureInfo.InvariantCulture), null, cancellationToken);
		var accountInfo = (account["accounts"] as JArray)?.FirstOrDefault() as JObject;
		if (accountInfo is null)
			return;

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCode,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);

		var serverTime = ParseUnixMicro(accountInfo["transaction_time"]) ?? CurrentTime;

		var collateral = accountInfo["collateral"]?.Value<string>().To<decimal?>();
		if (collateral is decimal c)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = "USDC".ToStockSharp(BoardCode),
				ServerTime = serverTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(PositionChangeTypes.CurrentValue, c, true), cancellationToken);
		}

		foreach (var asset in (accountInfo["assets"] as JArray)?.OfType<JObject>() ?? [])
		{
			var symbol = asset["symbol"]?.Value<string>();
			if (symbol.IsEmpty())
				continue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, asset["balance"]?.Value<string>().To<decimal?>(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, asset["locked_balance"]?.Value<string>().To<decimal?>(), true), cancellationToken);
		}

		if (!IsSpotSection)
		{
			foreach (var pos in (accountInfo["positions"] as JArray)?.OfType<JObject>() ?? [])
			{
				var marketId = pos["market_id"]?.Value<int?>() ?? pos["marketId"]?.Value<int?>();
				if (marketId is not int id || !_marketsById.ContainsKey(id))
					continue;

				var sign = pos["sign"]?.Value<int?>() ?? 1;
				var qty = pos["position"]?.Value<string>().To<decimal?>() ?? pos["size"]?.Value<string>().To<decimal?>();
				var side = sign < 0 ? Sides.Sell : (Sides?)Sides.Buy;

				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = _portfolioName,
					SecurityId = GetSecurityId(id),
					ServerTime = serverTime,
					OriginalTransactionId = lookupMsg.TransactionId,
					Side = side,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, qty, true)
				.TryAdd(PositionChangeTypes.AveragePrice, pos["avg_entry_price"]?.Value<string>().To<decimal?>(), true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, pos["unrealized_pnl"]?.Value<string>().To<decimal?>(), true)
				.TryAdd(PositionChangeTypes.RealizedPnL, pos["realized_pnl"]?.Value<string>().To<decimal?>(), true), cancellationToken);
			}
		}

	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (!statusMsg.IsSubscribe)
			return;

		await EnsureMarketsAsync(cancellationToken);
		var authToken = ResolveAuthToken(null);
		var marketId = statusMsg.SecurityId.SecurityCode.IsEmpty() ? (int?)null : ResolveMarketId(statusMsg.SecurityId);

		var inactive = await RestClient.GetAccountInactiveOrdersAsync(Owner.AccountIndex, 1.Max(statusMsg.Count?.To<int>() ?? 100), marketId, authToken, cancellationToken);
		foreach (var order in (inactive["orders"] as JArray)?.OfType<JObject>() ?? [])
		{
			var id = order["market_id"]?.Value<int?>() ?? order["marketId"]?.Value<int?>();
			if (id is not int mid || !_marketsById.ContainsKey(mid))
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = GetSecurityId(mid),
				ServerTime = ParseUnixMicro(order["transaction_time"]) ?? ParseUnixMs(order["created_at"]) ?? CurrentTime,
				PortfolioName = _portfolioName,
				Side = (order["side"]?.Value<string>() ?? "BUY").ToSide(),
				OrderVolume = order["size"]?.Value<string>().To<decimal?>(),
				Balance = order["remaining_base_amount"]?.Value<string>().To<decimal?>(),
				OrderPrice = order["price"]?.Value<string>().To<decimal?>() ?? 0m,
				OrderType = (order["type"]?.Value<string>()).ToOrderType(),
				OrderState = (order["status"]?.Value<string>()).ToOrderState(),
				OrderId = order["order_index"]?.Value<long?>(),
				OrderStringId = order["order_id"]?.Value<string>(),
				TimeInForce = (order["time_in_force"]?.Value<string>()).ToTimeInForce(),
				OriginalTransactionId = statusMsg.TransactionId,
			}, cancellationToken);
		}

		var txs = await RestClient.GetAccountTxsAsync(1.Max(statusMsg.Count?.To<int>() ?? 100), Owner.AccountIndex, marketId, authToken, cancellationToken);
		foreach (var tx in (txs["txs"] as JArray)?.OfType<JObject>() ?? [])
		{
			if (!tx["type"]?.Value<string>().EqualsIgnoreCase("trade") == true)
				continue;

			var id = tx["market_id"]?.Value<int?>() ?? tx["marketId"]?.Value<int?>();
			if (id is not int mid || !_marketsById.ContainsKey(mid))
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = GetSecurityId(mid),
				ServerTime = ParseUnixMicro(tx["transaction_time"]) ?? ParseUnixMs(tx["timestamp"]) ?? CurrentTime,
				PortfolioName = _portfolioName,
				OrderId = tx["order_index"]?.Value<long?>(),
				TradeId = tx["trade_id"]?.Value<long?>() ?? tx["id"]?.Value<long?>(),
				TradePrice = tx["price"]?.Value<string>().To<decimal?>(),
				TradeVolume = tx["size"]?.Value<string>().To<decimal?>(),
				Commission = tx["fee"]?.Value<string>().To<decimal?>(),
				Side = (tx["side"]?.Value<string>() ?? "BUY").ToSide(),
				OriginalTransactionId = statusMsg.TransactionId,
			}, cancellationToken);
		}

	}

	private async ValueTask EnsureMarketsAsync(CancellationToken cancellationToken)
	{
		if (_marketsById.Count > 0)
			return;

		var books = await RestClient.GetOrderBooksAsync(SectionFilter, null, cancellationToken);
		var orderBooks = books["order_books"] as JArray ?? [];

		foreach (var item in orderBooks.OfType<JObject>())
		{
			var marketId = item["market_id"]?.Value<int?>();
			var symbol = item["symbol"]?.Value<string>()?.ToUpperInvariant();
			var marketType = item["market_type"]?.Value<string>();
			var status = item["status"]?.Value<string>();

			if (marketId is not int id || symbol.IsEmpty())
				continue;

			if (!status.IsEmpty() && !status.EqualsIgnoreCase("active"))
				continue;

			if (IsSpotSection && !marketType.EqualsIgnoreCase("spot"))
				continue;

			if (!IsSpotSection && !marketType.EqualsIgnoreCase("perp"))
				continue;

			_marketsById[id] = item;
			_marketsBySymbol[symbol] = item;
		}
	}

	private int ResolveMarketId(SecurityId securityId)
	{
		var secCode = securityId.SecurityCode.ToUpperInvariant();
		if (secCode.IsEmpty())
			throw new InvalidOperationException("Security code is empty.");

		if (int.TryParse(secCode, out var id) && _marketsById.ContainsKey(id))
			return id;

		if (_marketsBySymbol.TryGetValue(secCode, out var market))
			return market["market_id"]?.Value<int>() ?? throw new InvalidOperationException($"Market id is missing for symbol {secCode}.");

		throw new InvalidOperationException($"Lighter market '{secCode}' is not found in metadata cache.");
	}

	private SecurityId GetSecurityId(int marketId)
		=> (_marketsById.TryGetValue(marketId, out var market)
			? market["symbol"]?.Value<string>()
			: null).ToStockSharp(BoardCode);

	private static string GetUnderlying(string symbol)
	{
		if (symbol.IsEmpty())
			return null;

		var slash = symbol.IndexOf('/', StringComparison.Ordinal);
		return slash > 0 ? symbol[..slash] : symbol;
	}

	private async ValueTask PublishLevel1Async(int marketId, long transactionId, CancellationToken cancellationToken)
	{
		var details = await RestClient.GetOrderBookDetailsAsync(SectionFilter, marketId, cancellationToken);
		var row = FindMarketInDetails(details, marketId);
		if (row is null)
			return;

		var orderBook = await RestClient.GetOrderBookOrdersAsync(marketId, 1, cancellationToken);
		var bestBid = (orderBook["bids"] as JArray)?.FirstOrDefault() as JObject;
		var bestAsk = (orderBook["asks"] as JArray)?.FirstOrDefault() as JObject;

		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = GetSecurityId(marketId),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, row["last_trade_price"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.HighPrice, row["daily_price_high"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.LowPrice, row["daily_price_low"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.Volume, row["daily_base_token_volume"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.BestBidPrice, bestBid?["price"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.BestBidVolume, bestBid?["remaining_base_amount"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.BestAskPrice, bestAsk?["price"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.BestAskVolume, bestAsk?["remaining_base_amount"]?.Value<string>().To<decimal?>()),
		cancellationToken);
	}

	private async ValueTask PublishDepthAsync(int marketId, int? depth, long transactionId, CancellationToken cancellationToken)
	{
		var response = await RestClient.GetOrderBookOrdersAsync(marketId, depth, cancellationToken);

		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = GetSecurityId(marketId),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(response["bids"], depth),
			Asks = ToQuotes(response["asks"], depth),
		}, cancellationToken);
	}

	private async ValueTask PollTicksAsync(TickState state, CancellationToken cancellationToken)
	{
		var lastId = state.LastTradeId;
		var lastTime = state.LastTime;
		var rows = GetTradeRows(await RestClient.GetRecentTradesAsync(state.MarketId, 100, cancellationToken));

		foreach (var trade in rows.OrderBy(static t => t["timestamp"]?.Value<long>() ?? 0))
		{
			var tradeId = trade["trade_id"]?.Value<long?>() ?? 0;
			if (tradeId <= state.LastTradeId)
				continue;

			var serverTime = ParseUnixMs(trade["timestamp"]) ?? CurrentTime;
			await SendOutMessageAsync(CreateTickMessage(state.MarketId, state.TransactionId, trade, tradeId, serverTime), cancellationToken);

			lastId = tradeId;
			lastTime = serverTime;
		}

		using (_sync.EnterScope())
		{
			if (_ticksRealtime.TryGetValue(state.TransactionId, out var stored))
			{
				stored.LastTradeId = lastId;
				stored.LastTime = lastTime;
			}
		}
	}

	private async ValueTask PollCandlesAsync(CandleState state, CancellationToken cancellationToken)
	{
		var rows = GetTradeRows(await RestClient.GetRecentTradesAsync(state.MarketId, _maxTradesLimit, cancellationToken));
		var from = state.LastOpenTime == default ? DateTime.UtcNow - TimeSpan.FromHours(6) : state.LastOpenTime;
		var to = DateTime.UtcNow;
		var lastOpen = state.LastOpenTime;

		foreach (var candle in BuildCandles(rows, state.MarketId, state.TimeFrame, from, to))
		{
			if (candle.OpenTime <= state.LastOpenTime)
				continue;

			candle.OriginalTransactionId = state.TransactionId;
			await SendOutMessageAsync(candle, cancellationToken);
			lastOpen = candle.OpenTime;
		}

		using (_sync.EnterScope())
		{
			if (_candlesRealtime.TryGetValue(state.TransactionId, out var stored))
				stored.LastOpenTime = lastOpen;
		}
	}

	private ExecutionMessage CreateTickMessage(int marketId, long transactionId, JObject trade, long tradeId, DateTime serverTime)
	{
		var makerAsk = trade["is_maker_ask"]?.Value<bool?>();

		return new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = GetSecurityId(marketId),
			ServerTime = serverTime,
			TradeId = tradeId,
			TradePrice = trade["price"]?.Value<string>().To<decimal?>(),
			TradeVolume = trade["size"]?.Value<string>().To<decimal?>(),
			OriginSide = makerAsk is bool a ? (a ? Sides.Buy : Sides.Sell) : null,
			OriginalTransactionId = transactionId,
		};
	}

	private IEnumerable<TimeFrameCandleMessage> BuildCandles(IEnumerable<JObject> trades, int marketId, TimeSpan timeFrame, DateTime from, DateTime to)
	{
		var buckets = new SortedDictionary<DateTime, List<(decimal Price, decimal Volume)>>();

		foreach (var trade in trades)
		{
			var time = ParseUnixMs(trade["timestamp"]) ?? CurrentTime;
			if (time < from || time > to)
				continue;

			var openTime = FloorTime(time, timeFrame);
			var price = trade["price"]?.Value<string>().To<decimal?>();
			var volume = trade["size"]?.Value<string>().To<decimal?>();
			if (price is not decimal p || volume is not decimal v)
				continue;

			if (!buckets.TryGetValue(openTime, out var list))
			{
				list = [];
				buckets[openTime] = list;
			}

			list.Add((p, v));
		}

		foreach (var pair in buckets)
		{
			var prices = pair.Value.Select(static x => x.Price).ToArray();
			var volume = pair.Value.Sum(static x => x.Volume);

			yield return new TimeFrameCandleMessage
			{
				SecurityId = GetSecurityId(marketId),
				TypedArg = timeFrame,
				OpenTime = pair.Key,
				OpenPrice = prices.FirstOrDefault(),
				HighPrice = prices.Length == 0 ? 0m : prices.Max(),
				LowPrice = prices.Length == 0 ? 0m : prices.Min(),
				ClosePrice = prices.LastOrDefault(),
				TotalVolume = volume,
				State = CandleStates.Finished,
			};
		}
	}

	private static DateTime FloorTime(DateTime value, TimeSpan step)
	{
		if (step <= TimeSpan.Zero)
			return value;

		var ticks = value.Ticks - (value.Ticks % step.Ticks);
		return new DateTime(ticks, value.Kind);
	}

	private static IEnumerable<JObject> GetTradeRows(JObject response)
		=> (response["trades"] as JArray)?.OfType<JObject>() ?? [];

	private static DateTime? ParseUnixMs(JToken token)
	{
		var value = token?.Value<long?>();

		if (value is not long unix || unix <= 0)
			return null;

		// Lighter payloads may return unix seconds or milliseconds depending on endpoint/channel.
		return unix < 1_000_000_000_000L
			? unix.FromUnix()
			: unix.FromUnix(false);
	}

	private static DateTime? ParseUnixMicro(JToken token)
	{
		var value = token?.Value<long?>();
		return value is long unix && unix > 0 ? (unix / 1000).FromUnix(false) : null;
	}

	private ValueTask OnWsErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnWsOrderBookAsync(JObject payload, CancellationToken cancellationToken)
	{
		var marketId = ExtractMarketId(payload, "order_book");
		if (marketId is not int id)
			return;

		var orderBook = payload["order_book"] as JObject ?? payload["data"] as JObject ?? payload;
		var bidsToken = orderBook["bids"] ?? payload["bids"];
		var asksToken = orderBook["asks"] ?? payload["asks"];

		if (bidsToken is null && asksToken is null)
			return;

		(long TransactionId, int? Depth)[] depthSubscriptions;
		long[] level1Subscriptions;

		using (_sync.EnterScope())
		{
			depthSubscriptions =
			[
				.. _depthRealtime
					.Where(p => p.Value.MarketId == id)
					.Select(static p => (p.Key, p.Value.Depth))
			];

			level1Subscriptions =
			[
				.. _level1Realtime
					.Where(p => p.Value == id)
					.Select(static p => p.Key)
			];
		}

		var serverTime = ParseUnixMs(orderBook["timestamp"] ?? payload["timestamp"]) ?? CurrentTime;
		var seqNum = orderBook["nonce"]?.Value<long?>() ?? payload["nonce"]?.Value<long?>() ?? 0;

		foreach (var sub in depthSubscriptions)
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = GetSecurityId(id),
				ServerTime = serverTime,
				OriginalTransactionId = sub.TransactionId,
				State = QuoteChangeStates.SnapshotComplete,
				SeqNum = seqNum,
				Bids = ToQuotes(bidsToken, sub.Depth),
				Asks = ToQuotes(asksToken, sub.Depth),
			}, cancellationToken);
		}

		if (level1Subscriptions.Length == 0)
			return;

		var bestBid = (bidsToken as JArray)?.FirstOrDefault() as JObject;
		var bestAsk = (asksToken as JArray)?.FirstOrDefault() as JObject;
		var bestBidPrice = bestBid?["price"]?.Value<string>().To<decimal?>();
		var bestBidVolume = bestBid?["remaining_base_amount"]?.Value<string>().To<decimal?>()
			?? bestBid?["size"]?.Value<string>().To<decimal?>();
		var bestAskPrice = bestAsk?["price"]?.Value<string>().To<decimal?>();
		var bestAskVolume = bestAsk?["remaining_base_amount"]?.Value<string>().To<decimal?>()
			?? bestAsk?["size"]?.Value<string>().To<decimal?>();

		foreach (var subId in level1Subscriptions)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = GetSecurityId(id),
				ServerTime = serverTime,
				OriginalTransactionId = subId,
			}
			.TryAdd(Level1Fields.BestBidPrice, bestBidPrice)
			.TryAdd(Level1Fields.BestBidVolume, bestBidVolume)
			.TryAdd(Level1Fields.BestAskPrice, bestAskPrice)
			.TryAdd(Level1Fields.BestAskVolume, bestAskVolume),
			cancellationToken);
		}
	}

	private ValueTask OnWsMarketStatsAsync(JObject payload, CancellationToken cancellationToken)
		=> OnWsStatsAsync(payload, "market_stats", cancellationToken);

	private ValueTask OnWsSpotMarketStatsAsync(JObject payload, CancellationToken cancellationToken)
		=> OnWsStatsAsync(payload, "spot_market_stats", cancellationToken);

	private async ValueTask OnWsStatsAsync(JObject payload, string key, CancellationToken cancellationToken)
	{
		foreach (var stat in ExtractStatsRows(payload, key))
		{
			var marketId = stat["market_id"]?.Value<int?>()
				?? stat["marketId"]?.Value<int?>();

			if (marketId is not int id)
				continue;

			long[] subscriptions;

			using (_sync.EnterScope())
			{
				subscriptions =
				[
					.. _level1Realtime
						.Where(p => p.Value == id)
						.Select(static p => p.Key)
				];
			}

			if (subscriptions.Length == 0)
				continue;

			var serverTime = ParseUnixMs(stat["timestamp"] ?? payload["timestamp"]) ?? CurrentTime;
			var last = stat["last_price"]?.Value<string>().To<decimal?>();
			var high = stat["daily_price_high"]?.Value<string>().To<decimal?>()
				?? stat["high"]?.Value<string>().To<decimal?>();
			var low = stat["daily_price_low"]?.Value<string>().To<decimal?>()
				?? stat["low"]?.Value<string>().To<decimal?>();
			var volume = stat["daily_base_token_volume"]?.Value<string>().To<decimal?>()
				?? stat["volume"]?.Value<string>().To<decimal?>();

			foreach (var subId in subscriptions)
			{
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = GetSecurityId(id),
					ServerTime = serverTime,
					OriginalTransactionId = subId,
				}
				.TryAdd(Level1Fields.LastTradePrice, last)
				.TryAdd(Level1Fields.HighPrice, high)
				.TryAdd(Level1Fields.LowPrice, low)
				.TryAdd(Level1Fields.Volume, volume),
				cancellationToken);
			}
		}
	}

	private async ValueTask OnWsTradeAsync(JObject payload, CancellationToken cancellationToken)
	{
		var marketId = ExtractMarketId(payload, "trade");
		if (marketId is not int id)
			return;

		IEnumerable<JObject> trades = (payload["trades"] as JArray)?.OfType<JObject>() ?? [];

		if (!trades.Any() && payload["trade"] is JObject singleTrade)
			trades = [singleTrade];

		var orderedTrades = trades
			.OrderBy(static t => t["trade_id"]?.Value<long?>() ?? 0)
			.ThenBy(static t => t["timestamp"]?.Value<long?>() ?? 0)
			.ToArray();

		if (orderedTrades.Length == 0)
			return;

		foreach (var trade in orderedTrades)
		{
			var tradeId = trade["trade_id"]?.Value<long?>()
				?? trade["id"]?.Value<long?>()
				?? 0;

			if (tradeId <= 0)
				continue;

			var serverTime = ParseUnixMs(trade["timestamp"] ?? payload["timestamp"]) ?? CurrentTime;

			TickState[] tickStates;
			CandleState[] candleStates;

			using (_sync.EnterScope())
			{
				tickStates =
				[
					.. _ticksRealtime.Values
						.Where(s => s.MarketId == id)
				];

				candleStates =
				[
					.. _candlesRealtime.Values
						.Where(s => s.MarketId == id)
				];
			}

			foreach (var state in tickStates)
			{
				if (tradeId <= state.LastTradeId)
					continue;

				var tickMsg = CreateTickMessage(id, state.TransactionId, trade, tradeId, serverTime);
				await SendOutMessageAsync(tickMsg, cancellationToken);

				using (_sync.EnterScope())
				{
					if (_ticksRealtime.TryGetValue(state.TransactionId, out var stored))
					{
						stored.LastTradeId = tradeId;
						stored.LastTime = serverTime;
					}
				}
			}

			var price = trade["price"]?.Value<string>().To<decimal?>();
			var volume = trade["size"]?.Value<string>().To<decimal?>();

			if (price is not decimal p || volume is not decimal v)
				continue;

			foreach (var state in candleStates)
				await ProcessCandleTradeAsync(state.TransactionId, tradeId, serverTime, p, v, cancellationToken);
		}
	}

	private async ValueTask ProcessCandleTradeAsync(long transactionId, long tradeId, DateTime serverTime, decimal price, decimal volume, CancellationToken cancellationToken)
	{
		TimeFrameCandleMessage finished = null;

		using (_sync.EnterScope())
		{
			if (!_candlesRealtime.TryGetValue(transactionId, out var state))
				return;

			if (tradeId <= state.LastTradeId)
				return;

			var openTime = FloorTime(serverTime, state.TimeFrame);

			if (state.CurrentOpenTime == default)
			{
				if (openTime <= state.LastOpenTime)
				{
					state.LastTradeId = tradeId;
					return;
				}

				state.CurrentOpenTime = openTime;
				state.OpenPrice = price;
				state.HighPrice = price;
				state.LowPrice = price;
				state.ClosePrice = price;
				state.TotalVolume = volume;
				state.LastTradeId = tradeId;
				return;
			}

			if (openTime < state.CurrentOpenTime)
			{
				state.LastTradeId = tradeId;
				return;
			}

			if (openTime > state.CurrentOpenTime)
			{
				if (state.OpenPrice is decimal o
					&& state.HighPrice is decimal h
					&& state.LowPrice is decimal l
					&& state.ClosePrice is decimal c)
				{
					finished = new TimeFrameCandleMessage
					{
						SecurityId = GetSecurityId(state.MarketId),
						TypedArg = state.TimeFrame,
						OpenTime = state.CurrentOpenTime,
						CloseTime = state.CurrentOpenTime + state.TimeFrame,
						OpenPrice = o,
						HighPrice = h,
						LowPrice = l,
						ClosePrice = c,
						TotalVolume = state.TotalVolume,
						State = CandleStates.Finished,
						OriginalTransactionId = state.TransactionId,
					};
				}

				state.LastOpenTime = state.CurrentOpenTime;
				state.CurrentOpenTime = openTime;
				state.OpenPrice = price;
				state.HighPrice = price;
				state.LowPrice = price;
				state.ClosePrice = price;
				state.TotalVolume = volume;
				state.LastTradeId = tradeId;
				return;
			}

			state.HighPrice = state.HighPrice is decimal high ? high.Max(price) : price;
			state.LowPrice = state.LowPrice is decimal low ? low.Min(price) : price;
			state.ClosePrice = price;
			state.TotalVolume += volume;
			state.LastTradeId = tradeId;
		}

		if (finished is not null)
			await SendOutMessageAsync(finished, cancellationToken);
	}

	private static IEnumerable<JObject> ExtractStatsRows(JObject payload, string key)
	{
		var token = payload[key];

		if (token is JObject obj)
		{
			if (obj["market_id"] is not null || obj["marketId"] is not null)
			{
				yield return obj;
				yield break;
			}

			foreach (var property in obj.Properties())
			{
				if (property.Value is JObject stat)
				{
					if (stat["market_id"] is null && int.TryParse(property.Name, out var marketId))
						stat["market_id"] = marketId;

					yield return stat;
				}
			}

			yield break;
		}

		if (token is JArray arr)
		{
			foreach (var item in arr.OfType<JObject>())
				yield return item;
		}
	}

	private static int? ExtractMarketId(JObject payload, string prefix)
	{
		if (payload["market_id"]?.Value<int?>() is int marketId)
			return marketId;

		if (payload["marketId"]?.Value<int?>() is int marketId2)
			return marketId2;

		var channel = payload["channel"]?.Value<string>();
		if (channel.IsEmpty())
			return null;

		var normalizedPrefix = prefix.TrimEnd(':', '/');

		if (!channel.StartsWithIgnoreCase(normalizedPrefix))
			return null;

		var separator = channel.IndexOf(':');
		if (separator < 0)
			separator = channel.LastIndexOf('/');

		if (separator < 0 || separator + 1 >= channel.Length)
			return null;

		var idPart = channel[(separator + 1)..];
		return int.TryParse(idPart, out var parsed) ? parsed : null;
	}

	private QuoteChange[] ToQuotes(JToken token, int? depth)
	{
		if (token is not JArray arr || arr.Count == 0)
			return [];

		var maxDepth = 1.Max(depth ?? int.MaxValue);
		var result = new List<QuoteChange>(maxDepth.Min(arr.Count));

		foreach (var row in arr.OfType<JObject>().Take(maxDepth))
		{
			var price = row["price"]?.Value<string>().To<decimal?>();
			var volume = row["remaining_base_amount"]?.Value<string>().To<decimal?>()
				?? row["size"]?.Value<string>().To<decimal?>();

			if (price is decimal p && volume is decimal v)
				result.Add(new QuoteChange(p, v));
		}

		return [.. result];
	}

	private JObject FindMarketInDetails(JObject details, int marketId)
	{
		foreach (var key in new[] { "order_book_details", "spot_order_book_details" })
		{
			foreach (var item in (details[key] as JArray)?.OfType<JObject>() ?? [])
			{
				if (item["market_id"]?.Value<int?>() == marketId)
					return item;
			}
		}

		return null;
	}

	private string ResolveAuthToken(LigtherOrderCondition condition)
	{
		var token = condition?.AuthToken;
		if (token.IsEmpty())
			token = Owner.Key.UnSecure();

		if (token.IsEmpty() && !Owner.Secret.IsEmpty())
			token = Owner.Secret.UnSecure();

		return token;
	}

	private static JObject ParseRawPayload(string raw, string sourceName)
	{
		if (raw.IsEmpty())
			throw new InvalidOperationException($"{sourceName} must contain raw JSON transaction payload.");

		try
		{
			return JObject.Parse(raw);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"{sourceName} contains invalid JSON payload.", ex);
		}
	}

	private void EnsureTradingReady()
	{
		if (!SupportsTrading)
			throw new NotSupportedException($"Ligther {SectionName} trading is unavailable for this section.");

		var token = Owner.Key.UnSecure();
		if (token.IsEmpty() && Owner.Secret.IsEmpty())
			throw new InvalidOperationException("Authorization token (Key) or auth secret is required for private Ligther operations.");
	}

	private async ValueTask RegisterLevel1Async(long transactionId, int marketId, CancellationToken cancellationToken)
	{
		var orderBookChannel = GetOrderBookChannel(marketId);
		var statsChannel = GetStatsChannel(marketId);
		var subscribeBook = false;
		var subscribeStats = false;

		using (_sync.EnterScope())
		{
			_level1Realtime[transactionId] = marketId;
			subscribeBook = AddRef(_orderBookRefs, orderBookChannel);
			subscribeStats = AddRef(_statsRefs, statsChannel);
		}

		if (subscribeBook && _wsClient is not null)
			await WsClient.SubscribeAsync(orderBookChannel, null, cancellationToken);

		if (subscribeStats && _wsClient is not null)
			await WsClient.SubscribeAsync(statsChannel, null, cancellationToken);
	}

	private async ValueTask UnregisterLevel1Async(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		string orderBookChannel = null;
		string statsChannel = null;
		var unsubscribeBook = false;
		var unsubscribeStats = false;

		using (_sync.EnterScope())
		{
			if (_level1Realtime.TryGetValue(originalTransactionId, out var marketId))
			{
				_level1Realtime.Remove(originalTransactionId);
				orderBookChannel = GetOrderBookChannel(marketId);
				statsChannel = GetStatsChannel(marketId);
				unsubscribeBook = ReleaseRef(_orderBookRefs, orderBookChannel);
				unsubscribeStats = ReleaseRef(_statsRefs, statsChannel);
			}
		}

		if (unsubscribeBook && _wsClient is not null)
			await WsClient.UnsubscribeAsync(orderBookChannel, cancellationToken);

		if (unsubscribeStats && _wsClient is not null)
			await WsClient.UnsubscribeAsync(statsChannel, cancellationToken);
	}

	private async ValueTask RegisterDepthAsync(long transactionId, int marketId, int? depth, CancellationToken cancellationToken)
	{
		var channel = GetOrderBookChannel(marketId);
		var shouldSubscribe = false;

		using (_sync.EnterScope())
		{
			_depthRealtime[transactionId] = (marketId, depth);
			shouldSubscribe = AddRef(_orderBookRefs, channel);
		}

		if (shouldSubscribe && _wsClient is not null)
			await WsClient.SubscribeAsync(channel, null, cancellationToken);
	}

	private async ValueTask UnregisterDepthAsync(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		string channel = null;
		var shouldUnsubscribe = false;

		using (_sync.EnterScope())
		{
			if (_depthRealtime.TryGetValue(originalTransactionId, out var state))
			{
				_depthRealtime.Remove(originalTransactionId);
				channel = GetOrderBookChannel(state.MarketId);
				shouldUnsubscribe = ReleaseRef(_orderBookRefs, channel);
			}
		}

		if (shouldUnsubscribe && _wsClient is not null)
			await WsClient.UnsubscribeAsync(channel, cancellationToken);
	}

	private async ValueTask RegisterTicksAsync(long transactionId, int marketId, DateTime lastTime, long lastTradeId, CancellationToken cancellationToken)
	{
		var channel = GetTradeChannel(marketId);
		var shouldSubscribe = false;

		using (_sync.EnterScope())
		{
			_ticksRealtime[transactionId] = new()
			{
				MarketId = marketId,
				TransactionId = transactionId,
				LastTime = lastTime,
				LastTradeId = lastTradeId,
			};

			shouldSubscribe = AddRef(_tradeRefs, channel);
		}

		if (shouldSubscribe && _wsClient is not null)
			await WsClient.SubscribeAsync(channel, null, cancellationToken);
	}

	private async ValueTask UnregisterTicksAsync(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		string channel = null;
		var shouldUnsubscribe = false;

		using (_sync.EnterScope())
		{
			if (_ticksRealtime.TryGetValue(originalTransactionId, out var state))
			{
				_ticksRealtime.Remove(originalTransactionId);
				channel = GetTradeChannel(state.MarketId);
				shouldUnsubscribe = ReleaseRef(_tradeRefs, channel);
			}
		}

		if (shouldUnsubscribe && _wsClient is not null)
			await WsClient.UnsubscribeAsync(channel, cancellationToken);
	}

	private async ValueTask RegisterCandlesAsync(long transactionId, int marketId, TimeSpan timeFrame, DateTime lastOpenTime, CancellationToken cancellationToken)
	{
		var channel = GetTradeChannel(marketId);
		var shouldSubscribe = false;

		using (_sync.EnterScope())
		{
			_candlesRealtime[transactionId] = new()
			{
				MarketId = marketId,
				TransactionId = transactionId,
				TimeFrame = timeFrame,
				LastOpenTime = lastOpenTime,
				LastTradeId = 0,
				CurrentOpenTime = default,
				TotalVolume = 0m,
			};

			shouldSubscribe = AddRef(_tradeRefs, channel);
		}

		if (shouldSubscribe && _wsClient is not null)
			await WsClient.SubscribeAsync(channel, null, cancellationToken);
	}

	private async ValueTask UnregisterCandlesAsync(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (originalTransactionId <= 0)
			return;

		string channel = null;
		var shouldUnsubscribe = false;

		using (_sync.EnterScope())
		{
			if (_candlesRealtime.TryGetValue(originalTransactionId, out var state))
			{
				_candlesRealtime.Remove(originalTransactionId);
				channel = GetTradeChannel(state.MarketId);
				shouldUnsubscribe = ReleaseRef(_tradeRefs, channel);
			}
		}

		if (shouldUnsubscribe && _wsClient is not null)
			await WsClient.UnsubscribeAsync(channel, cancellationToken);
	}

	private void ClearRealtimeSubscriptions()
	{
		using (_sync.EnterScope())
		{
			_level1Realtime.Clear();
			_depthRealtime.Clear();
			_ticksRealtime.Clear();
			_candlesRealtime.Clear();

			_orderBookRefs.Clear();
			_tradeRefs.Clear();
			_statsRefs.Clear();
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

	private static string GetOrderBookChannel(int marketId)
		=> $"order_book/{marketId}";

	private string GetStatsChannel(int marketId)
		=> (IsSpotSection ? "spot_market_stats" : "market_stats") + "/" + marketId;

	private static string GetTradeChannel(int marketId)
		=> $"trade/{marketId}";

}
