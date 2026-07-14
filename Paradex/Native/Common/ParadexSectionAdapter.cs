namespace StockSharp.Paradex.Native.Common;

using DerivativesMarket = StockSharp.Paradex.Native.Derivatives.Model.Market;
using SpotMarket = StockSharp.Paradex.Native.Spot.Model.Market;

abstract class ParadexSectionAdapter : BaseNativeAdapter
{
	private const int _maxTradesLimit = 500;
	private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

	private ParadexRestClient _restClient;
	private string _portfolioName;
	private readonly Lock _sync = new();
	private readonly Dictionary<long, string> _level1Realtime = [];
	private readonly Dictionary<long, (string Market, int? Depth)> _depthRealtime = [];
	private readonly Dictionary<long, TickState> _ticksRealtime = [];
	private readonly Dictionary<long, CandleState> _candlesRealtime = [];
	private DateTime _lastPollTime;

	private readonly Dictionary<string, JObject> _marketsBySymbol = new(StringComparer.InvariantCultureIgnoreCase);

	private sealed class TickState
	{
		public string Market { get; init; }
		public long TransactionId { get; init; }
		public string LastTradeId { get; set; }
		public DateTime LastTime { get; set; }
	}

	private sealed class CandleState
	{
		public string Market { get; init; }
		public long TransactionId { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
	}

	protected ParadexSectionAdapter(ParadexMessageAdapter owner, string boardCode)
		: base(owner, boardCode)
	{
	}

	protected abstract bool IsSpotSection { get; }
	protected abstract SecurityTypes SectionSecurityType { get; }
	protected virtual bool SupportsTrading => true;

	protected ParadexRestClient RestClient => _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	public override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		await base.ConnectAsync(connectMsg, cancellationToken);

		var restEndpoint = IsSpotSection ? Owner.SpotRestEndpoint : Owner.DerivativesRestEndpoint;
		var wsEndpoint = IsSpotSection ? Owner.SpotWsEndpoint : Owner.DerivativesWsEndpoint;

		if (restEndpoint.IsEmpty())
			throw new InvalidOperationException($"REST endpoint for section '{SectionName}' is not configured.");

		if (wsEndpoint.IsEmpty())
			throw new InvalidOperationException($"WS endpoint for section '{SectionName}' is not configured.");

		_restClient = new(restEndpoint, Owner.Key, Owner.Secret, Owner.StarknetAccount, Owner.StarknetPrivateKey, Owner.AuthPath) { Parent = Owner };
		_portfolioName = $"{nameof(Paradex)}_{SectionName}_{(!Owner.StarknetAccount.IsEmpty() ? Owner.StarknetAccount : (Owner.Key.IsEmpty() ? "Public" : Owner.Key.ToId()))}";

