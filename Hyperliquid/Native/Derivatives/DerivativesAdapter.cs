namespace StockSharp.Hyperliquid.Native.Derivatives;

using StockSharp.Hyperliquid.Native.Derivatives.Model;
using StockSharp.Hyperliquid.Native.Common.Model;

sealed class DerivativesAdapter(HyperliquidMessageAdapter owner) : BaseNativeAdapter(owner)
{
	private readonly Dictionary<string, AssetInfo> _assetsByCoin = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, int> _assetIndexByCoin = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, AssetCtx> _ctxByCoin = new(StringComparer.InvariantCultureIgnoreCase);

	public override HyperliquidSections Section => HyperliquidSections.Derivatives;

	protected override void SubscribeWsSection()
		=> WsClient.ActiveAssetCtxReceived += OnActiveAssetCtxAsync;

	protected override void UnsubscribeWsSection()
		=> WsClient.ActiveAssetCtxReceived -= OnActiveAssetCtxAsync;

	protected override void ClearSectionCaches()
	{
		_assetsByCoin.Clear();
		_assetIndexByCoin.Clear();
		_ctxByCoin.Clear();
	}

	protected override bool ShouldProcessOrderStatusCoin(string coin)
	{
		if (coin.IsEmpty())
			return false;

		using (Sync.EnterScope())
		{
			if (_assetsByCoin.Count == 0)
				return true;

			return _assetsByCoin.ContainsKey(coin);
		}
	}

	protected override ValueTask SendSectionLevel1Async(string coin, long originTransId, CancellationToken cancellationToken)
	{
		if (!TryGetCachedCtx(coin, out var ctx))
			return default;

		return SendLevel1Async(coin, ctx, originTransId, cancellationToken);
	}

	protected override async ValueTask<decimal?> ResolveMarketReferenceAsync(string coin, CancellationToken cancellationToken)
	{
		if (!TryGetCachedCtx(coin, out var ctx) || ctx is null)
		{
			await EnsureMetaCacheAsync(cancellationToken);
			TryGetCachedCtx(coin, out ctx);
		}

		return ctx?.MidPx.AsDecimal() ?? ctx?.MarkPx.AsDecimal() ?? ctx?.OraclePx.AsDecimal();
	}

	public override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await EnsureMetaCacheAsync(cancellationToken);

		List<AssetInfo> assets;

