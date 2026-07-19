namespace StockSharp.NDAX;

public partial class NDAXMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var instrument = GetInstrument(regMsg.SecurityId);
        var stockSharpType = regMsg.OrderType ?? OrderTypes.Limit;
        var condition = regMsg.Condition as NDAXOrderCondition ?? new();
        var ndaxType = stockSharpType switch
        {
            OrderTypes.Market => NdaxOrderTypes.Market,
            OrderTypes.Limit => NdaxOrderTypes.Limit,
            OrderTypes.Conditional when regMsg.Price > 0 =>
                NdaxOrderTypes.StopLimit,
            OrderTypes.Conditional => NdaxOrderTypes.StopMarket,
            _ => throw new NotSupportedException(
                LocalizedStrings.OrderUnsupportedType.Put(stockSharpType,
                    regMsg.TransactionId)),
        };
        var volume = regMsg.Volume.Abs();
        if (volume <= 0)
            throw new InvalidOperationException(
                "NDAX order volume must be positive.");
        if (ndaxType is NdaxOrderTypes.Limit or NdaxOrderTypes.StopLimit &&
            regMsg.Price <= 0)
            throw new InvalidOperationException(
                "An NDAX limit order requires a positive price.");
        if (stockSharpType == OrderTypes.Conditional &&
            condition.TriggerPrice is not > 0)
            throw new InvalidOperationException(
                "An NDAX stop order requires a positive trigger price.");
        if (regMsg.PostOnly == true)
            throw new NotSupportedException(
                "NDAX does not document post-only through SendOrder.");
        if (regMsg.TillDate is not null)
            throw new NotSupportedException(
                "NDAX GTD orders are not exposed by this connector.");

        var clientOrderId = regMsg.TransactionId;
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            ClientOrderId = clientOrderId,
            InstrumentId = instrument.InstrumentId,
            Side = regMsg.Side,
            OrderType = stockSharpType,
            Volume = volume,
            Price = regMsg.Price,
            Condition = condition.Clone() as NDAXOrderCondition,
            State = OrderStates.Active,
        };
        TrackOrder(tracked);

        var response = await SocketClient.SendOrderAsync(new()
        {
            InstrumentId = instrument.InstrumentId,
            OmsId = OmsId,
            AccountId = EffectiveAccountId,
            TimeInForce = regMsg.TimeInForce.ToNdax(),
            ClientOrderId = clientOrderId,
            OcoOrderId = condition.OcoOrderId ?? 0,
            IsDisplayQuantityUsed = regMsg.VisibleVolume is > 0 &&
                regMsg.VisibleVolume < volume,
            DisplayQuantity = regMsg.VisibleVolume is > 0 &&
                regMsg.VisibleVolume < volume
                    ? regMsg.VisibleVolume
                    : null,
            Side = regMsg.Side.ToNdax(),
            Quantity = volume,
            OrderType = ndaxType,
            PegPriceType = 0,
            LimitPrice = ndaxType is NdaxOrderTypes.Limit or
                NdaxOrderTypes.StopLimit ? regMsg.Price : null,
            StopPrice = stockSharpType == OrderTypes.Conditional
                ? condition.TriggerPrice
                : null,
        }, cancellationToken);
        if (!response.Status.EqualsIgnoreCase("Accepted") ||
            response.OrderId <= 0)
        {
            tracked.State = OrderStates.Failed;
            throw new NdaxApiException("SendOrder", null,
                response.ErrorMessage ?? response.Status ??
                    "order was rejected");
        }
        tracked.OrderId = response.OrderId;
        TrackOrder(tracked);
        await SendTrackedOrderAsync(tracked, regMsg.TransactionId,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var orderId = cancelMsg.OrderId ?? 0;
        var clientOrderId = ParseIdentifier(cancelMsg.OrderStringId);
        var tracked = GetTrackedOrder(orderId, clientOrderId,
            cancelMsg.OriginalTransactionId);
        if (orderId <= 0)
            orderId = tracked?.OrderId ?? 0;
        if (clientOrderId <= 0)
            clientOrderId = tracked?.ClientOrderId ?? 0;
        if (orderId <= 0 && clientOrderId <= 0)
            throw new InvalidOperationException(
                "NDAX cancellation requires an order ID or client order ID.");
        await SocketClient.CancelOrderAsync(new()
        {
            OmsId = OmsId,
            AccountId = EffectiveAccountId,
            OrderId = orderId,
            ClientOrderId = clientOrderId,
        }, cancellationToken);
        if (tracked is not null)
        {
            tracked.State = OrderStates.Done;
            await SendTrackedOrderAsync(tracked, cancelMsg.TransactionId,
                cancellationToken, 0m);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
            throw new NotSupportedException(
                "NDAX spot cancellation cannot close positions.");
        var instrumentId = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? (int?)null
            : GetInstrument(cancelMsg.SecurityId).InstrumentId;
        if (instrumentId is null && cancelMsg.Side is null)
        {
            await SocketClient.CancelAllOrdersAsync(EffectiveAccountId,
                cancellationToken);
            TrackedOrder[] trackedOrders;
            using (_sync.EnterScope())
                trackedOrders = [.. _ordersByClientId.Values.Distinct()
                    .Where(static order => order.State != OrderStates.Done)];
            foreach (var tracked in trackedOrders)
            {
                tracked.State = OrderStates.Done;
                await SendTrackedOrderAsync(tracked,
                    cancelMsg.TransactionId, cancellationToken, 0m);
            }
            return;
        }

        var orders = await SocketClient.GetOpenOrdersAsync(EffectiveAccountId,
            cancellationToken);
        foreach (var order in (orders ?? []).Where(order => order is not null &&
            (instrumentId is null || order.InstrumentId == instrumentId) &&
            (cancelMsg.Side is null || order.Side.ToSide() ==
                cancelMsg.Side)))
            await SocketClient.CancelOrderAsync(new()
            {
                OmsId = OmsId,
                AccountId = EffectiveAccountId,
                OrderId = order.OrderId,
                ClientOrderId = order.ClientOrderId,
            }, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(
        PortfolioLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
            cancellationToken);
        EnsurePrivateReady();
        if (!lookupMsg.IsSubscribe)
        {
            var removed = false;
            using (_sync.EnterScope())
            {
                removed = _portfolioSubscriptions.Remove(
                    lookupMsg.OriginalTransactionId);
                RemoveFingerprintPrefix(_positionFingerprints,
                    lookupMsg.OriginalTransactionId);
            }
            if (removed)
                await ReleaseStreamAsync(AccountStreamKey,
                    cancellationToken);
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = PortfolioName,
            BoardCode = BoardCodes.NDAX,
            OriginalTransactionId = lookupMsg.TransactionId,
        }, cancellationToken);
        var positions = await SocketClient.GetPositionsAsync(
            EffectiveAccountId, cancellationToken);
        foreach (var position in positions ?? [])
            await SendPositionAsync(position, lookupMsg.TransactionId, true,
                cancellationToken);
        if (lookupMsg.IsHistoryOnly())
        {
            await CompletePortfolioAsync(lookupMsg, cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _portfolioSubscriptions.Add(lookupMsg.TransactionId);
        try
        {
            await AcquireStreamAsync(AccountStreamKey, cancellationToken);
            await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _portfolioSubscriptions.Remove(lookupMsg.TransactionId);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OrderStatusAsync(
        OrderStatusMessage statusMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(statusMsg.TransactionId,
            cancellationToken);
        EnsurePrivateReady();
        if (!statusMsg.IsSubscribe)
        {
            await RemoveOrderSubscriptionAsync(
                statusMsg.OriginalTransactionId, cancellationToken);
            return;
        }
        if (statusMsg.Count is <= 0)
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        ValidatePortfolio(statusMsg.PortfolioName);
        var subscription = new OrderSubscription
        {
            OrderId = statusMsg.OrderId,
            ClientOrderId = ParseIdentifier(statusMsg.OrderStringId) is var id &&
                id > 0 ? id : null,
            InstrumentId = statusMsg.SecurityId.SecurityCode.IsEmpty()
                ? null
                : GetInstrument(statusMsg.SecurityId).InstrumentId,
            Side = statusMsg.Side,
            From = statusMsg.From?.ToUniversalTime(),
            To = statusMsg.To?.ToUniversalTime(),
            Maximum = (statusMsg.Count ?? 1000).Min(5000).Max(1).To<int>(),
        };
        var open = await SocketClient.GetOpenOrdersAsync(EffectiveAccountId,
            cancellationToken);
        var history = await SocketClient.GetOrderHistoryAsync(
            EffectiveAccountId, cancellationToken);
        var orders = (open ?? []).Concat(history ?? [])
            .Where(order => MatchesOrder(subscription, order))
            .GroupBy(static order => order.OrderId)
            .Select(static group => group.First())
            .OrderBy(static order => order.ReceiveTime)
            .TakeLast(subscription.Maximum)
            .ToArray();
        foreach (var order in orders)
            await SendOrderAsync(order, statusMsg.TransactionId, true,
                cancellationToken);

        var trades = await SocketClient.GetAccountTradesAsync(
            EffectiveAccountId, 0, subscription.Maximum,
            cancellationToken);
        foreach (var trade in (trades ?? []).Where(trade =>
            MatchesTrade(subscription, trade)).OrderBy(static trade =>
            trade.TradeTimeMilliseconds > 0
                ? trade.TradeTimeMilliseconds
                : trade.TradeTime))
            await SendAccountTradeAsync(trade, statusMsg.TransactionId,
                cancellationToken);

        if (statusMsg.IsHistoryOnly())
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _orderSubscriptions[statusMsg.TransactionId] = subscription;
        try
        {
            await AcquireStreamAsync(AccountStreamKey, cancellationToken);
            await SendSubscriptionResultAsync(statusMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _orderSubscriptions.Remove(statusMsg.TransactionId);
            throw;
        }
    }

    private StreamKey AccountStreamKey => new(
        NdaxSubscriptionKinds.AccountEvents, 0, 0, EffectiveAccountId);

    private async ValueTask OnSocketPositionAsync(NdaxAccountPosition position,
        CancellationToken cancellationToken)
    {
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _portfolioSubscriptions];
        foreach (var target in targets)
            await SendPositionAsync(position, target, false,
                cancellationToken);
    }

    private async ValueTask OnSocketOrderAsync(NdaxOrder order,
        CancellationToken cancellationToken)
    {
        if (order is null)
            return;
        var targets = new HashSet<long>();
        using (_sync.EnterScope())
            foreach (var pair in _orderSubscriptions)
                if (MatchesOrder(pair.Value, order))
                    targets.Add(pair.Key);
        var tracked = GetTrackedOrder(order.OrderId, order.ClientOrderId);
        if (tracked is not null)
            targets.Add(tracked.TransactionId);
        foreach (var target in targets)
            await SendOrderAsync(order, target, false, cancellationToken);
    }

    private async ValueTask OnSocketAccountTradeAsync(NdaxAccountTrade trade,
        CancellationToken cancellationToken)
    {
        if (trade is null)
            return;
        var targets = new HashSet<long>();
        using (_sync.EnterScope())
            foreach (var pair in _orderSubscriptions)
                if (MatchesTrade(pair.Value, trade))
                    targets.Add(pair.Key);
        var tracked = GetTrackedOrder(trade.OrderId, trade.ClientOrderId);
        if (tracked is not null)
            targets.Add(tracked.TransactionId);
        foreach (var target in targets)
            await SendAccountTradeAsync(trade, target, cancellationToken);
    }

    private ValueTask OnSocketOrderRejectedAsync(NdaxNewOrderReject reject,
        CancellationToken cancellationToken)
    {
        if (reject is null)
            return default;
        var tracked = GetTrackedOrder(null, reject.ClientOrderId);
        if (tracked is null)
            return SendOutErrorAsync(new NdaxApiException(
                "NewOrderRejectEvent", null, reject.RejectReason ??
                    reject.Status ?? "order was rejected"), cancellationToken);
        tracked.State = OrderStates.Failed;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = GetInstrument(tracked.InstrumentId)?.Symbol
                .ToStockSharp() ?? default,
            ServerTime = CurrentTime,
            PortfolioName = PortfolioName,
            Side = tracked.Side,
            OrderId = tracked.OrderId > 0 ? tracked.OrderId : null,
            OrderVolume = tracked.Volume,
            Balance = 0m,
            OrderPrice = tracked.Price,
            OrderType = tracked.OrderType,
            OrderState = OrderStates.Failed,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = tracked.TransactionId,
            Condition = tracked.Condition,
            Error = new NdaxApiException("SendOrder", null,
                reject.RejectReason ?? reject.Status ??
                    "order was rejected"),
        }, cancellationToken);
    }

    private ValueTask SendPositionAsync(NdaxAccountPosition position,
        long targetId, bool isForced, CancellationToken cancellationToken)
    {
        if (position is null || position.ProductSymbol.IsEmpty())
            return default;
        var fingerprint = new PositionFingerprint(position.Amount,
            position.Hold);
        var key = $"{targetId}:{position.ProductId}";
        using (_sync.EnterScope())
        {
            if (!isForced && _positionFingerprints.TryGetValue(key,
                out var previous) && previous == fingerprint)
                return default;
            _positionFingerprints[key] = fingerprint;
        }
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = PortfolioName,
            SecurityId = new SecurityId
            {
                SecurityCode = position.ProductSymbol.ToUpperInvariant(),
                BoardCode = BoardCodes.NDAX,
            },
            ServerTime = CurrentTime,
            OriginalTransactionId = targetId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, position.Amount, true)
        .TryAdd(PositionChangeTypes.BlockedValue, position.Hold, true),
            cancellationToken);
    }

    private ValueTask SendOrderAsync(NdaxOrder order, long targetId,
        bool isForced, CancellationToken cancellationToken)
    {
        if (order is null)
            return default;
        var instrument = GetInstrument(order.InstrumentId);
        if (instrument is null)
            return default;
        var tracked = GetTrackedOrder(order.OrderId, order.ClientOrderId);
        var state = order.OrderState.ToOrderState();
        if (tracked is not null)
        {
            tracked.OrderId = order.OrderId;
            tracked.State = state;
            TrackOrder(tracked);
        }
        var fingerprint = new OrderFingerprint(order.OrderState,
            order.OrigQuantity, order.QuantityExecuted, order.Price,
            order.AvgPrice);
        var key = $"{targetId}:{order.OrderId}";
        using (_sync.EnterScope())
        {
            if (!isForced && _orderFingerprints.TryGetValue(key,
                out var previous) && previous == fingerprint)
                return default;
            _orderFingerprints[key] = fingerprint;
        }
        var total = order.OrigQuantity > 0
            ? order.OrigQuantity
            : order.Quantity;
        var balance = state is OrderStates.Done or OrderStates.Failed
            ? 0m
            : (total - order.QuantityExecuted).Max(0m);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = instrument.Symbol.ToStockSharp(),
            ServerTime = GetTime(order.ReceiveTime.FromNdaxTime()),
            PortfolioName = PortfolioName,
            Side = order.Side.ToSide(),
            OrderId = order.OrderId,
            OrderStringId = order.ClientOrderId > 0
                ? order.ClientOrderId.ToString(CultureInfo.InvariantCulture)
                : null,
            OrderVolume = total,
            Balance = balance,
            OrderPrice = order.Price,
            AveragePrice = order.AvgPrice > 0 ? order.AvgPrice : null,
            OrderType = order.OrderType.ToOrderType(),
            OrderState = state,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = targetId,
            Condition = tracked?.Condition,
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(order.RejectReason ??
                    order.ChangeReason ?? "NDAX order was rejected.")
                : null,
        }, cancellationToken);
    }

    private ValueTask SendAccountTradeAsync(NdaxAccountTrade trade,
        long targetId, CancellationToken cancellationToken)
    {
        if (trade is null || trade.TradeId <= 0)
            return default;
        using (_sync.EnterScope())
        {
            if (_accountTrades.Count > 100000)
                _accountTrades.Clear();
            if (!_accountTrades.Add(new(targetId, trade.TradeId)))
                return default;
        }
        var instrument = GetInstrument(trade.InstrumentId);
        if (instrument is null)
            return default;
        var tracked = GetTrackedOrder(trade.OrderId, trade.ClientOrderId);
        var time = trade.TradeTimeMilliseconds > 0
            ? trade.TradeTimeMilliseconds.FromMilliseconds()
            : trade.TradeTime.FromNdaxTime();
        string commissionCurrency = null;
        using (_sync.EnterScope())
            if (trade.FeeProductId is int feeProductId &&
                _products.TryGetValue(feeProductId, out var feeProduct))
                commissionCurrency = feeProduct.Symbol;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = instrument.Symbol.ToStockSharp(),
            ServerTime = GetTime(time),
            PortfolioName = PortfolioName,
            Side = trade.ToSide(),
            OrderId = trade.OrderId > 0 ? trade.OrderId : null,
            OrderStringId = trade.ClientOrderId > 0
                ? trade.ClientOrderId.ToString(CultureInfo.InvariantCulture)
                : null,
            TradeId = trade.TradeId,
            TradePrice = trade.Price,
            TradeVolume = trade.Quantity,
            Commission = trade.Fee,
            CommissionCurrency = commissionCurrency,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = targetId,
        }, cancellationToken);
    }

    private ValueTask SendTrackedOrderAsync(TrackedOrder order, long targetId,
        CancellationToken cancellationToken, decimal? balance = null)
    {
        var instrument = GetInstrument(order.InstrumentId);
        if (instrument is null)
            return default;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = instrument.Symbol.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = PortfolioName,
            Side = order.Side,
            OrderId = order.OrderId > 0 ? order.OrderId : null,
            OrderStringId = order.ClientOrderId.ToString(
                CultureInfo.InvariantCulture),
            OrderVolume = order.Volume,
            Balance = balance ?? order.Volume,
            OrderPrice = order.Price,
            OrderType = order.OrderType,
            OrderState = order.State,
            TransactionId = order.TransactionId,
            OriginalTransactionId = targetId,
            Condition = order.Condition,
        }, cancellationToken);
    }

    private bool MatchesOrder(OrderSubscription subscription,
        NdaxOrder order)
    {
        if (order is null)
            return false;
        if (subscription.OrderId is > 0 &&
            order.OrderId != subscription.OrderId)
            return false;
        if (subscription.ClientOrderId is > 0 &&
            order.ClientOrderId != subscription.ClientOrderId)
            return false;
        if (subscription.InstrumentId is int instrumentId &&
            order.InstrumentId != instrumentId)
            return false;
        if (subscription.Side is Sides side && order.Side.ToSide() != side)
            return false;
        var time = order.ReceiveTime.FromNdaxTime();
        return (subscription.From is null || time >= subscription.From) &&
            (subscription.To is null || time <= subscription.To);
    }

    private bool MatchesTrade(OrderSubscription subscription,
        NdaxAccountTrade trade)
    {
        if (trade is null)
            return false;
        if (subscription.OrderId is > 0 &&
            trade.OrderId != subscription.OrderId)
            return false;
        if (subscription.ClientOrderId is > 0 &&
            trade.ClientOrderId != subscription.ClientOrderId)
            return false;
        if (subscription.InstrumentId is int instrumentId &&
            trade.InstrumentId != instrumentId)
            return false;
        if (subscription.Side is Sides side && trade.ToSide() != side)
            return false;
        var time = trade.TradeTimeMilliseconds > 0
            ? trade.TradeTimeMilliseconds.FromMilliseconds()
            : trade.TradeTime.FromNdaxTime();
        return (subscription.From is null || time >= subscription.From) &&
            (subscription.To is null || time <= subscription.To);
    }

    private async ValueTask RemoveOrderSubscriptionAsync(long targetId,
        CancellationToken cancellationToken)
    {
        var removed = false;
        using (_sync.EnterScope())
        {
            removed = _orderSubscriptions.Remove(targetId);
            RemoveFingerprintPrefix(_orderFingerprints, targetId);
            _accountTrades.RemoveWhere(key => key.TargetId == targetId);
        }
        if (removed)
            await ReleaseStreamAsync(AccountStreamKey, cancellationToken);
    }

    private async ValueTask CompletePortfolioAsync(
        PortfolioLookupMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }

    private async ValueTask CompleteOrderStatusAsync(
        OrderStatusMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }

    private static long ParseIdentifier(string value)
        => long.TryParse(value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var result)
                ? result
                : 0;

    private static void RemoveFingerprintPrefix<TValue>(
        Dictionary<string, TValue> values, long targetId)
    {
        var prefix = targetId.ToString(CultureInfo.InvariantCulture) + ":";
        foreach (var key in values.Keys.Where(key => key.StartsWith(prefix,
            StringComparison.Ordinal)).ToArray())
            values.Remove(key);
    }
}