		await EnsureMarketsAsync(cancellationToken);
		ClearRealtimeSubscriptions();
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
			_restClient?.Dispose();
			_restClient = null;
		}
	}

	public override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		ClearRealtimeSubscriptions();
		_restClient?.Dispose();
		_restClient = null;
		_marketsBySymbol.Clear();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	public override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		var now = DateTime.UtcNow;

		if (now - _lastPollTime < _pollInterval)
			return;

		_lastPollTime = now;

		Dictionary<long, string> level1;
		Dictionary<long, (string Market, int? Depth)> depth;
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
			try { await PublishLevel1Async(pair.Value, pair.Key, cancellationToken); }
			catch (Exception ex) { await SendOutErrorAsync(ex, cancellationToken); }
		}

		foreach (var pair in depth)
		{
			try { await PublishDepthAsync(pair.Value.Market, pair.Value.Depth, pair.Key, cancellationToken); }
			catch (Exception ex) { await SendOutErrorAsync(ex, cancellationToken); }
		}

		foreach (var state in ticks)
		{
			try { await PollTicksAsync(state, cancellationToken); }
			catch (Exception ex) { await SendOutErrorAsync(ex, cancellationToken); }
		}

		foreach (var state in candles)
		{
			try { await PollCandlesAsync(state, cancellationToken); }
			catch (Exception ex) { await SendOutErrorAsync(ex, cancellationToken); }
		}
	}

	public override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await EnsureMarketsAsync(cancellationToken);

		var secTypes = lookupMsg.GetSecurityTypes();
		if (secTypes.Count > 0 && !secTypes.Contains(SectionSecurityType))
			return;

		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var market in _marketsBySymbol.Values)
		{
			var symbol = market["symbol"]?.Value<string>()?.ToUpperInvariant();
			if (symbol.IsEmpty() || !IsMarketForSection(market))
				continue;

			var secMsg = new SecurityMessage
			{
				SecurityId = symbol.ToStockSharp(BoardCode),
				Name = symbol,
				SecurityType = SectionSecurityType,
				PriceStep = market["price_tick_size"]?.Value<string>().To<decimal?>(),
				VolumeStep = market["order_size_increment"]?.Value<string>().To<decimal?>(),
				UnderlyingSecurityMinVolume = market["min_notional"]?.Value<string>().To<decimal?>(),
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryFillUnderlyingId(market["base_currency"]?.Value<string>()?.ToUpperInvariant());

			var expiryUnix = market["expiry_at"]?.Value<long?>() ?? market["expiry_at"]?.Value<string>().To<long?>();
			if (expiryUnix is long eu && eu > 0)
				secMsg.ExpiryDate = eu.FromUnix(false);

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
			UnregisterLevel1(mdMsg.OriginalTransactionId);
			return;
		}

		var market = ResolveMarket(mdMsg.SecurityId);
		await PublishLevel1Async(market, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			RegisterLevel1(mdMsg.TransactionId, market);
	}

	public override async ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			UnregisterDepth(mdMsg.OriginalTransactionId);
			return;
		}

		var market = ResolveMarket(mdMsg.SecurityId);
		await PublishDepthAsync(market, mdMsg.MaxDepth, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			RegisterDepth(mdMsg.TransactionId, market, mdMsg.MaxDepth);
	}

	public override async ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			UnregisterTicks(mdMsg.OriginalTransactionId);
			return;
		}

		var market = ResolveMarket(mdMsg.SecurityId);
		var from = mdMsg.From ?? (DateTime.UtcNow - TimeSpan.FromHours(4));
		var to = mdMsg.To ?? DateTime.UtcNow;
		var left = mdMsg.Count ?? long.MaxValue;
		var lastTime = from;
		string lastTradeId = null;

		foreach (var trade in GetTradeRows(await RestClient.GetTradesAsync(market, _maxTradesLimit, cancellationToken))
			.OrderBy(static t => t["created_at"]?.Value<long?>() ?? t["created_at"]?.Value<string>().To<long?>() ?? 0L))
		{
			var serverTime = ParseUnixMs(trade["created_at"]) ?? CurrentTime;
			if (serverTime < from)
				continue;
			if (serverTime > to)
				break;

			var tradeId = trade["id"]?.Value<string>() ?? serverTime.ToUnix(false).To<string>();
			if (!lastTradeId.IsEmpty() && CompareTradeIds(tradeId, lastTradeId) <= 0)
				continue;

			await SendOutMessageAsync(CreateTickMessage(market, mdMsg.TransactionId, trade, tradeId, serverTime), cancellationToken);
			lastTradeId = tradeId;
			lastTime = serverTime;

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			RegisterTicks(mdMsg.TransactionId, market, lastTime, lastTradeId);
	}

	public override async ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			UnregisterCandles(mdMsg.OriginalTransactionId);
			return;
		}

		var market = ResolveMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var from = mdMsg.From ?? (DateTime.UtcNow - TimeSpan.FromDays(1));
		var to = mdMsg.To ?? DateTime.UtcNow;
		var left = mdMsg.Count ?? long.MaxValue;
		var lastOpen = from;

		var trades = GetTradeRows(await RestClient.GetTradesAsync(market, _maxTradesLimit, cancellationToken));
		foreach (var candle in BuildCandles(market, trades, timeFrame, from, to))
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
			RegisterCandles(mdMsg.TransactionId, market, timeFrame, lastOpen);
	}

	public override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = regMsg.Condition as ParadexOrderCondition;
		var token = condition?.AuthToken;

		EnsureTradingReady(token);

		var market = ResolveMarket(regMsg.SecurityId);
		var payload = BuildRegisterPayload(regMsg, market);
		var response = await RestClient.CreateOrderAsync(payload, token, cancellationToken);
		var order = ExtractEntity(response, "result", "results", "data", "order") ?? response;

		var orderId = ResolveOrderId(order);
		var orderStringId = ResolveOrderStringId(order) ?? payload["client_id"]?.Value<string>();
		var orderType = (order["type"]?.Value<string>()).ToOrderType() ?? regMsg.OrderType ?? OrderTypes.Limit;
		var side = ParseSide(order["side"]?.Value<string>()) ?? regMsg.Side;
		var serverTime = ParseUnixMs(order["created_at"] ?? order["updated_at"] ?? order["timestamp"]) ?? CurrentTime;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(BoardCode),
			ServerTime = serverTime,
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = order["size"]?.Value<string>().To<decimal?>() ?? regMsg.Volume.Abs(),
			Balance = order["remaining_size"]?.Value<string>().To<decimal?>() ?? order["remaining"]?.Value<string>().To<decimal?>() ?? regMsg.Volume.Abs(),
			OrderPrice = order["price"]?.Value<string>().To<decimal?>() ?? regMsg.Price,
			OrderType = orderType,
			OrderState = (order["status"]?.Value<string>()).ToOrderState(),
			OrderId = orderId,
			OrderStringId = orderStringId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = (order["time_in_force"]?.Value<string>() ?? order["tif"]?.Value<string>()).ToTimeInForce(),
			PostOnly = order["post_only"]?.Value<bool?>() ?? regMsg.PostOnly,
			Condition = regMsg.Condition,
			PositionEffect = regMsg.PositionEffect,
		}, cancellationToken);
	}

	public override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var token = (replaceMsg.Condition as ParadexOrderCondition)?.AuthToken;
		EnsureTradingReady(token);

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

		var market = ResolveMarket(cancelMsg.SecurityId);
		var orderId = cancelMsg.OrderId?.To<string>();
		var orderStringId = cancelMsg.OrderStringId;

		if (orderId.IsEmpty() && orderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.TransactionId));

		if (!orderId.IsEmpty())
		{
			await RestClient.CancelOrderAsync(orderId, null, cancellationToken);
		}
		else
		{
			try
			{
				await RestClient.CancelOrderAsync(orderStringId, null, cancellationToken);
			}
			catch
			{
				await RestClient.CancelByClientIdAsync(orderStringId, null, cancellationToken);
			}
		}

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(BoardCode),
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
			throw new NotSupportedException("ClosePositions mode is not supported by Paradex REST API.");

		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty() ? null : ResolveMarket(cancelMsg.SecurityId);
		var sideFilter = cancelMsg.Side;
		var stopFilter = cancelMsg.IsStop;

		if (sideFilter is null && stopFilter is null)
		{
			await RestClient.CancelAllOrdersAsync(market, null, cancellationToken);
			return;
		}

		var ordersResponse = await RestClient.GetOrdersAsync(market, null, cancellationToken);
		foreach (var order in ExtractObjects(ordersResponse, "results", "orders", "data", "list"))
		{
			var side = ParseSide(order["side"]?.Value<string>());
			if (sideFilter is Sides sf && side != sf)
				continue;

			var orderType = order["type"]?.Value<string>();
			if (stopFilter is bool isStop && IsStopType(orderType) != isStop)
				continue;

			var id = ResolveOrderStringId(order);
			if (id.IsEmpty())
				continue;

			await RestClient.CancelOrderAsync(id, null, cancellationToken);
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

		var balances = await RestClient.GetBalancesAsync(null, cancellationToken);
		foreach (var balance in ExtractObjects(balances, "results", "balances", "data", "list"))
		{
			var asset = ResolveAssetCode(balance);
			if (asset.IsEmpty())
				continue;

			var total = balance["total"]?.Value<string>().To<decimal?>()
				?? balance["balance"]?.Value<string>().To<decimal?>()
				?? balance["wallet_balance"]?.Value<string>().To<decimal?>()
				?? balance["equity"]?.Value<string>().To<decimal?>();

			var available = balance["available"]?.Value<string>().To<decimal?>()
				?? balance["free"]?.Value<string>().To<decimal?>()
				?? balance["available_balance"]?.Value<string>().To<decimal?>();

			var serverTime = ParseUnixMs(balance["updated_at"] ?? balance["timestamp"]) ?? CurrentTime;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = asset.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, total, true)
			.TryAdd(PositionChangeTypes.BlockedValue, total is decimal t && available is decimal a ? (t - a).Max(0m) : null, true),
			cancellationToken);
		}

		if (!IsSpotSection)
		{
			var positions = await RestClient.GetPositionsAsync(null, cancellationToken);
			foreach (var position in ExtractObjects(positions, "results", "positions", "data", "list"))
			{
				var market = position["market"]?.Value<string>()?.ToUpperInvariant()
					?? position["symbol"]?.Value<string>()?.ToUpperInvariant();
				if (market.IsEmpty())
					continue;

				var size = position["size"]?.Value<string>().To<decimal?>()
					?? position["position_size"]?.Value<string>().To<decimal?>()
					?? position["qty"]?.Value<string>().To<decimal?>();

				var side = ParseSide(position["side"]?.Value<string>());
				if (side is null && size is decimal q)
					side = q < 0 ? Sides.Sell : (q > 0 ? Sides.Buy : null);

				var serverTime = ParseUnixMs(position["updated_at"] ?? position["timestamp"]) ?? CurrentTime;

				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = _portfolioName,
					SecurityId = market.ToStockSharp(BoardCode),
					ServerTime = serverTime,
					OriginalTransactionId = lookupMsg.TransactionId,
					Side = side,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, size, true)
				.TryAdd(PositionChangeTypes.AveragePrice, position["entry_price"]?.Value<string>().To<decimal?>(), true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, position["unrealized_pnl"]?.Value<string>().To<decimal?>(), true)
				.TryAdd(PositionChangeTypes.RealizedPnL, position["realized_pnl"]?.Value<string>().To<decimal?>(), true)
				.TryAdd(PositionChangeTypes.LiquidationPrice, position["liquidation_price"]?.Value<string>().To<decimal?>(), true),
				cancellationToken);
			}
		}

	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (!statusMsg.IsSubscribe)
			return;

		EnsurePrivateReady();

		var market = statusMsg.SecurityId.SecurityCode.IsEmpty() ? null : ResolveMarket(statusMsg.SecurityId);
		var orders = await RestClient.GetOrdersAsync(market, null, cancellationToken);
		var fills = await RestClient.GetFillsAsync(market, statusMsg.From, statusMsg.To, statusMsg.Count?.To<int?>(), null, cancellationToken);

		foreach (var order in ExtractObjects(orders, "results", "orders", "data", "list")
			.OrderBy(static o => o["created_at"]?.Value<long?>() ?? o["created_at"]?.Value<string>().To<long?>() ?? 0L))
		{
			var symbol = order["market"]?.Value<string>()?.ToUpperInvariant()
				?? order["symbol"]?.Value<string>()?.ToUpperInvariant();

			if (symbol.IsEmpty())
				continue;

			var serverTime = ParseUnixMs(order["created_at"] ?? order["updated_at"] ?? order["timestamp"]) ?? CurrentTime;
			if (statusMsg.From is DateTime from && serverTime < from)
				continue;
			if (statusMsg.To is DateTime to && serverTime > to)
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				PortfolioName = _portfolioName,
				Side = ParseSide(order["side"]?.Value<string>()) ?? Sides.Buy,
				OrderVolume = order["size"]?.Value<string>().To<decimal?>() ?? order["orig_size"]?.Value<string>().To<decimal?>(),
				Balance = order["remaining_size"]?.Value<string>().To<decimal?>() ?? order["remaining"]?.Value<string>().To<decimal?>(),
				OrderPrice = order["price"]?.Value<string>().To<decimal?>() ?? 0m,
				OrderType = (order["type"]?.Value<string>()).ToOrderType() ?? OrderTypes.Limit,
				OrderState = (order["status"]?.Value<string>()).ToOrderState(),
				Condition = TryParseCondition(order),
				TimeInForce = (order["time_in_force"]?.Value<string>() ?? order["tif"]?.Value<string>()).ToTimeInForce(),
				PostOnly = order["post_only"]?.Value<bool?>(),
				OrderId = ResolveOrderId(order),
				OrderStringId = ResolveOrderStringId(order),
				OriginalTransactionId = statusMsg.TransactionId,
			}, cancellationToken);
		}

		var left = statusMsg.Count ?? long.MaxValue;

		foreach (var fill in ExtractObjects(fills, "results", "fills", "data", "list")
			.OrderBy(static t => t["created_at"]?.Value<long?>() ?? t["created_at"]?.Value<string>().To<long?>() ?? 0L))
		{
			var symbol = fill["market"]?.Value<string>()?.ToUpperInvariant()
				?? fill["symbol"]?.Value<string>()?.ToUpperInvariant();
			if (symbol.IsEmpty())
				continue;

			var serverTime = ParseUnixMs(fill["created_at"] ?? fill["timestamp"]) ?? CurrentTime;
			if (statusMsg.From is DateTime from && serverTime < from)
				continue;
			if (statusMsg.To is DateTime to && serverTime > to)
				continue;

			var side = ParseSide(fill["side"]?.Value<string>());
			if (side is null)
				continue;

			var tradeIdString = fill["id"]?.Value<string>() ?? fill["trade_id"]?.Value<string>();

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				PortfolioName = _portfolioName,
				OrderId = ResolveOrderId(fill),
				OrderStringId = ResolveOrderStringId(fill),
				TradeId = TryParseLong(tradeIdString),
				TradePrice = fill["price"]?.Value<string>().To<decimal?>(),
				TradeVolume = fill["size"]?.Value<string>().To<decimal?>() ?? fill["quantity"]?.Value<string>().To<decimal?>(),
				Commission = fill["fee"]?.Value<string>().To<decimal?>(),
				CommissionCurrency = fill["fee_currency"]?.Value<string>()?.ToUpperInvariant() ?? fill["fee_asset"]?.Value<string>()?.ToUpperInvariant(),
				Side = side.Value,
				OriginalTransactionId = statusMsg.TransactionId,
			}, cancellationToken);

			if (--left <= 0)
				break;
		}

	}

	private async ValueTask EnsureMarketsAsync(CancellationToken cancellationToken)
	{
		if (_marketsBySymbol.Count > 0)
			return;

		var root = await RestClient.GetMarketsAsync(cancellationToken);
		var rows = ExtractObjects(root, "results", "markets", "data", "list").ToArray();

		foreach (var row in rows)
		{
			var symbol = row["symbol"]?.Value<string>()?.ToUpperInvariant();
			if (symbol.IsEmpty())
				continue;

			if (!IsMarketForSection(row))
				continue;

			if (IsSpotSection)
			{
				var model = row.ToObject<SpotMarket>();
				if (model?.Symbol.IsEmpty() != false)
					continue;
			}
			else
			{
				var model = row.ToObject<DerivativesMarket>();
				if (model?.Symbol.IsEmpty() != false)
					continue;
			}

			_marketsBySymbol[symbol] = row;
		}
	}

	private bool IsMarketForSection(JObject market)
	{
		var kind = market["asset_kind"]?.Value<string>()?.ToUpperInvariant();
		var symbol = market["symbol"]?.Value<string>()?.ToUpperInvariant();

		if (IsSpotSection)
		{
			if (!kind.IsEmpty())
				return kind.Contains("SPOT", StringComparison.Ordinal);

			return !symbol.IsEmpty()
				&& !symbol.Contains("PERP", StringComparison.Ordinal)
				&& !symbol.Contains("FUT", StringComparison.Ordinal);
		}

		if (!kind.IsEmpty())
		{
			if (kind.Contains("PERP", StringComparison.Ordinal) || kind.Contains("FUT", StringComparison.Ordinal) || kind.Contains("DERIV", StringComparison.Ordinal))
				return true;

			if (kind.Contains("SPOT", StringComparison.Ordinal))
				return false;
		}

		return !symbol.IsEmpty() && (symbol.Contains("PERP", StringComparison.Ordinal) || symbol.Contains("FUT", StringComparison.Ordinal));
	}

	private string ResolveMarket(SecurityId securityId)
	{
		var secCode = securityId.SecurityCode?.ToUpperInvariant();
		if (secCode.IsEmpty())
			throw new InvalidOperationException("Security code is empty.");

		if (_marketsBySymbol.TryGetValue(secCode, out _))
			return secCode;

		var normalized = secCode.Replace("/", "-", StringComparison.Ordinal);
		if (_marketsBySymbol.TryGetValue(normalized, out _))
			return normalized;

		var compact = secCode.Replace("-", string.Empty, StringComparison.Ordinal);
		foreach (var symbol in _marketsBySymbol.Keys)
		{
			if (symbol.Replace("-", string.Empty, StringComparison.Ordinal).EqualsIgnoreCase(compact))
				return symbol;
		}

		throw new InvalidOperationException($"Paradex market '{secCode}' is not found in {SectionName} cache.");
	}

	private async ValueTask PublishLevel1Async(string market, long transactionId, CancellationToken cancellationToken)
	{
		var summary = await RestClient.GetMarketSummaryAsync(market, cancellationToken);
		var stats = ExtractMarketSummary(summary, market);
		var bbo = await RestClient.GetBboAsync(market, cancellationToken);

		var last = stats?["last_traded_price"]?.Value<string>().To<decimal?>()
			?? stats?["mark_price"]?.Value<string>().To<decimal?>();

		var changeRate = stats?["price_change_rate_24h"]?.Value<string>().To<decimal?>();
		decimal? open = null;

		if (last is decimal l && changeRate is decimal c && c > -1m)
			open = l / (1m + c);

		var bestBid = bbo["bid"]?.Value<string>().To<decimal?>() ?? stats?["bid"]?.Value<string>().To<decimal?>();
		var bestAsk = bbo["ask"]?.Value<string>().To<decimal?>() ?? stats?["ask"]?.Value<string>().To<decimal?>();
		var serverTime = ParseUnixMs(bbo["last_updated_at"] ?? stats?["created_at"]) ?? CurrentTime;

		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(BoardCode),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, last)
		.TryAdd(Level1Fields.OpenPrice, open)
		.TryAdd(Level1Fields.BestBidPrice, bestBid)
		.TryAdd(Level1Fields.BestBidVolume, bbo["bid_size"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.BestAskPrice, bestAsk)
		.TryAdd(Level1Fields.BestAskVolume, bbo["ask_size"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.Volume, stats?["volume_24h"]?.Value<string>().To<decimal?>())
		.TryAdd(Level1Fields.Change, last is decimal lp && open is decimal op ? lp - op : null),
		cancellationToken);
	}

	private async ValueTask PublishDepthAsync(string market, int? maxDepth, long transactionId, CancellationToken cancellationToken)
	{
		var depth = await RestClient.GetOrderBookAsync(market, cancellationToken);
		var serverTime = ParseUnixMs(depth["last_updated_at"]) ?? CurrentTime;

		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(BoardCode),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = depth["seq_no"]?.Value<long?>() ?? depth["seq_no"]?.Value<string>().To<long?>() ?? 0L,
			Bids = ToQuotes(depth["bids"], maxDepth),
			Asks = ToQuotes(depth["asks"], maxDepth),
		}, cancellationToken);
	}

	private async ValueTask PollTicksAsync(TickState state, CancellationToken cancellationToken)
	{
		var rows = GetTradeRows(await RestClient.GetTradesAsync(state.Market, 200, cancellationToken));
		var lastTime = state.LastTime;
		var lastTradeId = state.LastTradeId;

		foreach (var trade in rows.OrderBy(static t => t["created_at"]?.Value<long?>() ?? t["created_at"]?.Value<string>().To<long?>() ?? 0L))
		{
			var tradeId = trade["id"]?.Value<string>();
			if (tradeId.IsEmpty())
				continue;

			if (!lastTradeId.IsEmpty() && CompareTradeIds(tradeId, lastTradeId) <= 0)
				continue;

			var serverTime = ParseUnixMs(trade["created_at"]) ?? CurrentTime;
			if (serverTime <= state.LastTime)
				continue;

			await SendOutMessageAsync(CreateTickMessage(state.Market, state.TransactionId, trade, tradeId, serverTime), cancellationToken);
			lastTradeId = tradeId;
			lastTime = serverTime;
		}

		using (_sync.EnterScope())
		{
			if (_ticksRealtime.TryGetValue(state.TransactionId, out var saved))
			{
				saved.LastTradeId = lastTradeId;
				saved.LastTime = lastTime;
			}
		}
	}

	private async ValueTask PollCandlesAsync(CandleState state, CancellationToken cancellationToken)
	{
		var from = state.LastOpenTime == default
			? DateTime.UtcNow - TimeSpan.FromHours(6)
			: state.LastOpenTime - state.TimeFrame;
		var to = DateTime.UtcNow;

		var rows = GetTradeRows(await RestClient.GetTradesAsync(state.Market, _maxTradesLimit, cancellationToken));
		var lastOpen = state.LastOpenTime;

		foreach (var candle in BuildCandles(state.Market, rows, state.TimeFrame, from, to))
		{
			if (candle.OpenTime <= state.LastOpenTime)
				continue;

			candle.OriginalTransactionId = state.TransactionId;
			await SendOutMessageAsync(candle, cancellationToken);
			lastOpen = candle.OpenTime;
		}

		using (_sync.EnterScope())
		{
			if (_candlesRealtime.TryGetValue(state.TransactionId, out var saved))
				saved.LastOpenTime = lastOpen;
		}
	}

	private ExecutionMessage CreateTickMessage(string market, long transactionId, JObject trade, string tradeId, DateTime serverTime)
	{
		return new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToStockSharp(BoardCode),
			ServerTime = serverTime,
			TradeId = TryParseLong(tradeId),
			TradePrice = trade["price"]?.Value<string>().To<decimal?>(),
			TradeVolume = trade["size"]?.Value<string>().To<decimal?>(),
			OriginSide = ParseSide(trade["side"]?.Value<string>()),
			OriginalTransactionId = transactionId,
		};
	}

	private IEnumerable<TimeFrameCandleMessage> BuildCandles(string market, IEnumerable<JObject> trades, TimeSpan timeFrame, DateTime from, DateTime to)
	{
		var candles = new SortedDictionary<DateTime, List<(decimal Price, decimal Volume)>>();

		foreach (var trade in trades)
		{
			var time = ParseUnixMs(trade["created_at"]) ?? CurrentTime;
			if (time < from || time > to)
				continue;

			var price = trade["price"]?.Value<string>().To<decimal?>();
			var volume = trade["size"]?.Value<string>().To<decimal?>();
			if (price is not decimal p || volume is not decimal v)
				continue;

			var open = FloorTime(time, timeFrame);
			if (!candles.TryGetValue(open, out var bucket))
			{
				bucket = [];
				candles[open] = bucket;
			}

			bucket.Add((p, v));
		}

		foreach (var pair in candles)
		{
			var prices = pair.Value.Select(static x => x.Price).ToArray();
			yield return new TimeFrameCandleMessage
			{
				SecurityId = market.ToStockSharp(BoardCode),
				TypedArg = timeFrame,
				OpenTime = pair.Key,
				OpenPrice = prices.FirstOrDefault(),
				HighPrice = prices.Length == 0 ? 0m : prices.Max(),
				LowPrice = prices.Length == 0 ? 0m : prices.Min(),
				ClosePrice = prices.LastOrDefault(),
				TotalVolume = pair.Value.Sum(static x => x.Volume),
				State = CandleStates.Finished,
			};
		}
	}

	private JObject BuildRegisterPayload(OrderRegisterMessage regMsg, string market)
	{
		var condition = regMsg.Condition as ParadexOrderCondition;
		var nativeType = ResolveNativeOrderType(regMsg.OrderType, regMsg.Price, condition, out var orderPrice, out var triggerPrice);

		var payload = new JObject
		{
			["market"] = market,
			["side"] = regMsg.Side.ToNative(),
			["size"] = regMsg.Volume.Abs().To<string>(),
			["type"] = nativeType,
			["client_id"] = ResolveClientId(regMsg.TransactionId, regMsg.UserOrderId, condition?.ClientId),
			["time_in_force"] = regMsg.TimeInForce.ToNative(),
			["reduce_only"] = regMsg.PositionEffect == OrderPositionEffects.CloseOnly || condition?.ReduceOnly == true,
		};

		if (orderPrice is decimal p)
			payload["price"] = p.To<string>();

		if (triggerPrice is decimal tp)
			payload["trigger_price"] = tp.To<string>();

		if (regMsg.PostOnly == true)
		{
			payload["post_only"] = true;
			payload["instruction"] = "POST_ONLY";
		}

		if (!condition?.PositionSide.IsEmpty() == true)
			payload["position_side"] = condition.PositionSide.ToUpperInvariant();

		return payload;
	}

	private static string ResolveNativeOrderType(OrderTypes? orderType, decimal price, ParadexOrderCondition condition, out decimal? orderPrice, out decimal? triggerPrice)
	{
		orderPrice = null;
		triggerPrice = null;

		switch (orderType ?? OrderTypes.Limit)
		{
			case OrderTypes.Limit:
				if (price <= 0)
					throw new InvalidOperationException("Limit order price must be positive.");

				orderPrice = price;

				if (condition?.ActivationPrice is decimal conditionalTrigger && conditionalTrigger > 0)
				{
					triggerPrice = conditionalTrigger;
					return condition.Type == ParadexOrderConditionTypes.TakeProfit ? "TAKE_PROFIT_LIMIT" : "STOP_LIMIT";
				}

				return "LIMIT";

			case OrderTypes.Market:
				if (condition?.ActivationPrice is decimal marketTrigger && marketTrigger > 0)
				{
					triggerPrice = marketTrigger;
					return condition.Type == ParadexOrderConditionTypes.TakeProfit ? "TAKE_PROFIT_MARKET" : "STOP_MARKET";
				}

				return "MARKET";

			case OrderTypes.Conditional:
			{
				triggerPrice = condition?.ActivationPrice;
				if (triggerPrice is null || triggerPrice <= 0)
					throw new InvalidOperationException("Conditional order requires positive activation price.");

				var marketConditional = condition?.IsMarket != false || condition?.ClosePositionPrice is null;
				if (!marketConditional)
				{
					orderPrice = condition.ClosePositionPrice;
					if (orderPrice is null || orderPrice <= 0)
						throw new InvalidOperationException("Conditional limit order requires positive close price.");
				}

				var isTp = condition?.Type == ParadexOrderConditionTypes.TakeProfit;
				return isTp
					? (marketConditional ? "TAKE_PROFIT_MARKET" : "TAKE_PROFIT_LIMIT")
					: (marketConditional ? "STOP_MARKET" : "STOP_LIMIT");
			}

			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		}
	}

	private static string ResolveClientId(long transactionId, string userOrderId, string conditionClientId)
	{
		if (!conditionClientId.IsEmpty())
			return conditionClientId;

		if (!userOrderId.IsEmpty())
			return userOrderId;

		return $"x-ss-{transactionId}";
	}

	private static long? ResolveOrderId(JObject row)
		=> TryParseLong(row["order_id"]?.Value<string>())
			?? TryParseLong(row["id"]?.Value<string>())
			?? row["order_id"]?.Value<long?>()
			?? row["id"]?.Value<long?>();

	private static string ResolveOrderStringId(JObject row)
		=> row["id"]?.Value<string>()
			?? row["order_id"]?.Value<string>()
			?? row["client_id"]?.Value<string>()
			?? row["client_order_id"]?.Value<string>();

	private static ParadexOrderCondition TryParseCondition(JObject row)
	{
		var trigger = row["trigger_price"]?.Value<string>().To<decimal?>()
			?? row["stop_price"]?.Value<string>().To<decimal?>();

		if (trigger is null)
			return null;

		var typeText = row["type"]?.Value<string>()?.ToUpperInvariant();
		var condition = new ParadexOrderCondition
		{
			ActivationPrice = trigger,
			ClosePositionPrice = row["price"]?.Value<string>().To<decimal?>(),
			ReduceOnly = row["reduce_only"]?.Value<bool?>() ?? false,
			PositionSide = row["position_side"]?.Value<string>(),
			ClientId = row["client_id"]?.Value<string>() ?? row["client_order_id"]?.Value<string>(),
			Type = typeText?.Contains("TAKE_PROFIT", StringComparison.Ordinal) == true
				? ParadexOrderConditionTypes.TakeProfit
				: ParadexOrderConditionTypes.StopLoss,
		};

		if (typeText?.Contains("MARKET", StringComparison.Ordinal) == true)
			condition.IsMarket = true;

		return condition;
	}

	private static JObject ExtractEntity(JObject response, params string[] keys)
	{
		if (response is null)
			return null;

		foreach (var key in keys)
		{
			if (response[key] is JObject obj)
				return obj;

			if (response[key] is JArray arr)
				return arr.OfType<JObject>().FirstOrDefault();
		}

		return response;
	}

	private static JObject ExtractMarketSummary(JObject response, string market)
	{
		foreach (var item in ExtractObjects(response, "results", "data", "list"))
		{
			var symbol = item["symbol"]?.Value<string>()?.ToUpperInvariant();
			if (symbol.EqualsIgnoreCase(market))
				return item;
		}

		return ExtractEntity(response, "result", "results", "data");
	}

	private static IEnumerable<JObject> GetTradeRows(JObject response)
		=> ExtractObjects(response, "results", "trades", "data", "list");

	private static IEnumerable<JObject> ExtractObjects(JToken token, params string[] keys)
	{
		if (token is null)
			return [];

		if (token is JArray arr)
			return arr.OfType<JObject>();

		if (token is not JObject obj)
			return [];

		foreach (var key in keys)
		{
			if (obj[key] is JArray keyed)
				return keyed.OfType<JObject>();
		}

		foreach (var key in keys)
		{
			if (obj[key] is JObject nested)
			{
				var nestedObjects = ExtractObjects(nested, keys).ToArray();
				if (nestedObjects.Length > 0)
					return nestedObjects;
			}
		}

		return [obj];
	}

	private static string ResolveAssetCode(JObject balance)
		=> balance["asset"]?.Value<string>()?.ToUpperInvariant()
			?? balance["currency"]?.Value<string>()?.ToUpperInvariant()
			?? balance["token"]?.Value<string>()?.ToUpperInvariant()
			?? balance["coin"]?.Value<string>()?.ToUpperInvariant();

	private static Sides? ParseSide(string value)
	{
		if (value.IsEmpty())
			return null;

		return value.ToUpperInvariant() switch
		{
			"BUY" or "BID" or "LONG" => Sides.Buy,
			"SELL" or "ASK" or "SHORT" => Sides.Sell,
			_ => null,
		};
	}

	private static bool IsStopType(string value)
	{
		var upper = value?.ToUpperInvariant();
		return upper?.Contains("STOP", StringComparison.Ordinal) == true
			|| upper?.Contains("TAKE_PROFIT", StringComparison.Ordinal) == true
			|| upper?.Contains("TRIGGER", StringComparison.Ordinal) == true;
	}

	private static QuoteChange[] ToQuotes(JToken entries, int? maxDepth)
	{
		if (entries is not JArray arr || arr.Count == 0)
			return [];

		var depth = 1.Max(maxDepth ?? int.MaxValue);
		var result = new List<QuoteChange>(depth.Min(arr.Count));

		foreach (var item in arr.Take(depth))
		{
			decimal? price = null;
			decimal? volume = null;

			if (item is JArray row && row.Count >= 2)
			{
				price = row[0]?.Value<string>().To<decimal?>() ?? row[0]?.Value<decimal?>();
				volume = row[1]?.Value<string>().To<decimal?>() ?? row[1]?.Value<decimal?>();
			}
			else if (item is JObject obj)
			{
				price = obj["price"]?.Value<string>().To<decimal?>() ?? obj["px"]?.Value<string>().To<decimal?>();
				volume = obj["size"]?.Value<string>().To<decimal?>() ?? obj["qty"]?.Value<string>().To<decimal?>();
			}

			if (price is decimal p && volume is decimal v)
				result.Add(new QuoteChange(p, v));
		}

		return [.. result];
	}

	private static DateTime FloorTime(DateTime value, TimeSpan step)
	{
		if (step <= TimeSpan.Zero)
			return value;

		var ticks = value.Ticks - value.Ticks % step.Ticks;
		return new DateTime(ticks, value.Kind);
	}

	private static DateTime? ParseUnixMs(JToken token)
	{
		var unix = token?.Value<long?>() ?? token?.Value<string>().To<long?>();
		return unix is long ms && ms > 0 ? ms.FromUnix(false) : null;
	}

	private static long? TryParseLong(string value)
		=> long.TryParse(value, out var result) ? result : null;

	private static int CompareTradeIds(string left, string right)
	{
		if (left.IsEmpty() || right.IsEmpty())
			return string.CompareOrdinal(left, right);

		if (left.All(char.IsDigit) && right.All(char.IsDigit))
		{
			if (left.Length != right.Length)
				return left.Length.CompareTo(right.Length);

			return string.CompareOrdinal(left, right);
		}

		return string.CompareOrdinal(left, right);
	}

	private void EnsureTradingReady(string tokenOverride = null)
	{
		if (!SupportsTrading)
			throw new NotSupportedException($"Paradex {SectionName} trading is unavailable for this section.");

		EnsurePrivateReady(tokenOverride);
	}

	private void EnsurePrivateReady(string tokenOverride = null)
	{
		if (!tokenOverride.IsEmpty())
			return;

		if (!Owner.Key.IsEmpty())
			return;

		if (!Owner.StarknetAccount.IsEmpty() && (!Owner.Secret.IsEmpty() || !Owner.StarknetPrivateKey.IsEmpty()))
			return;

		throw new InvalidOperationException("Paradex private operation requires bearer Key or Starknet account + signature material.");
	}

	private void RegisterLevel1(long transactionId, string market)
	{
		using (_sync.EnterScope())
			_level1Realtime[transactionId] = market;
	}

	private void UnregisterLevel1(long originalTransactionId)
	{
		if (originalTransactionId <= 0)
			return;

		using (_sync.EnterScope())
			_level1Realtime.Remove(originalTransactionId);
	}

	private void RegisterDepth(long transactionId, string market, int? depth)
	{
		using (_sync.EnterScope())
			_depthRealtime[transactionId] = (market, depth);
	}

	private void UnregisterDepth(long originalTransactionId)
	{
		if (originalTransactionId <= 0)
			return;

		using (_sync.EnterScope())
			_depthRealtime.Remove(originalTransactionId);
	}

	private void RegisterTicks(long transactionId, string market, DateTime lastTime, string lastTradeId)
	{
		using (_sync.EnterScope())
		{
			_ticksRealtime[transactionId] = new()
			{
				Market = market,
				TransactionId = transactionId,
				LastTime = lastTime,
				LastTradeId = lastTradeId,
			};
		}
	}

	private void UnregisterTicks(long originalTransactionId)
	{
		if (originalTransactionId <= 0)
			return;

		using (_sync.EnterScope())
			_ticksRealtime.Remove(originalTransactionId);
	}

	private void RegisterCandles(long transactionId, string market, TimeSpan timeFrame, DateTime lastOpenTime)
	{
		using (_sync.EnterScope())
		{
			_candlesRealtime[transactionId] = new()
			{
				Market = market,
				TransactionId = transactionId,
				TimeFrame = timeFrame,
				LastOpenTime = lastOpenTime,
			};
		}
	}

	private void UnregisterCandles(long originalTransactionId)
	{
		if (originalTransactionId <= 0)
			return;

		using (_sync.EnterScope())
			_candlesRealtime.Remove(originalTransactionId);
	}

	private void ClearRealtimeSubscriptions()
	{
		using (_sync.EnterScope())
		{
			_level1Realtime.Clear();
			_depthRealtime.Clear();
			_ticksRealtime.Clear();
			_candlesRealtime.Clear();
		}
	}
}
