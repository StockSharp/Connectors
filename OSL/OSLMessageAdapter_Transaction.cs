namespace StockSharp.OSL;

public partial class OSLMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var product = GetSymbol(regMsg.SecurityId);
        var orderType = regMsg.OrderType ?? OrderTypes.Limit;
        if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
            throw new NotSupportedException(
                LocalizedStrings.OrderUnsupportedType.Put(orderType,
                    regMsg.TransactionId));
        var volume = regMsg.Volume.Abs();
        if (volume <= 0)
            throw new InvalidOperationException(
                "OSL order volume must be positive.");
        if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
            throw new InvalidOperationException(
                "An OSL limit order requires a positive price.");
        if (orderType == OrderTypes.Market && regMsg.PostOnly == true)
            throw new InvalidOperationException(
                "OSL post-only applies to limit orders only.");
        if (orderType == OrderTypes.Market && regMsg.TimeInForce is not null)
            throw new NotSupportedException(
                "OSL SPOT market orders do not accept time-in-force.");
        if (regMsg.PostOnly == true && regMsg.TimeInForce is not null and
            not TimeInForce.PutInQueue)
            throw new InvalidOperationException(
                "OSL post-only cannot be combined with IOC or FOK.");
        if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
            throw new NotSupportedException(
                "OSL SPOT does not document iceberg orders.");
        if (regMsg.TillDate is not null)
            throw new NotSupportedException(
                "OSL SPOT does not document GTD orders.");

        var condition = regMsg.Condition as OSLOrderCondition ?? new();
        if (condition.QuoteAmount is <= 0)
            throw new InvalidOperationException(
                "OSL market-buy quote amount must be positive.");
        if (condition.QuoteAmount is not null &&
            (orderType != OrderTypes.Market || regMsg.Side != Sides.Buy))
            throw new InvalidOperationException(
                "OSL quote amount applies to market buys only.");

        var clientOrderId = CreateClientOrderId(regMsg.TransactionId,
            regMsg.UserOrderId);
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            ClientOrderId = clientOrderId,
            Symbol = product.Symbol,
            Side = regMsg.Side,
            OrderType = orderType,
            Volume = volume,
            Price = regMsg.Price,
            Condition = condition.Clone() as OSLOrderCondition,
            State = OrderStates.Pending,
        };
        TrackOrder(tracked, clientOrderId,
            $"transaction:{regMsg.TransactionId.ToString(
                CultureInfo.InvariantCulture)}");
        await EnsureTransactionStreamsAsync(product.Symbol,
            cancellationToken);

        var order = await RestClient.PlaceOrderAsync(new()
        {
            Symbol = product.Symbol,
            Side = regMsg.Side.ToOSL(),
            Type = orderType == OrderTypes.Market
                ? OSLOrderTypes.Market
                : OSLOrderTypes.Limit,
            ClientOrderId = clientOrderId,
            TimeInForce = orderType == OrderTypes.Limit
                ? regMsg.TimeInForce.ToOSL(regMsg.PostOnly == true)
                : null,
            Quantity = condition.QuoteAmount is not null ? null : volume,
            Amount = condition.QuoteAmount,
            Price = orderType == OrderTypes.Limit ? regMsg.Price : null,
            SelfTradePreventionMode =
                condition.SelfTradePrevention.ToOSL(),
        }, cancellationToken);
        if (order is not null)
        {
            tracked.OrderId = order.OrderId;
            tracked.ClientOrderId = order.EffectiveClientOrderId.IsEmpty()
                ? clientOrderId
                : order.EffectiveClientOrderId;
            tracked.State = order.Status.ToStockSharpOrderState();
            TrackOrder(tracked, tracked.OrderId, tracked.ClientOrderId);
            await SendOrderAsync(order, regMsg.TransactionId, true,
                cancellationToken);
        }
        else
            await SendRegisteredOrderAsync(tracked, regMsg.TransactionId,
                cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var tracked = GetTrackedOrder(cancelMsg.OrderStringId) ??
            GetTrackedOrder(cancelMsg.OrderId?.ToString(
                CultureInfo.InvariantCulture)) ??
            GetTrackedOrder(cancelMsg.OriginalTransactionId);
        var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? tracked?.Symbol
            : GetSymbol(cancelMsg.SecurityId).Symbol;
        if (symbol.IsEmpty())
            throw new InvalidOperationException(
                "OSL cancellation requires a security identifier.");

        long? orderId = cancelMsg.OrderId;
        string clientOrderId = null;
        if (orderId is null && !cancelMsg.OrderStringId.IsEmpty())
        {
            if (long.TryParse(cancelMsg.OrderStringId,
                NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var numericId))
                orderId = numericId;
            else
                clientOrderId = cancelMsg.OrderStringId.Trim();
        }
        if (orderId is null && clientOrderId.IsEmpty())
        {
            orderId = tracked?.OrderId.ToLong();
            clientOrderId = orderId is null ? tracked?.ClientOrderId : null;
        }
        if (orderId is null && clientOrderId.IsEmpty())
            throw new InvalidOperationException(
                "OSL cancellation requires an order ID or client order ID.");

        var order = await RestClient.CancelOrderAsync(new()
        {
            Symbol = symbol,
            OrderId = orderId,
            ClientOrderId = clientOrderId,
        }, cancellationToken);
        if (order is not null)
            await SendOrderAsync(order, cancelMsg.TransactionId, true,
                cancellationToken);
        else if (tracked is not null)
        {
            tracked.State = OrderStates.Done;
            await SendCanceledOrderAsync(tracked, cancelMsg.TransactionId,
                cancellationToken);
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
                "OSL SPOT cancellation cannot close positions.");
        var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetSymbol(cancelMsg.SecurityId).Symbol;

        if (cancelMsg.Side is null)
        {
            var cancelled = await RestClient.CancelAllOrdersAsync(new()
            {
                Symbol = symbol,
            }, cancellationToken);
            foreach (var order in cancelled ?? [])
                await SendOrderAsync(order, cancelMsg.TransactionId, true,
                    cancellationToken);
            return;
        }

        var open = await RestClient.GetOpenOrdersAsync(symbol, null, null,
            null, null, 100, cancellationToken);
        foreach (var order in (open ?? []).Where(order =>
            order?.Side.IsEmpty() == false &&
            order.Side.ToStockSharpSide() == cancelMsg.Side))
        {
            if (order.OrderId.ToLong() is not long orderId ||
                order.EffectiveSymbol.IsEmpty())
                continue;
            var cancelled = await RestClient.CancelOrderAsync(new()
            {
                Symbol = order.EffectiveSymbol,
                OrderId = orderId,
            }, cancellationToken);
            await SendOrderAsync(cancelled ?? order,
                cancelMsg.TransactionId, true, cancellationToken);
        }
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
                RemoveFingerprintPrefix(_balanceFingerprints,
                    lookupMsg.OriginalTransactionId);
            }
            if (removed)
                await ReleasePrivateStreamAsync(OSLWsChannels.SpotAssets,
                    "default", cancellationToken);
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = GetPortfolioName(),
            BoardCode = BoardCodes.OSL,
            OriginalTransactionId = lookupMsg.TransactionId,
        }, cancellationToken);
        foreach (var asset in await RestClient.GetAssetsAsync(null,
            cancellationToken) ?? [])
            await SendBalanceAsync(asset, lookupMsg.TransactionId, true,
                cancellationToken);
        if (lookupMsg.IsHistoryOnly())
        {
            await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
                cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _portfolioSubscriptions.Add(lookupMsg.TransactionId);
        try
        {
            await AcquirePrivateStreamAsync(OSLWsChannels.SpotAssets,
                "default", cancellationToken);
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
        var suppliedId = statusMsg.OrderStringId?.Trim();
        var orderId = statusMsg.OrderId?.ToString(
            CultureInfo.InvariantCulture);
        var clientOrderId = suppliedId;
        if (!suppliedId.IsEmpty() && long.TryParse(suppliedId,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            orderId = suppliedId;
            clientOrderId = null;
        }
        var subscription = new OrderSubscription
        {
            OrderId = orderId,
            ClientOrderId = clientOrderId,
            Symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
                ? null
                : GetSymbol(statusMsg.SecurityId).Symbol,
            Side = statusMsg.Side,
            From = statusMsg.From?.ToUniversalTime(),
            To = statusMsg.To?.ToUniversalTime(),
            Maximum = (statusMsg.Count ?? 1000).Min(5000).Max(1).To<int>(),
        };
        var orders = await LoadOrdersAsync(subscription, cancellationToken);
        foreach (var order in orders)
            await SendOrderWithFillsAsync(order, statusMsg.TransactionId,
                true, cancellationToken);
        if (statusMsg.IsHistoryOnly())
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }

        subscription.StreamSymbols = subscription.Symbol.IsEmpty()
            ? GetOnlineSymbols()
            : [subscription.Symbol];
        using (_sync.EnterScope())
            _orderSubscriptions[statusMsg.TransactionId] = subscription;
        var acquiredSymbols = new List<string>();
        var fillAcquired = false;
        try
        {
            await AcquirePrivateStreamAsync(OSLWsChannels.Fill, "default",
                cancellationToken);
            fillAcquired = true;
            foreach (var symbol in subscription.StreamSymbols)
            {
                await AcquirePrivateStreamAsync(OSLWsChannels.Orders, symbol,
                    cancellationToken);
                acquiredSymbols.Add(symbol);
            }
            await SendSubscriptionResultAsync(statusMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _orderSubscriptions.Remove(statusMsg.TransactionId);
            foreach (var symbol in acquiredSymbols)
                await ReleasePrivateStreamAsync(OSLWsChannels.Orders, symbol,
                    cancellationToken);
            if (fillAcquired)
                await ReleasePrivateStreamAsync(OSLWsChannels.Fill,
                    "default", cancellationToken);
            throw;
        }
    }

    private async ValueTask EnsureTransactionStreamsAsync(string symbol,
        CancellationToken cancellationToken)
    {
        var acquireFill = false;
        var acquireOrder = false;
        using (_sync.EnterScope())
        {
            if (!_isTransactionFillSubscribed)
            {
                _isTransactionFillSubscribed = true;
                acquireFill = true;
            }
            acquireOrder = _transactionSymbols.Add(symbol.NormalizeSymbol());
        }
        var fillAcquired = false;
        try
        {
            if (acquireFill)
            {
                await AcquirePrivateStreamAsync(OSLWsChannels.Fill,
                    "default", cancellationToken);
                fillAcquired = true;
            }
            if (acquireOrder)
                await AcquirePrivateStreamAsync(OSLWsChannels.Orders, symbol,
                    cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
            {
                if (acquireFill)
                    _isTransactionFillSubscribed = false;
                if (acquireOrder)
                    _transactionSymbols.Remove(symbol);
            }
            if (fillAcquired)
                await ReleasePrivateStreamAsync(OSLWsChannels.Fill,
                    "default", cancellationToken);
            throw;
        }
    }

    private async ValueTask<OSLOrder[]> LoadOrdersAsync(
        OrderSubscription subscription, CancellationToken cancellationToken)
    {
        var result = new List<OSLOrder>();
        var open = await RestClient.GetOpenOrdersAsync(subscription.Symbol,
            subscription.OrderId, subscription.From, subscription.To, null,
            subscription.Maximum.Min(100), cancellationToken);
        result.AddRange((open ?? []).Where(order =>
            MatchesOrder(subscription, order)));

        string cursor = null;
        while (result.Count < subscription.Maximum)
        {
            var limit = (subscription.Maximum - result.Count).Min(100).Max(1);
            var page = await RestClient.GetHistoryOrdersAsync(
                subscription.Symbol, subscription.OrderId, subscription.From,
                subscription.To, cursor, limit, cancellationToken);
            if (page is not { Length: > 0 })
                break;
            result.AddRange(page.Where(order =>
                MatchesOrder(subscription, order)));
            if (page.Length < limit || !subscription.OrderId.IsEmpty())
                break;
            var next = page.LastOrDefault(static order =>
                order?.OrderId.IsEmpty() == false)?.OrderId;
            if (next.IsEmpty() || next.EqualsIgnoreCase(cursor))
                break;
            cursor = next;
        }

        return [.. result.Where(static order => order is not null)
            .GroupBy(static order => order.OrderId.IsEmpty()
                    ? order.EffectiveClientOrderId
                    : order.OrderId,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static order => order.CreationTime.ToLong() ??
                order.EffectiveUpdateTime.ToLong())
            .TakeLast(subscription.Maximum)];
    }

    private async ValueTask OnSocketAssetsAsync(OSLAsset[] assets,
        CancellationToken cancellationToken)
    {
        if (assets is not { Length: > 0 })
            return;
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _portfolioSubscriptions];
        foreach (var target in targets)
            foreach (var asset in assets)
                await SendBalanceAsync(asset, target, false,
                    cancellationToken);
    }

    private async ValueTask OnSocketOrderAsync(OSLOrder order,
        CancellationToken cancellationToken)
    {
        if (order is null)
            return;
        var targets = new HashSet<long>();
        using (_sync.EnterScope())
            foreach (var pair in _orderSubscriptions)
                if (MatchesOrder(pair.Value, order))
                    targets.Add(pair.Key);
        var tracked = MatchTrackedOrder(order);
        if (tracked is not null)
            targets.Add(tracked.TransactionId);
        foreach (var target in targets)
            await SendOrderAsync(order, target, false, cancellationToken);
    }

    private async ValueTask OnSocketFillAsync(OSLFill fill,
        CancellationToken cancellationToken)
    {
        if (fill is null)
            return;
        var targets = new HashSet<long>();
        using (_sync.EnterScope())
            foreach (var pair in _orderSubscriptions)
                if (MatchesFill(pair.Value, fill))
                    targets.Add(pair.Key);
        var tracked = GetTrackedOrder(fill.OrderId) ??
            GetTrackedOrder(fill.ClientOrderId);
        if (tracked is not null)
            targets.Add(tracked.TransactionId);
        foreach (var target in targets)
            await SendAccountTradeAsync(fill, target, cancellationToken);
    }

    private ValueTask SendBalanceAsync(OSLAsset asset, long targetId,
        bool isForced, CancellationToken cancellationToken)
    {
        if (asset?.EffectiveCoin.IsEmpty() != false)
            return default;
        var available = asset.Available.ToDecimal() ?? 0m;
        var frozen = asset.Frozen.ToDecimal() ?? 0m;
        var locked = asset.EffectiveLocked.ToDecimal() ?? 0m;
        var fingerprint = new BalanceFingerprint(available, frozen, locked);
        var key = $"{targetId}:{asset.EffectiveCoin}";
        using (_sync.EnterScope())
        {
            if (!isForced && _balanceFingerprints.TryGetValue(key,
                out var previous) && previous == fingerprint)
                return default;
            _balanceFingerprints[key] = fingerprint;
        }
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = GetPortfolioName(),
            SecurityId = new SecurityId
            {
                SecurityCode = asset.EffectiveCoin.ToUpperInvariant(),
                BoardCode = BoardCodes.OSL,
            },
            ServerTime = GetTime(asset.UpdateTime),
            OriginalTransactionId = targetId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, available, true)
        .TryAdd(PositionChangeTypes.BlockedValue, frozen + locked, true),
            cancellationToken);
    }

    private async ValueTask SendOrderWithFillsAsync(OSLOrder order,
        long targetId, bool isForced, CancellationToken cancellationToken)
    {
        await SendOrderAsync(order, targetId, isForced, cancellationToken);
        var executed = order?.EffectiveExecutedQuantity.ToDecimal() ?? 0m;
        if (order?.OrderId.IsEmpty() != false ||
            order.EffectiveSymbol.IsEmpty() || executed <= 0)
            return;
        foreach (var fill in await RestClient.GetFillsAsync(
            order.EffectiveSymbol, order.OrderId, null, null, null, 100,
            cancellationToken) ?? [])
            await SendAccountTradeAsync(fill, targetId, cancellationToken);
    }

    private ValueTask SendOrderAsync(OSLOrder order, long targetId,
        bool isForced, CancellationToken cancellationToken)
    {
        if (order is null || order.EffectiveSymbol.IsEmpty())
            return default;
        var tracked = MatchTrackedOrder(order);
        var clientOrderId = order.EffectiveClientOrderId;
        if (tracked is not null)
        {
            if (!order.OrderId.IsEmpty())
                tracked.OrderId = order.OrderId;
            if (!clientOrderId.IsEmpty())
                tracked.ClientOrderId = clientOrderId;
            tracked.State = order.Status.ToStockSharpOrderState();
            TrackOrder(tracked, tracked.OrderId, tracked.ClientOrderId);
        }
        var total = tracked?.Volume ??
            order.OriginalQuantity.ToDecimal() ??
            order.NewSize.ToDecimal() ?? order.Size.ToDecimal();
        var executed = order.EffectiveExecutedQuantity.ToDecimal();
        var state = order.Status.ToStockSharpOrderState();
        var price = order.Price.ToDecimal() ?? tracked?.Price ??
            (state == OrderStates.Active
                ? order.AveragePrice.ToDecimal()
                : null);
        var average = order.EffectiveAveragePrice.ToDecimal();
        if ((executed ?? 0m) <= 0)
            average = null;
        var fingerprint = new OrderFingerprint(order.Status, total, executed,
            price, average);
        var identity = order.OrderId.IsEmpty()
            ? clientOrderId
            : order.OrderId;
        var key = $"{targetId}:{identity}";
        using (_sync.EnterScope())
        {
            if (!isForced && _orderFingerprints.TryGetValue(key,
                out var previous) && previous == fingerprint)
                return default;
            _orderFingerprints[key] = fingerprint;
        }
        decimal? balance = total is decimal volume
            ? (volume - (executed ?? 0m)).Max(0m)
            : null;
        if (state == OrderStates.Done)
            balance = 0m;
        var side = order.Side.IsEmpty()
            ? tracked?.Side ?? throw new InvalidDataException(
                $"OSL order '{identity}' has no side.")
            : order.Side.ToStockSharpSide();
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.EffectiveSymbol.ToStockSharp(),
            ServerTime = GetTime(order.EffectiveUpdateTime.IsEmpty()
                ? order.CreationTime
                : order.EffectiveUpdateTime),
            PortfolioName = GetPortfolioName(),
            Side = side,
            OrderVolume = total,
            Balance = balance,
            OrderPrice = price ?? 0m,
            AveragePrice = average,
            OrderType = order.EffectiveOrderType.ToStockSharpOrderType(),
            OrderState = state,
            OrderId = order.OrderId.ToLong(),
            OrderStringId = order.OrderId.ToLong() is null
                ? order.OrderId
                : null,
            UserOrderId = clientOrderId,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = targetId,
            TimeInForce = order.EffectiveTimeInForce
                .ToStockSharpTimeInForce(),
            Condition = tracked?.Condition,
        }, cancellationToken);
    }

    private ValueTask SendAccountTradeAsync(OSLFill fill, long targetId,
        CancellationToken cancellationToken)
    {
        if (fill is null || fill.Symbol.IsEmpty() || fill.TradeId.IsEmpty() ||
            !AddAccountTrade(fill.TradeId, targetId))
            return default;
        var tracked = GetTrackedOrder(fill.OrderId) ??
            GetTrackedOrder(fill.ClientOrderId);
        var fee = (fill.FeeDetails ?? []).FirstOrDefault(static value =>
            value?.Fee.ToDecimal() is not null);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = fill.Symbol.ToStockSharp(),
            ServerTime = GetTime(fill.UpdateTime.IsEmpty()
                ? fill.CreationTime
                : fill.UpdateTime),
            PortfolioName = GetPortfolioName(),
            Side = fill.Side.ToStockSharpSide(),
            OrderId = fill.OrderId.ToLong(),
            OrderStringId = fill.OrderId.ToLong() is null
                ? fill.OrderId
                : null,
            UserOrderId = fill.ClientOrderId,
            TradeStringId = fill.TradeId,
            TradePrice = fill.Price.ToDecimal(),
            TradeVolume = fill.Size.ToDecimal(),
            Commission = fee?.Fee.ToDecimal(),
            CommissionCurrency = fee?.Coin,
            IsMarketMaker = fill.TradeScope.EqualsIgnoreCase("maker"),
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = targetId,
        }, cancellationToken);
    }

    private ValueTask SendRegisteredOrderAsync(TrackedOrder order,
        long targetId, CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.Symbol.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = order.Side,
            OrderVolume = order.Volume,
            Balance = order.Volume,
            OrderPrice = order.Price,
            OrderType = order.OrderType,
            OrderState = OrderStates.Active,
            UserOrderId = order.ClientOrderId,
            TransactionId = order.TransactionId,
            OriginalTransactionId = targetId,
            Condition = order.Condition,
        }, cancellationToken);

    private ValueTask SendCanceledOrderAsync(TrackedOrder order,
        long targetId, CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.Symbol.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = order.Side,
            OrderVolume = order.Volume,
            Balance = 0m,
            OrderPrice = order.Price,
            OrderType = order.OrderType,
            OrderState = OrderStates.Done,
            OrderId = order.OrderId.ToLong(),
            OrderStringId = order.OrderId.ToLong() is null
                ? order.OrderId
                : null,
            UserOrderId = order.ClientOrderId,
            TransactionId = order.TransactionId,
            OriginalTransactionId = targetId,
            Condition = order.Condition,
        }, cancellationToken);

    private bool MatchesOrder(OrderSubscription subscription, OSLOrder order)
    {
        if (order is null)
            return false;
        if (!subscription.OrderId.IsEmpty() &&
            !subscription.OrderId.EqualsIgnoreCase(order.OrderId))
            return false;
        if (!subscription.ClientOrderId.IsEmpty() &&
            !subscription.ClientOrderId.EqualsIgnoreCase(
                order.EffectiveClientOrderId))
            return false;
        if (!subscription.Symbol.IsEmpty() &&
            !subscription.Symbol.EqualsIgnoreCase(order.EffectiveSymbol))
            return false;
        if (subscription.Side is Sides side && !order.Side.IsEmpty() &&
            order.Side.ToStockSharpSide() != side)
            return false;
        var created = GetTime(order.CreationTime.IsEmpty()
            ? order.EffectiveUpdateTime
            : order.CreationTime);
        return (subscription.From is null || created >= subscription.From) &&
            (subscription.To is null || created <= subscription.To);
    }

    private bool MatchesFill(OrderSubscription subscription, OSLFill fill)
    {
        if (fill is null)
            return false;
        if (!subscription.OrderId.IsEmpty() &&
            !subscription.OrderId.EqualsIgnoreCase(fill.OrderId))
            return false;
        if (!subscription.ClientOrderId.IsEmpty() &&
            !subscription.ClientOrderId.EqualsIgnoreCase(fill.ClientOrderId))
            return false;
        if (!subscription.Symbol.IsEmpty() &&
            !subscription.Symbol.EqualsIgnoreCase(fill.Symbol))
            return false;
        if (subscription.Side is Sides side && !fill.Side.IsEmpty() &&
            fill.Side.ToStockSharpSide() != side)
            return false;
        var created = GetTime(fill.CreationTime.IsEmpty()
            ? fill.UpdateTime
            : fill.CreationTime);
        return (subscription.From is null || created >= subscription.From) &&
            (subscription.To is null || created <= subscription.To);
    }

    private async ValueTask RemoveOrderSubscriptionAsync(long targetId,
        CancellationToken cancellationToken)
    {
        OrderSubscription subscription = null;
        using (_sync.EnterScope())
        {
            _orderSubscriptions.Remove(targetId, out subscription);
            RemoveFingerprintPrefix(_orderFingerprints, targetId);
            _seenAccountTrades.RemoveWhere(key => key.TargetId == targetId);
        }
        if (subscription is null)
            return;
        foreach (var symbol in subscription.StreamSymbols ?? [])
            await ReleasePrivateStreamAsync(OSLWsChannels.Orders, symbol,
                cancellationToken);
        await ReleasePrivateStreamAsync(OSLWsChannels.Fill, "default",
            cancellationToken);
    }

    private async ValueTask CompleteOrderStatusAsync(
        OrderStatusMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }

    private static void RemoveFingerprintPrefix<TValue>(
        Dictionary<string, TValue> values, long targetId)
    {
        var prefix = targetId.ToString(CultureInfo.InvariantCulture) + ":";
        foreach (var key in values.Keys.Where(key => key.StartsWith(prefix,
            StringComparison.Ordinal)).ToArray())
            values.Remove(key);
    }

    private static string CreateClientOrderId(long transactionId,
        string userOrderId)
    {
        var value = userOrderId.IsEmpty()
            ? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}"
            : userOrderId.Trim();
        if (value.IsEmpty() || value.Any(static ch =>
            ch is not (>= 'a' and <= 'z') and
                not (>= 'A' and <= 'Z') and
                not (>= '0' and <= '9') and not '-' and not '_'))
            throw new InvalidOperationException(
                "OSL client order IDs may contain ASCII letters, digits, hyphens, and underscores only.");
        return value;
    }
}
