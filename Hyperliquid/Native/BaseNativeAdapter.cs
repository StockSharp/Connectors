namespace StockSharp.Hyperliquid.Native;

using System.Globalization;

using StockSharp.Hyperliquid.Native.Common.Model;

abstract class BaseNativeAdapter(HyperliquidMessageAdapter owner) : INativeAdapter
{
	protected HyperliquidMessageAdapter Owner { get; } = owner ?? throw new ArgumentNullException(nameof(owner));
	public abstract HyperliquidSections Section { get; }

	protected InfoClient InfoClient { get; private set; }
	protected WsClient WsClient { get; private set; }
	protected ExchangeClient ExchangeClient { get; private set; }
	protected HyperliquidSigner Signer { get; private set; }

	private string _portfolioName;
	private long _lastNonce;
	private int _orderStatusSubscriptions;
	private long _orderStatusSubscriptionId;

	protected readonly Lock Sync = new();
	protected readonly Dictionary<string, int> Level1Refs = new(StringComparer.InvariantCultureIgnoreCase);
	protected readonly Dictionary<string, int> TicksRefs = new(StringComparer.InvariantCultureIgnoreCase);
	protected readonly Dictionary<string, int> BookRefs = new(StringComparer.InvariantCultureIgnoreCase);
	protected readonly Dictionary<string, int> BookDepths = new(StringComparer.InvariantCultureIgnoreCase);
	protected readonly Dictionary<(string Coin, string Interval), long> CandleTransIds = [];
	protected readonly Dictionary<string, long> TransIdByCloid = new(StringComparer.InvariantCultureIgnoreCase);
	protected readonly Dictionary<long, long> TransIdByOrderId = [];

	public virtual async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (InfoClient is not null || WsClient is not null || ExchangeClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		InfoClient = new(Owner.InfoEndpoint) { Parent = Owner };
		WsClient = new(Owner.WsEndpoint, Owner.GetReconnectAttempts(), Owner.ReConnectionSettings.WorkingTime) { Parent = Owner };
		ExchangeClient = new(Owner.ExchangeEndpoint) { Parent = Owner };
		Signer = Owner.CreateSignerFromNative();

		if (Signer is not null && Owner.WalletAddress.IsEmpty())
			Owner.WalletAddress = !Owner.VaultAddress.IsEmpty() ? NormalizeAddress(Owner.VaultAddress) : Signer.Address;

		_portfolioName = $"{nameof(Hyperliquid)}_{Owner.WalletAddress ?? "Public"}";

		SubscribeWsCommon();
		SubscribeWsSection();

		await WsClient.ConnectAsync(cancellationToken);
	}

	public virtual ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (InfoClient is null || WsClient is null || ExchangeClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		WsClient.Disconnect();
		UnsubscribeWsSection();
		UnsubscribeWsCommon();
		WsClient.Dispose();
		WsClient = null;

		InfoClient.Dispose();
		InfoClient = null;

		ExchangeClient.Dispose();
		ExchangeClient = null;
		Signer = null;

		ClearCaches();

		return default;
	}

