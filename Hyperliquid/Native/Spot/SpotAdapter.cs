namespace StockSharp.Hyperliquid.Native.Spot;

using StockSharp.Hyperliquid.Native.Common.Model;
using StockSharp.Hyperliquid.Native.Spot.Model;

sealed class SpotAdapter(HyperliquidMessageAdapter owner) : BaseNativeAdapter(owner)
{
	private readonly Dictionary<string, AssetInfo> _spotAssetsByCoin = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, int> _spotAssetIndexByCoin = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, AssetCtx> _spotCtxByCoin = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, string> _spotCoinByAlias = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, string> _spotAliasByCoin = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, decimal> _spotVolumeStepByCoin = new(StringComparer.InvariantCultureIgnoreCase);

	public override HyperliquidSections Section => HyperliquidSections.Spot;

	protected override void SubscribeWsSection()
		=> WsClient.ActiveSpotAssetCtxReceived += OnActiveSpotAssetCtxAsync;

	protected override void UnsubscribeWsSection()
		=> WsClient.ActiveSpotAssetCtxReceived -= OnActiveSpotAssetCtxAsync;

	protected override void ClearSectionCaches()
	{
		_spotAssetsByCoin.Clear();
		_spotAssetIndexByCoin.Clear();
		_spotCtxByCoin.Clear();
		_spotCoinByAlias.Clear();
		_spotAliasByCoin.Clear();
		_spotVolumeStepByCoin.Clear();
	}

	protected override bool ShouldProcessOrderStatusCoin(string coin)
		=> IsSpotCoin(coin);

	protected override ValueTask SendSectionLevel1Async(string coin, long originTransId, CancellationToken cancellationToken)
	{
		if (!TryGetCachedSpotCtx(coin, out var ctx))
			return default;

		return SendSpotLevel1Async(coin, ctx, originTransId, cancellationToken);
	}

	protected override async ValueTask<decimal?> ResolveMarketReferenceAsync(string coin, CancellationToken cancellationToken)
	{
		if (!TryGetCachedSpotCtx(coin, out var spotCtx) || spotCtx is null)
		{
			await EnsureSpotMetaCacheAsync(cancellationToken);
			TryGetCachedSpotCtx(coin, out spotCtx);
		}

		return spotCtx?.MidPx.AsDecimal() ?? spotCtx?.MarkPx.AsDecimal();
	}

	public override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await EnsureSpotMetaCacheAsync(cancellationToken);

		List<AssetInfo> assets;