		using (Sync.EnterScope())
			assets = [.. _assetsByCoin.Values];

		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var asset in assets)
		{
			var volumeStep = asset.SzDecimals.GetVolumeStep();

			var secMsg = new SecurityMessage
			{
				SecurityId = asset.Name.ToStockSharp(),
				Name = $"{asset.Name}-PERP",
				SecurityType = SecurityTypes.Future,
				OriginalTransactionId = lookupMsg.TransactionId,
				VolumeStep = volumeStep,
				MinVolume = volumeStep,
			};

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

		var coin = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await EnsureMetaCacheAsync(cancellationToken);

			bool needSubscribe;

			using (Sync.EnterScope())
				needSubscribe = AddRef(Level1Refs, coin);

			if (needSubscribe)
				await WsClient.SubscribeActiveAssetCtx(coin, cancellationToken);

			await SendSectionLevel1Async(coin, mdMsg.TransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			bool needUnsubscribe;

			using (Sync.EnterScope())
				needUnsubscribe = RemoveRef(Level1Refs, coin);

			if (needUnsubscribe)
				await WsClient.UnsubscribeActiveAssetCtx(coin, cancellationToken);
		}
	}

	public override async ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var coin = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is not null)
			{
				var from = mdMsg.From.Value;
				var to = mdMsg.To ?? DateTime.MaxValue;
				var left = mdMsg.Count ?? long.MaxValue;

				var history = await InfoClient.GetRecentTradesAsync(coin, cancellationToken);

				foreach (var trade in history.OrderBy(static t => t.Time))
				{
					var time = trade.Time.FromUnix(false);

					if (time < from)
						continue;

					if (time > to)
						break;

					await ProcessTradeAsync(trade, mdMsg.TransactionId, cancellationToken);

					if (--left <= 0)
						break;
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				bool needSubscribe;

				using (Sync.EnterScope())
					needSubscribe = AddRef(TicksRefs, coin);

				if (needSubscribe)
					await WsClient.SubscribeTrades(coin, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			bool needUnsubscribe;

			using (Sync.EnterScope())
				needUnsubscribe = RemoveRef(TicksRefs, coin);

			if (needUnsubscribe)
				await WsClient.UnsubscribeTrades(coin, cancellationToken);
		}
	}

	public override async ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var coin = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			var snapshot = await InfoClient.GetL2BookAsync(coin, cancellationToken);
			await ProcessL2BookAsync(snapshot, mdMsg.TransactionId, cancellationToken);

			if (!mdMsg.IsHistoryOnly())
			{
				bool needSubscribe;

				using (Sync.EnterScope())
				{
					needSubscribe = AddRef(BookRefs, coin);
					BookDepths[coin] = 1.Max(mdMsg.MaxDepth ?? int.MaxValue);
				}

				if (needSubscribe)
					await WsClient.SubscribeL2Book(coin, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			bool needUnsubscribe;

			using (Sync.EnterScope())
			{
				needUnsubscribe = RemoveRef(BookRefs, coin);
				BookDepths.Remove(coin);
			}

			if (needUnsubscribe)
				await WsClient.UnsubscribeL2Book(coin, cancellationToken);
		}
	}

	public override async ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var coin = mdMsg.SecurityId.ToSymbol();
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToNative();

		if (mdMsg.IsSubscribe)
		{
			var shouldLoadHistory = mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly();

			if (shouldLoadHistory)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var from = mdMsg.From ?? (to - TimeSpan.FromDays(1));
				var left = mdMsg.Count ?? long.MaxValue;

				var candles = await InfoClient.GetCandleSnapshotAsync(coin, interval, from, to, cancellationToken);

				foreach (var candle in candles.OrderBy(static c => c.T))
				{
					var openTime = candle.T.FromUnix(false);

					if (openTime < from)
						continue;

					if (openTime > to)
						break;

					await ProcessCandleAsync(candle, mdMsg.TransactionId, CandleStates.Finished, cancellationToken);

					if (--left <= 0)
						break;
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				if (AddCandleSubscription(coin, interval, mdMsg.TransactionId))
					await WsClient.SubscribeCandles(coin, interval, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);

			if (mdMsg.IsHistoryOnly())
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		}
		else
		{
			if (RemoveCandleSubscription(coin, interval))
				await WsClient.UnsubscribeCandles(coin, interval, cancellationToken);
		}
	}

	public override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		await EnsureMetaCacheAsync(cancellationToken);

		var coin = regMsg.SecurityId.ToSymbol();
		var asset = GetAssetIndex(coin);
		var cloid = GetOrCreateCloid(regMsg.UserOrderId, regMsg.TransactionId);

		RememberTransactionByCloid(cloid, regMsg.TransactionId);

		var order = await CreateOrderWireAsync(regMsg, coin, asset, cloid, cancellationToken);
		var action = new JObject
		{
			["type"] = "order",
			["orders"] = new JArray(order.Token),
			["grouping"] = "na",
		};

		var statuses = await SendSignedActionAsync(action, cancellationToken);
		var (orderId, orderState) = ParseSingleOrderStatus(statuses[0], regMsg.TransactionId);

		if (orderId is long oid)
			RememberTransactionByOrderId(oid, regMsg.TransactionId);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = regMsg.SecurityId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderId = orderId,
			OrderStringId = cloid,
			OrderVolume = regMsg.Volume.Abs(),
			Balance = orderState == OrderStates.Done ? 0 : regMsg.Volume.Abs(),
			Side = regMsg.Side,
			OrderPrice = order.OrderPrice,
			PortfolioName = GetPortfolioName(),
			OrderState = orderState,
			OrderType = regMsg.OrderType ?? OrderTypes.Limit,
			PositionEffect = regMsg.PositionEffect,
			TimeInForce = regMsg.TimeInForce,
			PostOnly = regMsg.PostOnly,
		}, cancellationToken);
	}

	public override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		await EnsureMetaCacheAsync(cancellationToken);

		var coin = replaceMsg.SecurityId.ToSymbol();
		var asset = GetAssetIndex(coin);
		var cloid = GetOrCreateCloid(replaceMsg.UserOrderId, replaceMsg.TransactionId);
		var oldId = ResolveModifyOrderIdentifier(replaceMsg);

		RememberTransactionByCloid(cloid, replaceMsg.TransactionId);

		var order = await CreateOrderWireAsync(replaceMsg, coin, asset, cloid, cancellationToken);

		var action = new JObject
		{
			["type"] = "batchModify",
			["modifies"] = new JArray
			{
				new JObject
				{
					["oid"] = oldId,
					["order"] = order.Token,
				}
			},
		};

		var statuses = await SendSignedActionAsync(action, cancellationToken);
		var (orderId, orderState) = ParseSingleOrderStatus(statuses[0], replaceMsg.TransactionId);

		if (orderId is long oid)
			RememberTransactionByOrderId(oid, replaceMsg.TransactionId);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = replaceMsg.SecurityId,
			ServerTime = CurrentTime,
			OriginalTransactionId = replaceMsg.TransactionId,
			OrderId = orderId,
			OrderStringId = cloid,
			OrderVolume = replaceMsg.Volume.Abs(),
			Balance = orderState == OrderStates.Done ? 0 : replaceMsg.Volume.Abs(),
			Side = replaceMsg.Side,
			OrderPrice = order.OrderPrice,
			PortfolioName = GetPortfolioName(),
			OrderState = orderState,
			OrderType = replaceMsg.OrderType ?? OrderTypes.Limit,
			PositionEffect = replaceMsg.PositionEffect,
			TimeInForce = replaceMsg.TimeInForce,
			PostOnly = replaceMsg.PostOnly,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		await EnsureMetaCacheAsync(cancellationToken);

		if (cancelMsg.OrderId is null && cancelMsg.OrderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.TransactionId));

		var coin = await ResolveCoinAsync(cancelMsg.SecurityId, cancelMsg.OrderId, cancelMsg.OrderStringId, cancellationToken);
		var asset = GetAssetIndex(coin);

		JObject action;
		long? orderId = cancelMsg.OrderId;
		string orderStringId = cancelMsg.OrderStringId;

		if (orderId is null && TryParseLong(orderStringId, out var parsedOrderId))
			orderId = parsedOrderId;

		if (orderId is not null)
		{
			action = new JObject
			{
				["type"] = "cancel",
				["cancels"] = new JArray
				{
					new JObject
					{
						["a"] = asset,
						["o"] = orderId.Value,
					}
				},
			};
		}
		else
		{
			var cloid = NormalizeCloid(orderStringId);

			if (cloid.IsEmpty())
				throw new InvalidOperationException($"OrderStringId '{orderStringId}' is not a valid Hyperliquid cloid.");

			orderStringId = cloid;
			action = new JObject
			{
				["type"] = "cancelByCloid",
				["cancels"] = new JArray
				{
					new JObject
					{
						["asset"] = asset,
						["cloid"] = cloid,
					}
				},
			};
		}

		var statuses = await SendSignedActionAsync(action, cancellationToken);
		EnsureCancelSuccess(statuses[0], cancelMsg.TransactionId);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = coin.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = cancelMsg.TransactionId,
			OrderId = orderId,
			OrderStringId = orderStringId,
			OrderState = OrderStates.Done,
			Balance = 0,
			PortfolioName = GetPortfolioName(),
		}, cancellationToken);
	}

	public override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		await EnsureMetaCacheAsync(cancellationToken);
		EnsureWalletAddress();

		if ((cancelMsg.Mode & OrderGroupCancelModes.ClosePositions) == OrderGroupCancelModes.ClosePositions)
			throw new NotSupportedException("Hyperliquid connector currently supports only order-group cancellation.");

		var coinFilter = cancelMsg.SecurityId.SecurityCode;
		var sideFilter = cancelMsg.Side;
		var stopFilter = cancelMsg.IsStop;

		var openOrders = await InfoClient.GetOpenOrdersAsync(Owner.WalletAddress, cancellationToken);

		var targets = openOrders
			.Where(static order => order is not null)
			.Where(order => !coinFilter.IsEmpty() ? order.Coin.EqualsIgnoreCase(coinFilter) : true)
			.Where(order => sideFilter is null || order.Side.ToSide() == sideFilter.Value)
			.Where(order => stopFilter is null || (order.IsTrigger == true) == stopFilter.Value)
			.Where(order => order.Oid > 0 && order.Coin.IsEmpty() == false)
			.Where(order => IsPerpCoin(order.Coin))
			.ToArray();

		if (targets.Length == 0)
			return;

		var cancels = new JArray();

		foreach (var order in targets)
		{
			cancels.Add(new JObject
			{
				["a"] = GetAssetIndex(order.Coin),
				["o"] = order.Oid,
			});
		}

		var action = new JObject
		{
			["type"] = "cancel",
			["cancels"] = cancels,
		};

		var statuses = await SendSignedActionAsync(action, cancellationToken);
		Exception firstError = null;

		for (var i = 0; i < targets.Length; i++)
		{
			var status = i < statuses.Count ? statuses[i] : null;
			var error = GetCancelError(status);

			if (!error.IsEmpty())
			{
				firstError ??= new InvalidOperationException(error);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					SecurityId = targets[i].Coin.ToStockSharp(),
					ServerTime = CurrentTime,
					OriginalTransactionId = cancelMsg.TransactionId,
					OrderId = targets[i].Oid,
					OrderStringId = targets[i].Cloid,
					PortfolioName = GetPortfolioName(),
					Error = new InvalidOperationException(error),
				}, cancellationToken);

				continue;
			}

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = targets[i].Coin.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId = cancelMsg.TransactionId,
				OrderId = targets[i].Oid,
				OrderStringId = targets[i].Cloid,
				OrderState = OrderStates.Done,
				Balance = 0,
				PortfolioName = GetPortfolioName(),
			}, cancellationToken);
		}

		if (firstError is not null)
			throw firstError;
	}

	public override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (!lookupMsg.IsSubscribe)
			return;

		EnsureWalletAddress();
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);
	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		EnsureWalletAddress();
		await EnsureMetaCacheAsync(cancellationToken);

		if (!statusMsg.IsSubscribe)
		{
			if (RemoveOrderStatusSubscription())
			{
				await WsClient.UnsubscribeOrderUpdates(Owner.WalletAddress, cancellationToken);
				await WsClient.UnsubscribeUserFills(Owner.WalletAddress, cancellationToken);
			}

			return;
		}

		await SendOpenOrdersAsync(statusMsg, cancellationToken);
		await SendUserFillsAsync(statusMsg, cancellationToken);

		if (!statusMsg.IsHistoryOnly())
		{
			if (AddOrderStatusSubscription(statusMsg.TransactionId))
			{
				await WsClient.SubscribeOrderUpdates(Owner.WalletAddress, cancellationToken);
				await WsClient.SubscribeUserFills(Owner.WalletAddress, cancellationToken);
			}
		}

	}

	private async ValueTask EnsureMetaCacheAsync(CancellationToken cancellationToken)
	{
		using (Sync.EnterScope())
		{
			if (_assetsByCoin.Count > 0)
				return;
		}

		var (meta, ctxs) = await InfoClient.GetMetaAndAssetCtxsAsync(cancellationToken);

		using (Sync.EnterScope())
		{
			if (_assetsByCoin.Count > 0)
				return;

			for (var i = 0; i < meta.Universe.Length; i++)
			{
				var asset = meta.Universe[i];

				if (asset?.Name.IsEmpty() != false)
					continue;

				_assetsByCoin[asset.Name] = asset;
				_assetIndexByCoin[asset.Name] = i;

				if (i < ctxs.Length && ctxs[i] is not null)
					_ctxByCoin[asset.Name] = ctxs[i];
			}
		}
	}

	private bool TryGetCachedCtx(string coin, out AssetCtx ctx)
	{
		using (Sync.EnterScope())
			return _ctxByCoin.TryGetValue(coin, out ctx);
	}

	private void UpdateCachedCtx(string coin, AssetCtx ctx)
	{
		using (Sync.EnterScope())
			_ctxByCoin[coin] = ctx;
	}

	private bool IsPerpCoin(string coin)
	{
		if (coin.IsEmpty())
			return false;

		using (Sync.EnterScope())
			return _assetsByCoin.ContainsKey(coin);
	}

	private int GetAssetIndex(string coin)
	{
		using (Sync.EnterScope())
		{
			if (_assetIndexByCoin.TryGetValue(coin, out var asset))
				return asset;
		}

		throw new InvalidOperationException($"Asset '{coin}' is not found in metadata.");
	}

	private async ValueTask<string> ResolveCoinAsync(SecurityId secId, long? orderId, string orderStringId, CancellationToken cancellationToken)
	{
		if (!secId.SecurityCode.IsEmpty())
			return secId.SecurityCode;

		EnsureWalletAddress();

		var orders = await InfoClient.GetOpenOrdersAsync(Owner.WalletAddress, cancellationToken);
		var normalizedCloid = NormalizeCloid(orderStringId);

		foreach (var order in orders)
		{
			if (!IsPerpCoin(order.Coin))
				continue;

			if (orderId is long oid && order.Oid == oid)
				return order.Coin;

			if (!normalizedCloid.IsEmpty() && normalizedCloid.EqualsIgnoreCase(order.Cloid))
				return order.Coin;
		}

		throw new InvalidOperationException("Cannot resolve security for order cancellation.");
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originTransId, CancellationToken cancellationToken)
	{
		var snapshot = await InfoClient.GetClearinghouseStateAsync(Owner.WalletAddress, cancellationToken);
		var portfolioName = GetPortfolioName();
		var serverTime = snapshot.Time?.FromUnix(false) ?? CurrentTime;

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = portfolioName,
			BoardCode = Native.Extensions.BoardCode,
			OriginalTransactionId = originTransId,
		}, cancellationToken);

		var accountValue = snapshot.CrossMarginSummary?.AccountValue.AsDecimal();
		var withdrawable = snapshot.Withdrawable.AsDecimal();

		if (accountValue is not null || withdrawable is not null)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = portfolioName,
				SecurityId = "USDC".ToStockSharp(),
				ServerTime = serverTime,
				OriginalTransactionId = originTransId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, accountValue, true)
			.TryAdd(PositionChangeTypes.BlockedValue, accountValue is decimal av && withdrawable is decimal wd ? (av - wd).Max(0m) : null, true),
			cancellationToken);
		}

		foreach (var assetPosition in snapshot.AssetPositions ?? [])
		{
			var position = assetPosition?.Position;

			if (position?.Coin.IsEmpty() != false)
				continue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = portfolioName,
				SecurityId = position.Coin.ToStockSharp(),
				ServerTime = serverTime,
				OriginalTransactionId = originTransId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.Szi.AsDecimal(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.EntryPx.AsDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnl.AsDecimal(), true)
			.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPx.AsDecimal(), true)
			.TryAdd(PositionChangeTypes.VariationMargin, position.MarginUsed.AsDecimal(), true)
			.TryAdd(PositionChangeTypes.Leverage, position.Leverage?.Value, true),
			cancellationToken);
		}
	}

	private async ValueTask SendOpenOrdersAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		var orders = await InfoClient.GetOpenOrdersAsync(Owner.WalletAddress, cancellationToken);
		var secCodeFilter = statusMsg.SecurityId.SecurityCode;

		foreach (var order in orders.OrderBy(static o => o.Timestamp))
		{
			if (!IsPerpCoin(order.Coin))
				continue;

			if (!secCodeFilter.IsEmpty() && !order.Coin.EqualsIgnoreCase(secCodeFilter))
				continue;

			await ProcessOrderAsync(order, statusMsg.TransactionId, cancellationToken);
		}
	}

	private async ValueTask SendUserFillsAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		var fills = await InfoClient.GetUserFillsAsync(Owner.WalletAddress, cancellationToken);
		var secCodeFilter = statusMsg.SecurityId.SecurityCode;
		var from = statusMsg.From;
		var to = statusMsg.To;
		var left = statusMsg.Count ?? long.MaxValue;

		foreach (var fill in fills.OrderBy(static f => f.Time))
		{
			if (!IsPerpCoin(fill.Coin))
				continue;

			if (!secCodeFilter.IsEmpty() && !fill.Coin.EqualsIgnoreCase(secCodeFilter))
				continue;

			var time = fill.Time.FromUnix(false);

			if (from is not null && time < from.Value)
				continue;

			if (to is not null && time > to.Value)
				break;

			await ProcessFillAsync(fill, statusMsg.TransactionId, cancellationToken);

			if (--left <= 0)
				break;
		}
	}

	private async ValueTask OnActiveAssetCtxAsync(WsActiveAssetContext data, CancellationToken cancellationToken)
	{
		if (data?.Coin.IsEmpty() != false || data.Ctx is null)
			return;

		UpdateCachedCtx(data.Coin, data.Ctx);

		var subscribed = false;

		using (Sync.EnterScope())
			subscribed = Level1Refs.ContainsKey(data.Coin);

		if (!subscribed)
			return;

		await SendLevel1Async(data.Coin, data.Ctx, 0, cancellationToken);
	}

	private ValueTask SendLevel1Async(string coin, AssetCtx ctx, long originTransId, CancellationToken cancellationToken)
	{
		if (coin.IsEmpty() || ctx is null)
			return default;

		var last = ctx.MidPx.AsDecimal() ?? ctx.MarkPx.AsDecimal();
		var prev = ctx.PrevDayPx.AsDecimal();
		var impactBid = ctx.ImpactPxs is { Length: > 0 } ? ctx.ImpactPxs[0].AsDecimal() : null;
		var impactAsk = ctx.ImpactPxs is { Length: > 1 } ? ctx.ImpactPxs[1].AsDecimal() : null;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = coin.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = originTransId,
		}
		.TryAdd(Level1Fields.LastTradePrice, last)
		.TryAdd(Level1Fields.OpenPrice, prev)
		.TryAdd(Level1Fields.Change, last is decimal l && prev is decimal p ? l - p : null)
		.TryAdd(Level1Fields.OpenInterest, ctx.OpenInterest.AsDecimal())
		.TryAdd(Level1Fields.BestBidPrice, impactBid)
		.TryAdd(Level1Fields.BestAskPrice, impactAsk)
		.TryAdd(Level1Fields.Volume, ctx.DayBaseVlm.AsDecimal())
		.TryAdd(Level1Fields.HighPrice, ctx.MarkPx.AsDecimal())
		.TryAdd(Level1Fields.LowPrice, ctx.OraclePx.AsDecimal()),
		cancellationToken);
	}
}