	public virtual async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (WsClient is not null)
		{
			try
			{
				UnsubscribeWsSection();
				UnsubscribeWsCommon();
				WsClient.Disconnect();
				WsClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			WsClient = null;
		}

		if (InfoClient is not null)
		{
			try
			{
				InfoClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			InfoClient = null;
		}

		if (ExchangeClient is not null)
		{
			try
			{
				ExchangeClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			ExchangeClient = null;
		}

		Signer = null;
		ClearCaches();
	}

	public ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> WsClient?.PingAsync(cancellationToken) ?? default;

	public abstract ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken);
	public abstract ValueTask Level1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken);
	public abstract ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken);
	public abstract ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken);
	public abstract ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken);
	public abstract ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken);
	public abstract ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken);

	protected virtual void SubscribeWsSection()
	{
	}

	protected virtual void UnsubscribeWsSection()
	{
	}

	protected virtual bool ShouldProcessOrderStatusCoin(string coin)
		=> !coin.IsEmpty();

	protected virtual ValueTask OnOrderUpdateAsync(OpenOrder order, long subscriptionId, CancellationToken cancellationToken)
		=> ProcessOrderAsync(order, subscriptionId, cancellationToken);

	protected virtual ValueTask OnUserFillAsync(UserFill fill, long subscriptionId, CancellationToken cancellationToken)
		=> ProcessFillAsync(fill, subscriptionId, cancellationToken);

	protected abstract ValueTask<decimal?> ResolveMarketReferenceAsync(string coin, CancellationToken cancellationToken);
	protected abstract void ClearSectionCaches();
	protected abstract ValueTask SendSectionLevel1Async(string coin, long originTransId, CancellationToken cancellationToken);

	protected ValueTask SendOutMessageAsync(Message message, CancellationToken cancellationToken)
		=> Owner.SendOutFromNativeAsync(message, Section, cancellationToken);

	protected ValueTask SendOutErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Owner.SendOutErrorFromNativeAsync(error, cancellationToken);

	protected ValueTask SendOutConnectionStateAsync(ConnectionStates state, CancellationToken cancellationToken)
		=> Owner.SendConnectionStateFromNativeAsync(state, cancellationToken);

	protected ValueTask SendSubscriptionReplyAsync(long transactionId, CancellationToken cancellationToken, Exception error = null)
		=> Owner.SendSubscriptionReplyFromNativeAsync(transactionId, cancellationToken, error);

	protected ValueTask SendSubscriptionResultAsync(ISubscriptionMessage message, CancellationToken cancellationToken)
		=> Owner.SendSubscriptionResultFromNativeAsync(message, cancellationToken);

	protected ValueTask SendSubscriptionFinishedAsync(long transactionId, CancellationToken cancellationToken)
		=> Owner.SendSubscriptionFinishedFromNativeAsync(transactionId, cancellationToken);

	protected DateTime CurrentTime => Owner.GetServerTime();

	protected string GetPortfolioName()
		=> _portfolioName.IsEmpty() ? $"{nameof(Hyperliquid)}_{Owner.WalletAddress ?? "Public"}" : _portfolioName;

	protected void EnsureWalletAddress()
	{
		if (Owner.WalletAddress.IsEmpty() && Signer is not null)
			Owner.WalletAddress = !Owner.VaultAddress.IsEmpty() ? NormalizeAddress(Owner.VaultAddress) : Signer.Address;

		if (Owner.WalletAddress.IsEmpty())
			throw new InvalidOperationException("Wallet address is not specified.");
	}

	protected void EnsureTradingReady()
	{
		if (ExchangeClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		Signer ??= Owner.CreateSignerFromNative();

		if (Signer is null)
			throw new InvalidOperationException("Private key is not specified.");

		if (Owner.WalletAddress.IsEmpty())
			Owner.WalletAddress = !Owner.VaultAddress.IsEmpty() ? NormalizeAddress(Owner.VaultAddress) : Signer.Address;
	}

	protected async ValueTask<JArray> SendSignedActionAsync(JObject action, CancellationToken cancellationToken)
	{
		var nonce = CreateNonce();
		var signature = Signer.SignL1Action(action, NormalizeAddress(Owner.VaultAddress), nonce, Owner.ExpiresAfter);
		var response = await ExchangeClient.PostActionAsync(action, signature, nonce, NormalizeAddress(Owner.VaultAddress), Owner.ExpiresAfter, cancellationToken);

		var status = response["status"]?.Value<string>();

		if (!"ok".EqualsIgnoreCase(status))
		{
			var error = response["response"]?.ToString(Formatting.None) ?? response.ToString(Formatting.None);
			throw new InvalidOperationException(error);
		}

		var statuses = response["response"]?["data"]?["statuses"] as JArray;

		if (statuses is null)
			throw new InvalidOperationException($"Unexpected exchange response: {response.ToString(Formatting.None)}");

		return statuses;
	}

	protected async ValueTask<(JObject Token, decimal OrderPrice)> CreateOrderWireAsync(OrderRegisterMessage orderMsg, string coin, int asset, string cloid, CancellationToken cancellationToken)
	{
		var volume = orderMsg.Volume.Abs();

		if (volume <= 0m)
			throw new InvalidOperationException("Order volume must be positive.");

		var orderType = orderMsg.OrderType ?? OrderTypes.Limit;
		var reduceOnly = orderMsg.PositionEffect == OrderPositionEffects.CloseOnly;
		var limitPrice = orderMsg.Price;
		JObject type;

		switch (orderType)
		{
			case OrderTypes.Limit:
			{
				if (limitPrice <= 0m)
					throw new InvalidOperationException("Limit order price must be positive.");

				type = new JObject
				{
					["limit"] = new JObject
					{
						["tif"] = GetNativeTif(orderMsg, isMarketOrder: false),
					},
				};
				break;
			}
			case OrderTypes.Market:
			{
				limitPrice = await ResolveMarketPriceAsync(orderMsg, coin, cancellationToken);
				type = new JObject
				{
					["limit"] = new JObject
					{
						["tif"] = "Ioc",
					},
				};
				break;
			}
			case OrderTypes.Conditional:
			{
				var trigger = ResolveTrigger(orderMsg, out var triggerPx, out var isMarket, out var closePrice);
				limitPrice = closePrice ?? (orderMsg.Price > 0m ? orderMsg.Price : triggerPx);

				if (limitPrice <= 0m)
					limitPrice = triggerPx;

				type = new JObject
				{
					["trigger"] = new JObject
					{
						["isMarket"] = isMarket,
						["triggerPx"] = HyperliquidSigner.ToWire(triggerPx),
						["tpsl"] = trigger,
					},
				};

				break;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderMsg.OrderType, orderMsg.TransactionId));
		}

		var wire = new JObject
		{
			["a"] = asset,
			["b"] = orderMsg.Side == Sides.Buy,
			["p"] = HyperliquidSigner.ToWire(limitPrice),
			["s"] = HyperliquidSigner.ToWire(volume),
			["r"] = reduceOnly,
			["t"] = type,
			["c"] = cloid,
		};

		return (wire, limitPrice);
	}

	protected static string ResolveTrigger(OrderRegisterMessage orderMsg, out decimal triggerPx, out bool isMarket, out decimal? closePrice)
	{
		triggerPx = 0m;
		isMarket = false;
		closePrice = null;

		if (orderMsg.Condition is HyperliquidOrderCondition hlCondition)
		{
			if (hlCondition.IsTrailing)
				throw new NotSupportedException("Hyperliquid does not support trailing triggers.");

			triggerPx = hlCondition.ActivationPrice ?? orderMsg.Price;
			closePrice = hlCondition.ClosePositionPrice;
			isMarket = hlCondition.IsMarket || !hlCondition.ClosePositionPrice.HasValue;

			if (triggerPx <= 0m)
				throw new InvalidOperationException("Conditional order trigger price must be positive.");

			return hlCondition.Type == HyperliquidOrderConditionTypes.TakeProfit ? "tp" : "sl";
		}

		if (orderMsg.Condition is ITakeProfitOrderCondition takeProfit)
		{
			triggerPx = takeProfit.ActivationPrice ?? orderMsg.Price;
			closePrice = takeProfit.ClosePositionPrice;
			isMarket = !takeProfit.ClosePositionPrice.HasValue;
			return "tp";
		}

		if (orderMsg.Condition is IStopLossOrderCondition stopLoss)
		{
			if (stopLoss.IsTrailing)
				throw new NotSupportedException("Hyperliquid does not support trailing triggers.");

			triggerPx = stopLoss.ActivationPrice ?? orderMsg.Price;
			closePrice = stopLoss.ClosePositionPrice;
			isMarket = !stopLoss.ClosePositionPrice.HasValue;
			return "sl";
		}

		triggerPx = orderMsg.Price;
		isMarket = false;

		if (triggerPx <= 0m)
			throw new InvalidOperationException("Conditional order trigger price must be positive.");

		return "sl";
	}

	protected async ValueTask<decimal> ResolveMarketPriceAsync(OrderRegisterMessage orderMsg, string coin, CancellationToken cancellationToken)
	{
		if (orderMsg.Price > 0m)
			return orderMsg.Price;

		var reference = await ResolveMarketReferenceAsync(coin, cancellationToken);

		if (reference is not decimal refPx || refPx <= 0m)
			throw new InvalidOperationException($"Cannot derive market reference price for '{coin}'. Specify price explicitly.");

		var slippage = Math.Clamp(orderMsg.Slippage ?? Owner.MarketOrderSlippage, 0m, 0.95m);
		var signedSlippage = orderMsg.Side == Sides.Buy ? 1m + slippage : 1m - slippage;
		var price = refPx * signedSlippage;

		if (price <= 0m)
			throw new InvalidOperationException("Calculated market limit price is non-positive.");

		return price;
	}

	protected string GetNativeTif(OrderRegisterMessage orderMsg, bool isMarketOrder)
	{
		if (isMarketOrder)
			return "Ioc";

		if (orderMsg.PostOnly == true || orderMsg.IsMarketMaker == true)
			return "Alo";

		if (orderMsg.TimeInForce == TimeInForce.MatchOrCancel)
			Owner.AddWarningFromNative("Hyperliquid does not support FOK natively; IOC is used.");

		return orderMsg.TimeInForce switch
		{
			null or TimeInForce.PutInQueue => "Gtc",
			TimeInForce.CancelBalance => "Ioc",
			TimeInForce.MatchOrCancel => "Ioc",
			_ => "Gtc",
		};
	}

	protected static JToken ResolveModifyOrderIdentifier(OrderReplaceMessage replaceMsg)
	{
		if (replaceMsg.OldOrderId is long oldId)
			return oldId;

		if (TryParseLong(replaceMsg.OldOrderStringId, out var parsedOrderId))
			return parsedOrderId;

		var oldCloid = NormalizeCloid(replaceMsg.OldOrderStringId);

		if (!oldCloid.IsEmpty())
			return oldCloid;

		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.TransactionId));
	}

	protected static (long? OrderId, OrderStates State) ParseSingleOrderStatus(JToken statusToken, long transactionId)
	{
		if (statusToken is null)
			throw new InvalidOperationException("Exchange returned empty order status.");

		var obj = statusToken as JObject;

		if (obj is null)
			throw new InvalidOperationException($"Unexpected order status format: {statusToken}.");

		if (obj["error"]?.Value<string>() is { Length: > 0 } error)
			throw new InvalidOperationException(error);

		var resting = obj["resting"] as JObject;

		if (resting is not null)
			return (resting["oid"]?.Value<long?>(), OrderStates.Active);

		var filled = obj["filled"] as JObject;

		if (filled is not null)
			return (filled["oid"]?.Value<long?>(), OrderStates.Done);

		throw new InvalidOperationException($"Unknown order status format for transaction {transactionId}: {obj.ToString(Formatting.None)}");
	}

	protected static void EnsureCancelSuccess(JToken statusToken, long transactionId)
	{
		var error = GetCancelError(statusToken);

		if (!error.IsEmpty())
			throw new InvalidOperationException(error + $" (transaction: {transactionId})");
	}

	protected static string GetCancelError(JToken statusToken)
	{
		if (statusToken is null)
			return "Cancel response status is missing.";

		switch (statusToken.Type)
		{
			case JTokenType.String:
			{
				var value = statusToken.Value<string>();
				return value.EqualsIgnoreCase("success") ? null : value;
			}
			case JTokenType.Object:
			{
				var obj = (JObject)statusToken;
				return obj["error"]?.Value<string>();
			}
			default:
				return $"Unexpected cancel response status: {statusToken}";
		}
	}

	protected long ResolveTransactionId(long orderId, string cloid)
	{
		using (Sync.EnterScope())
		{
			if (!cloid.IsEmpty() && TransIdByCloid.TryGetValue(cloid, out var transIdByCloid))
			{
				if (orderId > 0)
					TransIdByOrderId[orderId] = transIdByCloid;

				return transIdByCloid;
			}

			if (orderId > 0 && TransIdByOrderId.TryGetValue(orderId, out var transIdByOrderId))
				return transIdByOrderId;
		}

		if (TryParseCloidTransactionId(cloid, out var parsed))
			return parsed;

		return 0;
	}

	protected void RememberTransactionByCloid(string cloid, long transactionId)
	{
		if (cloid.IsEmpty() || transactionId <= 0)
			return;

		using (Sync.EnterScope())
			TransIdByCloid[cloid] = transactionId;
	}

	protected void RememberTransactionByOrderId(long orderId, long transactionId)
	{
		if (orderId <= 0 || transactionId <= 0)
			return;

		using (Sync.EnterScope())
			TransIdByOrderId[orderId] = transactionId;
	}

	protected static string GetOrCreateCloid(string userOrderId, long transactionId)
	{
		var normalized = NormalizeCloid(userOrderId);

		return !normalized.IsEmpty()
			? normalized
			: $"0x{transactionId:x32}";
	}

	protected static string NormalizeCloid(string value)
	{
		if (value.IsEmpty())
			return null;

		var cloid = value.Trim();

		if (!cloid.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			return null;

		var hex = cloid[2..];

		if (hex.Length != 32 || !hex.All(Uri.IsHexDigit))
			return null;

		return "0x" + hex.ToLowerInvariant();
	}

	protected static bool TryParseCloidTransactionId(string cloid, out long transactionId)
	{
		transactionId = 0;

		if (cloid.IsEmpty())
			return false;

		var normalized = NormalizeCloid(cloid);

		if (!normalized.IsEmpty())
		{
			var hex = normalized[2..];

			if (ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw) && raw <= long.MaxValue)
			{
				transactionId = (long)raw;
				return true;
			}

			return false;
		}

		return TryParseLong(cloid, out transactionId);
	}

	protected static bool TryParseLong(string value, out long result)
	{
		result = 0;
		return !value.IsEmpty() && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
	}

	protected static string NormalizeAddress(string value)
		=> value.IsEmpty() ? null : value.Trim().ToLowerInvariant();

	protected bool AddOrderStatusSubscription(long transactionId)
	{
		using (Sync.EnterScope())
		{
			_orderStatusSubscriptions++;
			_orderStatusSubscriptionId = transactionId;
			return _orderStatusSubscriptions == 1;
		}
	}

	protected bool RemoveOrderStatusSubscription()
	{
		using (Sync.EnterScope())
		{
			if (_orderStatusSubscriptions <= 0)
				return false;

			_orderStatusSubscriptions--;

			if (_orderStatusSubscriptions != 0)
				return false;

			_orderStatusSubscriptionId = 0;
			return true;
		}
	}

	protected long GetOrderStatusSubscriptionId()
	{
		using (Sync.EnterScope())
			return _orderStatusSubscriptionId;
	}

	protected static bool AddRef(Dictionary<string, int> refs, string key)
	{
		if (refs.TryGetValue(key, out var count))
		{
			refs[key] = count + 1;
			return false;
		}

		refs.Add(key, 1);
		return true;
	}

	protected static bool RemoveRef(Dictionary<string, int> refs, string key)
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

	protected bool AddCandleSubscription(string coin, string interval, long transId)
	{
		using (Sync.EnterScope())
		{
			var key = (coin, interval);
			var isFirst = !CandleTransIds.ContainsKey(key);
			CandleTransIds[key] = transId;
			return isFirst;
		}
	}

	protected bool RemoveCandleSubscription(string coin, string interval)
	{
		using (Sync.EnterScope())
			return CandleTransIds.Remove((coin, interval));
	}

	protected bool TryGetCandleTransactionId(string coin, string interval, out long transId)
	{
		using (Sync.EnterScope())
			return CandleTransIds.TryGetValue((coin, interval), out transId);
	}

	protected ValueTask ProcessTradeAsync(WsTrade trade, long originTransId, CancellationToken cancellationToken)
	{
		if (trade?.Coin.IsEmpty() != false)
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Coin.ToStockSharp(),
			ServerTime = trade.Time.FromUnix(false),
			TradeId = trade.Tid,
			TradePrice = trade.Px.AsDecimal(),
			TradeVolume = trade.Sz.AsDecimal()?.Abs(),
			OriginSide = trade.Side.ToSideOrNull(),
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	protected ValueTask ProcessL2BookAsync(L2BookSnapshot snapshot, long originTransId, CancellationToken cancellationToken)
	{
		if (snapshot?.Coin.IsEmpty() != false)
			return default;

		var maxDepth = int.MaxValue;

		using (Sync.EnterScope())
		{
			if (BookDepths.TryGetValue(snapshot.Coin, out var depth))
				maxDepth = depth;
		}

		var bids = snapshot.Levels is { Length: > 0 }
			? snapshot.Levels[0]
				.Take(maxDepth)
				.Select(static l => new QuoteChange(l.Px.AsDecimal() ?? 0m, l.Sz.AsDecimal() ?? 0m))
				.ToArray()
			: [];

		var asks = snapshot.Levels is { Length: > 1 }
			? snapshot.Levels[1]
				.Take(maxDepth)
				.Select(static l => new QuoteChange(l.Px.AsDecimal() ?? 0m, l.Sz.AsDecimal() ?? 0m))
				.ToArray()
			: [];

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = snapshot.Coin.ToStockSharp(),
			ServerTime = snapshot.Time.FromUnix(false),
			Bids = bids,
			Asks = asks,
			State = QuoteChangeStates.SnapshotComplete,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	protected ValueTask ProcessCandleAsync(WsCandle candle, long originTransId, CandleStates state, CancellationToken cancellationToken)
	{
		if (candle?.S.IsEmpty() != false || candle.I.IsEmpty())
			return default;

		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = candle.S.ToStockSharp(),
			TypedArg = candle.I.ToTimeFrame(),
			OpenTime = candle.T.FromUnix(false),
			OpenPrice = candle.O.AsDecimal() ?? 0,
			ClosePrice = candle.C.AsDecimal() ?? 0,
			HighPrice = candle.H.AsDecimal() ?? 0,
			LowPrice = candle.L.AsDecimal() ?? 0,
			TotalVolume = candle.V.AsDecimal() ?? 0,
			State = state,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	protected ValueTask ProcessOrderAsync(OpenOrder order, long originTransId, CancellationToken cancellationToken, OrderPositionEffects? positionEffect = null)
	{
		if (order?.Coin.IsEmpty() != false)
			return default;

		var transId = ResolveTransactionId(order.Oid, order.Cloid);

		if (transId > 0)
		{
			RememberTransactionByOrderId(order.Oid, transId);

			if (!order.Cloid.IsEmpty())
				RememberTransactionByCloid(order.Cloid, transId);
		}

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Coin.ToStockSharp(),
			ServerTime = order.Timestamp.FromUnix(false),
			TransactionId = transId,
			OriginalTransactionId = originTransId,
			OrderId = order.Oid,
			OrderStringId = order.Cloid,
			OrderVolume = order.OrigSz.AsDecimal() ?? order.Sz.AsDecimal(),
			Balance = order.Sz.AsDecimal()?.Abs(),
			Side = order.Side.ToSide(),
			OrderPrice = order.LimitPx.AsDecimal() ?? 0m,
			PortfolioName = GetPortfolioName(),
			OrderState = order.Status.ToOrderState(),
			OrderType = order.IsTrigger == true ? OrderTypes.Conditional : OrderTypes.Limit,
			PositionEffect = positionEffect ?? (order.ReduceOnly == true ? OrderPositionEffects.CloseOnly : null),
		}, cancellationToken);
	}

	protected ValueTask ProcessFillAsync(UserFill fill, long originTransId, CancellationToken cancellationToken)
	{
		if (fill?.Coin.IsEmpty() != false)
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Coin.ToStockSharp(),
			ServerTime = fill.Time.FromUnix(false),
			OriginalTransactionId = originTransId,
			OrderId = fill.Oid,
			TradeId = fill.Tid,
			TradePrice = fill.Px.AsDecimal(),
			TradeVolume = fill.Sz.AsDecimal()?.Abs(),
			OriginSide = fill.Side.ToSideOrNull(),
			PortfolioName = GetPortfolioName(),
			Commission = fill.Fee.AsDecimal(),
			CommissionCurrency = fill.FeeToken,
		}, cancellationToken);
	}

	protected long CreateNonce()
	{
		var nonce = (long)DateTime.UtcNow.ToUnix(false);

		using (Sync.EnterScope())
		{
			if (nonce <= _lastNonce)
				nonce = _lastNonce + 1;

			_lastNonce = nonce;
			return nonce;
		}
	}

	private void ClearCaches()
	{
		using (Sync.EnterScope())
		{
			Level1Refs.Clear();
			TicksRefs.Clear();
			BookRefs.Clear();
			BookDepths.Clear();
			CandleTransIds.Clear();
			TransIdByCloid.Clear();
			TransIdByOrderId.Clear();
			_lastNonce = 0;
			_orderStatusSubscriptions = 0;
			_orderStatusSubscriptionId = 0;
			ClearSectionCaches();
		}
	}

	private void SubscribeWsCommon()
	{
		WsClient.StateChanged += OnWsStateChangedAsync;
		WsClient.Error += OnWsErrorAsync;
		WsClient.TradesReceived += OnTradesAsync;
		WsClient.L2BookReceived += OnL2BookAsync;
		WsClient.CandleReceived += OnCandleAsync;
		WsClient.OrderUpdatesReceived += OnOrderUpdatesAsync;
		WsClient.UserFillsReceived += OnUserFillsAsync;
	}

	private void UnsubscribeWsCommon()
	{
		WsClient.StateChanged -= OnWsStateChangedAsync;
		WsClient.Error -= OnWsErrorAsync;
		WsClient.TradesReceived -= OnTradesAsync;
		WsClient.L2BookReceived -= OnL2BookAsync;
		WsClient.CandleReceived -= OnCandleAsync;
		WsClient.OrderUpdatesReceived -= OnOrderUpdatesAsync;
		WsClient.UserFillsReceived -= OnUserFillsAsync;
	}

	private async ValueTask OnWsStateChangedAsync(ConnectionStates state, CancellationToken cancellationToken)
	{
		await SendOutConnectionStateAsync(state, cancellationToken);

		if (state != ConnectionStates.Connected)
			return;

		await ResubscribeMarketDataAsync(cancellationToken);
	}

	private ValueTask OnWsErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask ResubscribeMarketDataAsync(CancellationToken cancellationToken)
	{
		List<string> level1Symbols;
		List<string> tickSymbols;
		List<string> bookSymbols;
		List<(string Coin, string Interval)> candleKeys;
		var orderStatusSubscriptions = 0;

		using (Sync.EnterScope())
		{
			level1Symbols = [.. Level1Refs.Keys];
			tickSymbols = [.. TicksRefs.Keys];
			bookSymbols = [.. BookRefs.Keys];
			candleKeys = [.. CandleTransIds.Keys];
			orderStatusSubscriptions = _orderStatusSubscriptions;
		}

		foreach (var coin in level1Symbols)
			await WsClient.SubscribeActiveAssetCtx(coin, cancellationToken);

		foreach (var coin in tickSymbols)
			await WsClient.SubscribeTrades(coin, cancellationToken);

		foreach (var coin in bookSymbols)
			await WsClient.SubscribeL2Book(coin, cancellationToken);

		foreach (var (coin, interval) in candleKeys)
			await WsClient.SubscribeCandles(coin, interval, cancellationToken);

		if (orderStatusSubscriptions > 0 && !Owner.WalletAddress.IsEmpty())
		{
			await WsClient.SubscribeOrderUpdates(Owner.WalletAddress, cancellationToken);
			await WsClient.SubscribeUserFills(Owner.WalletAddress, cancellationToken);
		}
	}

	private async ValueTask OnTradesAsync(WsTrade[] trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			var subscribed = false;

			using (Sync.EnterScope())
				subscribed = !trade.Coin.IsEmpty() && TicksRefs.ContainsKey(trade.Coin);

			if (!subscribed)
				continue;

			await ProcessTradeAsync(trade, 0, cancellationToken);
		}
	}

	private async ValueTask OnL2BookAsync(L2BookSnapshot snapshot, CancellationToken cancellationToken)
	{
		var subscribed = false;

		using (Sync.EnterScope())
			subscribed = snapshot?.Coin.IsEmpty() == false && BookRefs.ContainsKey(snapshot.Coin);

		if (!subscribed)
			return;

		await ProcessL2BookAsync(snapshot, 0, cancellationToken);
	}

	private async ValueTask OnCandleAsync(WsCandle candle, CancellationToken cancellationToken)
	{
		if (candle?.S.IsEmpty() != false || candle.I.IsEmpty())
			return;

		if (!TryGetCandleTransactionId(candle.S, candle.I, out var transId))
			return;

		var state = candle.TClose.FromUnix(false) <= DateTime.UtcNow ? CandleStates.Finished : CandleStates.Active;

		await ProcessCandleAsync(candle, transId, state, cancellationToken);
	}

	private async ValueTask OnOrderUpdatesAsync(OpenOrder[] updates, CancellationToken cancellationToken)
	{
		var subscriptionId = GetOrderStatusSubscriptionId();

		if (subscriptionId == 0)
			return;

		foreach (var order in updates)
		{
			if (!ShouldProcessOrderStatusCoin(order?.Coin))
				continue;

			await OnOrderUpdateAsync(order, subscriptionId, cancellationToken);
		}
	}

	private async ValueTask OnUserFillsAsync(UserFill[] fills, CancellationToken cancellationToken)
	{
		var subscriptionId = GetOrderStatusSubscriptionId();

		if (subscriptionId == 0)
			return;

		foreach (var fill in fills)
		{
			if (!ShouldProcessOrderStatusCoin(fill?.Coin))
				continue;

			await OnUserFillAsync(fill, subscriptionId, cancellationToken);
		}
	}
}