		using (Sync.EnterScope())
			assets = [.. _spotAssetsByCoin.Values];

		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var asset in assets)
		{
			var coin = asset.Name;
			var secMsg = new SecurityMessage
			{
				SecurityId = coin.ToStockSharp(),
				Name = GetSpotAlias(coin),
				SecurityType = SecurityTypes.CryptoCurrency,
				OriginalTransactionId = lookupMsg.TransactionId,
			};

			var volumeStep = GetSpotVolumeStep(coin);
			secMsg.VolumeStep = volumeStep;
			secMsg.MinVolume = volumeStep;

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
		await EnsureSpotMetaCacheAsync(cancellationToken);

		var coin = ResolveSpotCoin(mdMsg.SecurityId.ToSymbol());

		if (mdMsg.IsSubscribe)
		{
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
		await EnsureSpotMetaCacheAsync(cancellationToken);

		var coin = ResolveSpotCoin(mdMsg.SecurityId.ToSymbol());

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
		await EnsureSpotMetaCacheAsync(cancellationToken);

		var coin = ResolveSpotCoin(mdMsg.SecurityId.ToSymbol());

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
		await EnsureSpotMetaCacheAsync(cancellationToken);

		var coin = ResolveSpotCoin(mdMsg.SecurityId.ToSymbol());
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
		await EnsureSpotMetaCacheAsync(cancellationToken);
		EnsureSpotOrderSupported(regMsg);

		var coin = ResolveSpotCoin(regMsg.SecurityId.ToSymbol());
		var asset = GetSpotAssetIndex(coin);
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
			SecurityId = coin.ToStockSharp(),
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
			TimeInForce = regMsg.TimeInForce,
			PostOnly = regMsg.PostOnly,
		}, cancellationToken);
	}

	public override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		await EnsureSpotMetaCacheAsync(cancellationToken);
		EnsureSpotOrderSupported(replaceMsg);

		var coin = ResolveSpotCoin(replaceMsg.SecurityId.ToSymbol());
		var asset = GetSpotAssetIndex(coin);
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
			SecurityId = coin.ToStockSharp(),
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
			TimeInForce = replaceMsg.TimeInForce,
			PostOnly = replaceMsg.PostOnly,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		await EnsureSpotMetaCacheAsync(cancellationToken);

		if (cancelMsg.OrderId is null && cancelMsg.OrderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.TransactionId));

		var coin = await ResolveSpotCoinAsync(cancelMsg.SecurityId, cancelMsg.OrderId, cancelMsg.OrderStringId, cancellationToken);
		var asset = GetSpotAssetIndex(coin);

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
		await EnsureSpotMetaCacheAsync(cancellationToken);
		EnsureWalletAddress();

		if ((cancelMsg.Mode & OrderGroupCancelModes.ClosePositions) == OrderGroupCancelModes.ClosePositions)
			throw new NotSupportedException("Hyperliquid connector currently supports only order-group cancellation.");

		var coinFilter = cancelMsg.SecurityId.SecurityCode;

		if (!coinFilter.IsEmpty())
			coinFilter = ResolveSpotCoin(coinFilter);

		var sideFilter = cancelMsg.Side;
		var stopFilter = cancelMsg.IsStop;

		var openOrders = await InfoClient.GetOpenOrdersAsync(Owner.WalletAddress, cancellationToken);

		var targets = openOrders
			.Where(static order => order is not null)
			.Where(order => IsSpotCoin(order.Coin))
			.Where(order => !coinFilter.IsEmpty() ? order.Coin.EqualsIgnoreCase(coinFilter) : true)
			.Where(order => sideFilter is null || order.Side.ToSide() == sideFilter.Value)
			.Where(order => stopFilter is null || (order.IsTrigger == true) == stopFilter.Value)
			.Where(order => order.Oid > 0 && order.Coin.IsEmpty() == false)
			.ToArray();

		if (targets.Length == 0)
			return;

		var cancels = new JArray();

		foreach (var order in targets)
		{
			cancels.Add(new JObject
			{
				["a"] = GetSpotAssetIndex(order.Coin),
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
		await SendSpotPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);
	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		EnsureWalletAddress();
		await EnsureSpotMetaCacheAsync(cancellationToken);

		if (!statusMsg.IsSubscribe)
		{
			if (RemoveOrderStatusSubscription())
			{
				await WsClient.UnsubscribeOrderUpdates(Owner.WalletAddress, cancellationToken);
				await WsClient.UnsubscribeUserFills(Owner.WalletAddress, cancellationToken);
			}

			return;
		}

		await SendSpotOpenOrdersAsync(statusMsg, cancellationToken);
		await SendSpotUserFillsAsync(statusMsg, cancellationToken);

		if (!statusMsg.IsHistoryOnly())
		{
			if (AddOrderStatusSubscription(statusMsg.TransactionId))
			{
				await WsClient.SubscribeOrderUpdates(Owner.WalletAddress, cancellationToken);
				await WsClient.SubscribeUserFills(Owner.WalletAddress, cancellationToken);
			}
		}

	}

	private async ValueTask EnsureSpotMetaCacheAsync(CancellationToken cancellationToken)
	{
		using (Sync.EnterScope())
		{
			if (_spotAssetsByCoin.Count > 0)
				return;
		}

		var (meta, ctxs) = await InfoClient.GetSpotMetaAndAssetCtxsAsync(cancellationToken);

		using (Sync.EnterScope())
		{
			if (_spotAssetsByCoin.Count > 0)
				return;

			var tokensByIndex = (meta.Tokens ?? [])
				.Where(static token => token is not null)
				.GroupBy(static token => token.Index)
				.Select(static group => group.First())
				.ToDictionary(static token => token.Index, static token => token);

			for (var i = 0; i < (meta.Universe?.Length ?? 0); i++)
			{
				var asset = meta.Universe[i];

				if (asset?.Name.IsEmpty() != false)
					continue;

				_spotAssetsByCoin[asset.Name] = asset;
				_spotAssetIndexByCoin[asset.Name] = 10000 + asset.Index;
				_spotCoinByAlias[asset.Name] = asset.Name;
				_spotVolumeStepByCoin[asset.Name] = GetSpotVolumeStep(asset, tokensByIndex);

				var alias = BuildSpotAlias(asset, tokensByIndex);

				if (!alias.IsEmpty())
				{
					if (!_spotCoinByAlias.ContainsKey(alias))
						_spotCoinByAlias[alias] = asset.Name;

					_spotAliasByCoin[asset.Name] = alias;
				}
				else
				{
					_spotAliasByCoin[asset.Name] = asset.Name;
				}

				if (i < ctxs.Length && ctxs[i] is not null)
					_spotCtxByCoin[asset.Name] = ctxs[i];
			}

			foreach (var ctx in ctxs)
			{
				if (ctx?.Coin.IsEmpty() != false)
					continue;

				_spotCtxByCoin[ctx.Coin] = ctx;
			}
		}
	}

	private bool TryGetCachedSpotCtx(string coin, out AssetCtx ctx)
	{
		using (Sync.EnterScope())
			return _spotCtxByCoin.TryGetValue(coin, out ctx);
	}

	private string ResolveSpotCoin(string symbolOrCoin)
	{
		if (symbolOrCoin.IsEmpty())
			return symbolOrCoin;

		using (Sync.EnterScope())
		{
			if (_spotCoinByAlias.TryGetValue(symbolOrCoin, out var coin))
				return coin;
		}

		return symbolOrCoin;
	}

	private bool IsSpotCoin(string coin)
	{
		if (coin.IsEmpty())
			return false;

		using (Sync.EnterScope())
			return _spotAssetsByCoin.ContainsKey(coin);
	}

	private string GetSpotAlias(string coin)
	{
		using (Sync.EnterScope())
		{
			if (_spotAliasByCoin.TryGetValue(coin, out var alias))
				return alias;
		}

		return coin;
	}

	private decimal GetSpotVolumeStep(string coin)
	{
		using (Sync.EnterScope())
		{
			if (_spotVolumeStepByCoin.TryGetValue(coin, out var step))
				return step;
		}

		return 1m;
	}

	private int GetSpotAssetIndex(string coin)
	{
		using (Sync.EnterScope())
		{
			if (_spotAssetIndexByCoin.TryGetValue(coin, out var asset))
				return asset;
		}

		throw new InvalidOperationException($"Spot asset '{coin}' is not found in metadata.");
	}

	private async ValueTask<string> ResolveSpotCoinAsync(SecurityId secId, long? orderId, string orderStringId, CancellationToken cancellationToken)
	{
		if (!secId.SecurityCode.IsEmpty())
			return ResolveSpotCoin(secId.SecurityCode);

		EnsureWalletAddress();

		var orders = await InfoClient.GetOpenOrdersAsync(Owner.WalletAddress, cancellationToken);
		var normalizedCloid = NormalizeCloid(orderStringId);

		foreach (var order in orders)
		{
			if (!IsSpotCoin(order.Coin))
				continue;

			if (orderId is long oid && order.Oid == oid)
				return order.Coin;

			if (!normalizedCloid.IsEmpty() && normalizedCloid.EqualsIgnoreCase(order.Cloid))
				return order.Coin;
		}

		throw new InvalidOperationException("Cannot resolve spot security for order cancellation.");
	}

	private async ValueTask SendSpotPortfolioSnapshotAsync(long originTransId, CancellationToken cancellationToken)
	{
		var snapshot = await InfoClient.GetSpotClearinghouseStateAsync(Owner.WalletAddress, cancellationToken);
		var portfolioName = GetPortfolioName();
		var serverTime = snapshot.Time?.FromUnix(false) ?? CurrentTime;

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = portfolioName,
			BoardCode = Native.Extensions.BoardCode,
			OriginalTransactionId = originTransId,
		}, cancellationToken);

		foreach (var balance in snapshot.Balances ?? [])
		{
			if (balance?.Coin.IsEmpty() != false)
				continue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = portfolioName,
				SecurityId = balance.Coin.ToStockSharp(),
				ServerTime = serverTime,
				OriginalTransactionId = originTransId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Total.AsDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, balance.Hold.AsDecimal(), true),
			cancellationToken);
		}
	}

	private async ValueTask SendSpotOpenOrdersAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		var orders = await InfoClient.GetOpenOrdersAsync(Owner.WalletAddress, cancellationToken);
		var secCodeFilter = statusMsg.SecurityId.SecurityCode;

		if (!secCodeFilter.IsEmpty())
			secCodeFilter = ResolveSpotCoin(secCodeFilter);

		foreach (var order in orders.OrderBy(static o => o.Timestamp))
		{
			if (!IsSpotCoin(order.Coin))
				continue;

			if (!secCodeFilter.IsEmpty() && !order.Coin.EqualsIgnoreCase(secCodeFilter))
				continue;

			await ProcessOrderAsync(order, statusMsg.TransactionId, cancellationToken, positionEffect: null);
		}
	}

	private async ValueTask SendSpotUserFillsAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		var fills = await InfoClient.GetUserFillsAsync(Owner.WalletAddress, cancellationToken);
		var secCodeFilter = statusMsg.SecurityId.SecurityCode;

		if (!secCodeFilter.IsEmpty())
			secCodeFilter = ResolveSpotCoin(secCodeFilter);

		var from = statusMsg.From;
		var to = statusMsg.To;
		var left = statusMsg.Count ?? long.MaxValue;

		foreach (var fill in fills.OrderBy(static f => f.Time))
		{
			if (!IsSpotCoin(fill.Coin))
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

	private async ValueTask OnActiveSpotAssetCtxAsync(WsActiveAssetContext data, CancellationToken cancellationToken)
	{
		if (data?.Coin.IsEmpty() != false || data.Ctx is null)
			return;

		using (Sync.EnterScope())
			_spotCtxByCoin[data.Coin] = data.Ctx;

		var subscribed = false;

		using (Sync.EnterScope())
			subscribed = Level1Refs.ContainsKey(data.Coin);

		if (!subscribed)
			return;

		await SendSpotLevel1Async(data.Coin, data.Ctx, 0, cancellationToken);
	}

	private ValueTask SendSpotLevel1Async(string coin, AssetCtx ctx, long originTransId, CancellationToken cancellationToken)
	{
		if (coin.IsEmpty() || ctx is null)
			return default;

		var last = ctx.MidPx.AsDecimal() ?? ctx.MarkPx.AsDecimal();
		var prev = ctx.PrevDayPx.AsDecimal();

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = coin.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = originTransId,
		}
		.TryAdd(Level1Fields.LastTradePrice, last)
		.TryAdd(Level1Fields.OpenPrice, prev)
		.TryAdd(Level1Fields.Change, last is decimal l && prev is decimal p ? l - p : null)
		.TryAdd(Level1Fields.Volume, ctx.DayNtlVlm.AsDecimal()),
		cancellationToken);
	}

	private static string BuildSpotAlias(AssetInfo asset, IReadOnlyDictionary<int, TokenInfo> tokensByIndex)
	{
		if (asset?.Tokens is not { Length: >= 2 })
			return null;

		if (!tokensByIndex.TryGetValue(asset.Tokens[0], out var baseToken) || baseToken?.Name.IsEmpty() != false)
			return null;

		if (!tokensByIndex.TryGetValue(asset.Tokens[1], out var quoteToken) || quoteToken?.Name.IsEmpty() != false)
			return null;

		return $"{baseToken.Name}/{quoteToken.Name}";
	}

	private static decimal GetSpotVolumeStep(AssetInfo asset, IReadOnlyDictionary<int, TokenInfo> tokensByIndex)
	{
		if (asset?.Tokens is not { Length: > 0 })
			return 1m;

		return tokensByIndex.TryGetValue(asset.Tokens[0], out var baseToken)
			? baseToken.SzDecimals.GetVolumeStep()
			: 1m;
	}

	private static void EnsureSpotOrderSupported(OrderRegisterMessage orderMsg)
	{
		if (orderMsg.PositionEffect == OrderPositionEffects.CloseOnly)
			throw new NotSupportedException("CloseOnly is not supported for Hyperliquid spot orders.");
	}
}


