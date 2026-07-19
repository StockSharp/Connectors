namespace StockSharp.Zoomex;

public partial class ZoomexMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var product = GetProduct(regMsg.SecurityId);
        var volume = regMsg.Volume.Abs();
        if (volume <= 0)
            throw new InvalidOperationException(
                "Order volume must be positive.");
        var condition = regMsg.Condition as ZoomexOrderCondition ?? new();
        var isConditional = regMsg.OrderType == OrderTypes.Conditional ||
            condition.TriggerPrice is not null;
        var isMarket = regMsg.OrderType == OrderTypes.Market ||
            isConditional && regMsg.Price <= 0;
        if (!isMarket && regMsg.Price <= 0)
            throw new InvalidOperationException(
                "A positive limit price is required.");
        if (isConditional && condition.TriggerPrice is not (> 0m))
            throw new InvalidOperationException(
                "A positive trigger price is required.");
        if (isMarket && regMsg.PostOnly == true)
            throw new InvalidOperationException(
                "Market orders cannot be post-only.");
        if (product.Category == ZoomexCategories.Spot &&
            (condition.IsReduceOnly || condition.IsCloseOnTrigger ||
                regMsg.PositionEffect == OrderPositionEffects.CloseOnly))
            throw new InvalidOperationException(
                "Reduce-only and close-on-trigger flags apply to futures only.");

        var linkId = CreateOrderLinkId(regMsg.TransactionId,
            regMsg.UserOrderId);
        var request = new ZoomexPlaceOrderRequest
        {
            Category = product.Category,
            Symbol = product.Symbol,
            Side = regMsg.Side.ToNative(),
            OrderType = isMarket
                ? ZoomexOrderTypes.Market
                : ZoomexOrderTypes.Limit,
            Quantity = volume.ToNative(),
            Price = isMarket ? null : regMsg.Price.ToNative(),
            TimeInForce = isMarket
                ? null
                : regMsg.TimeInForce.ToNative(regMsg.PostOnly == true),
            PositionIndex = product.Category == ZoomexCategories.Spot
                ? null
                : condition.PositionIndex.ToNative(),
            OrderLinkId = linkId,
            TriggerDirection = isConditional &&
                product.Category != ZoomexCategories.Spot
                ? condition.TriggerDirection.ToNative()
                : null,
            TriggerPrice = isConditional
                ? condition.TriggerPrice.ToNative()
                : null,
            TriggerBy = isConditional &&
                product.Category != ZoomexCategories.Spot
                    ? condition.TriggerPriceType.ToNative()
                    : null,
            IsReduceOnly = product.Category == ZoomexCategories.Spot
                ? null
                : condition.IsReduceOnly ||
                    regMsg.PositionEffect == OrderPositionEffects.CloseOnly,
            IsCloseOnTrigger = product.Category == ZoomexCategories.Spot
                ? null
                : condition.IsCloseOnTrigger,
            MarketUnit = product.Category == ZoomexCategories.Spot &&
                isMarket && !isConditional
                ? condition.MarketUnit.ToNative()
                : null,
            OrderFilter = product.Category == ZoomexCategories.Spot &&
                isConditional ? "StopOrder" : null,
        };
        var result = await RestClient.PlaceOrderAsync(request,
            cancellationToken);
        if (result?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                "Zoomex returned no order identifier.");

        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            Category = product.Category,
            Symbol = product.Symbol,
            OrderId = result.OrderId,
            OrderLinkId = result.OrderLinkId.IsEmpty()
                ? linkId
                : result.OrderLinkId,
            Side = regMsg.Side,
            OrderType = isConditional
                ? OrderTypes.Conditional
                : regMsg.OrderType ?? OrderTypes.Limit,
            Volume = volume,
            Price = isMarket ? 0m : regMsg.Price,
            Condition = condition,
            State = OrderStates.Active,
        };
        TrackOrder(tracked);
        await SendOrderAcknowledgementAsync(tracked,
            regMsg.TransactionId, OrderStates.Active, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(replaceMsg.PortfolioName);
        var product = GetProduct(replaceMsg.SecurityId);
        var volume = replaceMsg.Volume.Abs();
        if (volume <= 0 || replaceMsg.Price <= 0)
            throw new InvalidOperationException(
                "Replacement price and volume must be positive.");
        ResolveOrderIdentity(replaceMsg.OldOrderId,
            replaceMsg.OldOrderStringId, null, out var orderId,
            out var orderLinkId, true);
        var condition = replaceMsg.Condition as ZoomexOrderCondition;
        var result = await RestClient.AmendOrderAsync(new()
        {
            Category = product.Category,
            Symbol = product.Symbol,
            OrderId = orderId,
            OrderLinkId = orderLinkId,
            Quantity = volume.ToNative(),
            Price = replaceMsg.Price.ToNative(),
            TriggerPrice = condition?.TriggerPrice.ToNative(),
            TriggerBy = condition is null
                ? null
                : condition.TriggerPriceType.ToNative(),
        }, cancellationToken);
        if (result?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                "Zoomex returned no amended order identifier.");
        var tracked = new TrackedOrder
        {
            TransactionId = replaceMsg.TransactionId,
            Category = product.Category,
            Symbol = product.Symbol,
            OrderId = result.OrderId,
            OrderLinkId = result.OrderLinkId.IsEmpty()
                ? orderLinkId
                : result.OrderLinkId,
            Side = replaceMsg.Side,
            OrderType = condition?.TriggerPrice is not null
                ? OrderTypes.Conditional
                : OrderTypes.Limit,
            Volume = volume,
            Price = replaceMsg.Price,
            Condition = condition,
            State = OrderStates.Active,
        };
        TrackOrder(tracked);
        await SendOrderAcknowledgementAsync(tracked,
            replaceMsg.TransactionId, OrderStates.Active, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var product = GetProduct(cancelMsg.SecurityId);
        ResolveOrderIdentity(cancelMsg.OrderId, cancelMsg.OrderStringId,
            cancelMsg.UserOrderId, out var orderId, out var orderLinkId,
            true);
        var result = await RestClient.CancelOrderAsync(new()
        {
            Category = product.Category,
            Symbol = product.Symbol,
            OrderId = orderId,
            OrderLinkId = orderLinkId,
        }, cancellationToken);
        if (result?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                "Zoomex returned no canceled order identifier.");
        var tracked = GetTrackedOrder(result.OrderId) ??
            GetTrackedOrder(result.OrderLinkId);
        if (tracked is not null)
            await SendOrderAcknowledgementAsync(tracked,
                cancelMsg.TransactionId, OrderStates.Done,
                cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var product = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetProduct(cancelMsg.SecurityId);
        var categories = product is null
            ? GetEnabledCategories()
            : [product.Category];

        if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
            await ClosePositionsAsync(product, cancelMsg.TransactionId,
                cancellationToken);

        foreach (var category in categories)
        {
            var scopes = GetScopes(category, product?.Symbol);
            foreach (var scope in scopes)
            {
                if (cancelMsg.Side is null && cancelMsg.IsStop is null)
                {
                    var result = await RestClient.CancelAllOrdersAsync(new()
                    {
                        Category = category,
                        Symbol = scope.Symbol,
                        SettleCoin = scope.SettleCoin,
                    }, cancellationToken);
                    foreach (var item in result?.Items ?? [])
                    {
                        var tracked = GetTrackedOrder(item.OrderId) ??
                            GetTrackedOrder(item.OrderLinkId);
                        if (tracked is not null)
                            await SendOrderAcknowledgementAsync(tracked,
                                cancelMsg.TransactionId, OrderStates.Done,
                                cancellationToken);
                    }
                    continue;
                }

                foreach (var order in await LoadOpenOrdersAsync(category,
                    scope.Symbol, scope.SettleCoin, null, null,
                    cancellationToken))
                {
                    if (!MatchesCancel(cancelMsg, order))
                        continue;
                    await RestClient.CancelOrderAsync(new()
                    {
                        Category = category,
                        Symbol = order.Symbol,
                        OrderId = order.OrderId,
                        OrderLinkId = order.OrderLinkId,
                    }, cancellationToken);
                }
            }
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
            using (_sync.EnterScope())
            {
                _portfolioSubscriptions.Remove(
                    lookupMsg.OriginalTransactionId);
                RemoveFingerprintPrefix(_balanceFingerprints,
                    lookupMsg.OriginalTransactionId);
                RemoveFingerprintPrefix(_positionFingerprints,
                    lookupMsg.OriginalTransactionId);
            }
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = GetPortfolioName(),
            BoardCode = BoardCodes.Zoomex,
            OriginalTransactionId = lookupMsg.TransactionId,
        }, cancellationToken);
        await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true,
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
        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
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
            RemoveOrderSubscription(statusMsg.OriginalTransactionId);
            return;
        }
        if (statusMsg.Count is <= 0)
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        ValidatePortfolio(statusMsg.PortfolioName);
        ResolveOrderIdentity(statusMsg.OrderId, statusMsg.OrderStringId,
            statusMsg.UserOrderId, out var orderId, out var orderLinkId,
            false);
        var product = statusMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetProduct(statusMsg.SecurityId);
        var subscription = new OrderSubscription
        {
            Categories = product is null
                ? GetEnabledCategories()
                : [product.Category],
            Symbol = product?.Symbol,
            OrderId = orderId,
            OrderLinkId = orderLinkId,
            Side = statusMsg.Side,
            From = statusMsg.From?.ToUniversalTime(),
            To = statusMsg.To?.ToUniversalTime(),
            Maximum = (statusMsg.Count ?? 1000).Min(10000).Max(1).To<int>(),
        };
        await SendOrderSnapshotAsync(subscription, statusMsg.TransactionId,
            true, cancellationToken);
        if (statusMsg.IsHistoryOnly())
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _orderSubscriptions[statusMsg.TransactionId] = subscription;
        await SendSubscriptionResultAsync(statusMsg, cancellationToken);
    }

    private async ValueTask SendPortfolioSnapshotAsync(long target,
        bool isForced, CancellationToken cancellationToken)
    {
        var wallets = await RestClient.GetWalletBalanceAsync(
            AccountType.ToNative(), cancellationToken);
        foreach (var account in wallets?.Items ?? [])
            await SendWalletAsync(account, target, isForced, CurrentTime,
                cancellationToken);
        if (AccountType == ZoomexAccountTypes.Spot)
            return;
        foreach (var category in GetEnabledCategories().Where(
            static value => value != ZoomexCategories.Spot))
        {
            foreach (var scope in GetScopes(category, null))
                foreach (var position in await LoadPositionsAsync(category,
                    scope.Symbol, scope.SettleCoin, cancellationToken))
                    await SendPositionAsync(position, target, isForced,
                        CurrentTime, cancellationToken);
        }
    }

    private async ValueTask SendOrderSnapshotAsync(
        OrderSubscription subscription, long target, bool isForced,
        CancellationToken cancellationToken)
    {
        foreach (var category in subscription.Categories)
        {
            foreach (var scope in GetScopes(category,
                subscription.Symbol))
            {
                foreach (var order in await LoadOpenOrdersAsync(category,
                    scope.Symbol, scope.SettleCoin, subscription.OrderId,
                    subscription.OrderLinkId, cancellationToken))
                    if (MatchesOrder(subscription, order))
                        await SendOrderAsync(order, target, isForced,
                            cancellationToken);
            }
            foreach (var order in await LoadOrderHistoryAsync(category,
                subscription, cancellationToken))
                await SendOrderAsync(order, target, isForced,
                    cancellationToken);
            foreach (var execution in await LoadExecutionsAsync(category,
                subscription, cancellationToken))
                await SendExecutionAsync(execution, target,
                    cancellationToken);
        }
    }

    private async ValueTask OnSocketOrdersAsync(ZoomexOrder[] orders,
        long timestamp, CancellationToken cancellationToken)
    {
        _ = timestamp;
        foreach (var order in orders ?? [])
        {
            if (order?.OrderId.IsEmpty() != false)
                continue;
            var targets = GetOrderTargets(order.Category, order.Symbol,
                order.OrderId, order.OrderLinkId, order.Side,
                GetOrderUpdateTime(order));
            foreach (var target in targets)
                await SendOrderAsync(order, target, false,
                    cancellationToken);
        }
    }

    private async ValueTask OnSocketExecutionsAsync(
        ZoomexExecution[] executions, long timestamp,
        CancellationToken cancellationToken)
    {
        _ = timestamp;
        foreach (var execution in executions ?? [])
        {
            if (execution?.ExecutionId.IsEmpty() != false)
                continue;
            var targets = GetOrderTargets(execution.Category,
                execution.Symbol, execution.OrderId,
                execution.OrderLinkId, execution.Side,
                execution.ExecutionTime.ToLong() ?? 0);
            foreach (var target in targets)
                await SendExecutionAsync(execution, target,
                    cancellationToken);
        }
    }

    private async ValueTask OnSocketPositionsAsync(
        ZoomexPosition[] positions, long timestamp,
        CancellationToken cancellationToken)
    {
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _portfolioSubscriptions];
        foreach (var position in positions ?? [])
            foreach (var target in targets)
                await SendPositionAsync(position, target, false,
                    GetTime(timestamp), cancellationToken);
    }

    private async ValueTask OnSocketWalletsAsync(
        ZoomexWalletAccount[] accounts, long timestamp,
        CancellationToken cancellationToken)
    {
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _portfolioSubscriptions];
        foreach (var account in accounts ?? [])
        {
            if (account.AccountType != AccountType.ToNative())
                continue;
            foreach (var target in targets)
                await SendWalletAsync(account, target, false,
                    GetTime(timestamp), cancellationToken);
        }
    }

    private async ValueTask SendWalletAsync(ZoomexWalletAccount account,
        long target, bool isForced, DateTime serverTime,
        CancellationToken cancellationToken)
    {
        foreach (var coin in account?.Coins ?? [])
        {
            if (coin?.Coin.IsEmpty() != false)
                continue;
            var current = coin.Equity.ToDecimal() ??
                coin.WalletBalance.ToDecimal() ?? 0m;
            var available = coin.AvailableToWithdraw.ToDecimal();
            var margins = (coin.TotalOrderInitialMargin.ToDecimal() ?? 0m) +
                (coin.TotalPositionInitialMargin.ToDecimal() ?? 0m);
            var blocked = available is decimal free
                ? (current - free).Max(margins).Max(0m)
                : margins.Max(0m);
            var unrealized = coin.UnrealizedPnl.ToDecimal();
            var realized = coin.RealizedPnl.ToDecimal();
            var fingerprint = new BalanceFingerprint(current, blocked,
                unrealized, realized);
            var key = $"{target}:{coin.Coin.ToUpperInvariant()}";
            using (_sync.EnterScope())
            {
                if (!isForced && _balanceFingerprints.TryGetValue(key,
                    out var previous) && previous == fingerprint)
                    continue;
                _balanceFingerprints[key] = fingerprint;
            }
            await SendOutMessageAsync(new PositionChangeMessage
            {
                PortfolioName = GetPortfolioName(),
                SecurityId = new()
                {
                    SecurityCode = coin.Coin.ToUpperInvariant(),
                    BoardCode = BoardCodes.Zoomex,
                },
                ServerTime = serverTime,
                OriginalTransactionId = target,
            }
            .TryAdd(PositionChangeTypes.CurrentValue, current, true)
            .TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
            .TryAdd(PositionChangeTypes.UnrealizedPnL, unrealized, true)
            .TryAdd(PositionChangeTypes.RealizedPnL, realized, true),
                cancellationToken);
        }
    }

    private ValueTask SendPositionAsync(ZoomexPosition position, long target,
        bool isForced, DateTime serverTime,
        CancellationToken cancellationToken)
    {
        if (position?.Symbol.IsEmpty() != false ||
            position.Side.IsEmpty() || position.Side.EqualsIgnoreCase("None"))
            return default;
        var side = position.Side.ToStockSharp();
        var current = position.Size.ToDecimal()?.Abs() ?? 0m;
        var average = (position.AveragePrice.IsEmpty()
            ? position.EntryPrice
            : position.AveragePrice).ToDecimal();
        var mark = position.MarkPrice.ToDecimal();
        var unrealized = position.UnrealizedPnl.ToDecimal();
        var liquidation = position.LiquidationPrice.ToDecimal();
        var fingerprint = new PositionFingerprint(current, average, mark,
            unrealized, liquidation);
        var key = $"{target}:{position.Category}:{position.Symbol}:" +
            position.PositionIndex.ToString(CultureInfo.InvariantCulture);
        using (_sync.EnterScope())
        {
            if (!isForced && _positionFingerprints.TryGetValue(key,
                out var previous) && previous == fingerprint)
                return default;
            _positionFingerprints[key] = fingerprint;
        }
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = GetPortfolioName(),
            SecurityId = position.Symbol.ToStockSharp(position.Category),
            Side = side,
            ServerTime = serverTime,
            OriginalTransactionId = target,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, current, true)
        .TryAdd(PositionChangeTypes.AveragePrice, average, true)
        .TryAdd(PositionChangeTypes.CurrentPrice, mark, true)
        .TryAdd(PositionChangeTypes.UnrealizedPnL, unrealized, true)
        .TryAdd(PositionChangeTypes.RealizedPnL,
            position.RealizedPnl.ToDecimal(), true)
        .TryAdd(PositionChangeTypes.LiquidationPrice, liquidation, true)
        .TryAdd(PositionChangeTypes.Leverage,
            position.Leverage.ToDecimal(), true), cancellationToken);
    }

    private ValueTask SendOrderAsync(ZoomexOrder order, long target,
        bool isForced, CancellationToken cancellationToken)
    {
        if (order?.Symbol.IsEmpty() != false || order.OrderId.IsEmpty())
            return default;
        var tracked = GetTrackedOrder(order.OrderId) ??
            GetTrackedOrder(order.OrderLinkId);
        var total = order.Quantity.ToDecimal() ?? tracked?.Volume;
        var filled = order.ExecutedQuantity.ToDecimal() ?? 0m;
        var balance = order.LeavesQuantity.ToDecimal() ??
            (total is decimal volume ? (volume - filled).Max(0m) : null);
        var state = order.Status.ToStockSharpOrderState();
        if (state == OrderStates.None && tracked is not null)
            state = tracked.State;
        if (state == OrderStates.Done)
            balance = 0m;
        var updateTime = order.UpdatedTime.ToLong() ??
            order.CreatedTime.ToLong() ?? 0;
        var fingerprint = new OrderFingerprint(order.Status, filled, balance,
            order.Price.ToDecimal(), updateTime);
        var key = $"{target}:{order.OrderId}";
        using (_sync.EnterScope())
        {
            if (!isForced && _orderFingerprints.TryGetValue(key,
                out var previous) && previous == fingerprint)
                return default;
            _orderFingerprints[key] = fingerprint;
            if (tracked is not null)
                tracked.State = state;
        }
        var side = order.Side.ToStockSharp();
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.Symbol.ToStockSharp(order.Category),
            ServerTime = updateTime > 0
                ? updateTime.ToUtcTime()
                : CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = side,
            OrderVolume = total,
            Balance = balance,
            OrderPrice = order.Price.ToDecimal() ?? tracked?.Price ?? 0m,
            AveragePrice = filled > 0
                ? order.AveragePrice.ToDecimal()
                : null,
            OrderType = order.OrderType.ToStockSharpOrderType(
                order.StopOrderType),
            OrderState = state,
            OrderId = order.OrderId.ToLong(),
            OrderStringId = order.OrderId.ToLong() is null
                ? order.OrderId
                : null,
            UserOrderId = order.OrderLinkId,
            TransactionId = tracked?.TransactionId ??
                ParseTransactionId(order.OrderLinkId),
            OriginalTransactionId = target,
            TimeInForce = order.TimeInForce.ToStockSharpTimeInForce(),
            PostOnly = order.TimeInForce.EqualsIgnoreCase("PostOnly"),
            PositionEffect = order.IsReduceOnly || order.IsCloseOnTrigger
                ? OrderPositionEffects.CloseOnly
                : null,
            Condition = tracked?.Condition,
        }, cancellationToken);
    }

    private ValueTask SendExecutionAsync(ZoomexExecution execution,
        long target, CancellationToken cancellationToken)
    {
        if (execution?.ExecutionId.IsEmpty() != false ||
            execution.Symbol.IsEmpty())
            return default;
        using (_sync.EnterScope())
            if (!TryRemember(_seenExecutions, _executionDeliveryOrder,
                new(target, execution.ExecutionId)))
                return default;
        var tracked = GetTrackedOrder(execution.OrderId) ??
            GetTrackedOrder(execution.OrderLinkId);
        ZoomexProduct product;
        using (_sync.EnterScope())
            _products.TryGetValue(new(execution.Category,
                execution.Symbol.NormalizeSymbol()), out product);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = execution.Symbol.ToStockSharp(execution.Category),
            ServerTime = execution.ExecutionTime.ToUtcTime() ?? CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = execution.Side.ToStockSharp(),
            OrderId = execution.OrderId.ToLong(),
            OrderStringId = execution.OrderId.ToLong() is null
                ? execution.OrderId
                : null,
            TradeStringId = execution.ExecutionId,
            TradePrice = execution.ExecutionPrice.ToDecimal(),
            TradeVolume = execution.ExecutionQuantity.ToDecimal(),
            Commission = execution.ExecutionFee.ToDecimal(),
            CommissionCurrency = product?.SettleCoin.IsEmpty() == false
                ? product.SettleCoin
                : product?.QuoteCoin,
            TransactionId = tracked?.TransactionId ??
                ParseTransactionId(execution.OrderLinkId),
            OriginalTransactionId = target,
        }, cancellationToken);
    }

    private ValueTask SendOrderAcknowledgementAsync(TrackedOrder tracked,
        long target, OrderStates state,
        CancellationToken cancellationToken)
    {
        tracked.State = state;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = tracked.Symbol.ToStockSharp(tracked.Category),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = tracked.Side,
            OrderVolume = tracked.Volume,
            Balance = state == OrderStates.Done ? 0m : tracked.Volume,
            OrderPrice = tracked.Price,
            OrderType = tracked.OrderType,
            OrderState = state,
            OrderId = tracked.OrderId.ToLong(),
            OrderStringId = tracked.OrderId.ToLong() is null
                ? tracked.OrderId
                : null,
            UserOrderId = tracked.OrderLinkId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = target,
            Condition = tracked.Condition,
        }, cancellationToken);
    }

    private async ValueTask<ZoomexPosition[]> LoadPositionsAsync(
        ZoomexCategories category, string symbol, string settleCoin,
        CancellationToken cancellationToken)
    {
        var result = new List<ZoomexPosition>();
        var cursor = default(string);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await RestClient.GetPositionsAsync(category, symbol,
                settleCoin, cursor, 200, cancellationToken);
            foreach (var item in page?.Items ?? [])
            {
                item.Category = category;
                result.Add(item);
            }
            var next = page?.NextPageCursor;
            if (next.IsEmpty() || !seen.Add(next))
                break;
            cursor = next;
        }
        return [.. result];
    }

    private async ValueTask<ZoomexOrder[]> LoadOpenOrdersAsync(
        ZoomexCategories category, string symbol, string settleCoin,
        string orderId, string orderLinkId,
        CancellationToken cancellationToken)
    {
        var result = new List<ZoomexOrder>();
        var cursor = default(string);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await RestClient.GetOpenOrdersAsync(category, symbol,
                settleCoin, orderId, orderLinkId, cursor, 50,
                cancellationToken);
            result.AddRange((page?.Items ?? []).Where(
                static value => value is not null).Select(value =>
                value.ToOrder(category)));
            var next = page?.NextPageCursor;
            if (next.IsEmpty() || !seen.Add(next))
                break;
            cursor = next;
        }
        return [.. result];
    }

    private async ValueTask<ZoomexOrder[]> LoadOrderHistoryAsync(
        ZoomexCategories category, OrderSubscription subscription,
        CancellationToken cancellationToken)
    {
        var result = new List<ZoomexOrder>();
        await ForEachHistoryWindowAsync(subscription,
            async (from, to, token) =>
            {
                var cursor = default(string);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (result.Count < subscription.Maximum)
                {
                    var page = await RestClient.GetOrderHistoryAsync(
                        category, subscription.Symbol, subscription.OrderId,
                        subscription.OrderLinkId, from, to, cursor, 50,
                        token);
                    foreach (var item in page?.Items ?? [])
                    {
                        item.Category = category;
                        if (MatchesOrder(subscription, item))
                            result.Add(item);
                    }
                    var next = page?.NextPageCursor;
                    if (next.IsEmpty() || !seen.Add(next))
                        break;
                    cursor = next;
                }
            }, cancellationToken);
        return [.. result.Where(static order => order is not null &&
                !order.OrderId.IsEmpty())
            .GroupBy(static order => order.OrderId,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(
                GetOrderUpdateTime).First())
            .OrderBy(GetOrderUpdateTime)
            .TakeLast(subscription.Maximum)];
    }

    private async ValueTask<ZoomexExecution[]> LoadExecutionsAsync(
        ZoomexCategories category, OrderSubscription subscription,
        CancellationToken cancellationToken)
    {
        var result = new List<ZoomexExecution>();
        await ForEachHistoryWindowAsync(subscription,
            async (from, to, token) =>
            {
                var cursor = default(string);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (result.Count < subscription.Maximum)
                {
                    var page = await RestClient.GetExecutionsAsync(category,
                        subscription.Symbol, subscription.OrderId, from, to,
                        cursor, 100, token);
                    foreach (var item in page?.Items ?? [])
                    {
                        item.Category = category;
                        if (MatchesExecution(subscription, item))
                            result.Add(item);
                    }
                    var next = page?.NextPageCursor;
                    if (next.IsEmpty() || !seen.Add(next))
                        break;
                    cursor = next;
                }
            }, cancellationToken);
        return [.. result.Where(static value => value is not null &&
                !value.ExecutionId.IsEmpty())
            .GroupBy(static value => value.ExecutionId,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static value => value.ExecutionTime.ToLong())
            .TakeLast(subscription.Maximum)];
    }

    private static async ValueTask ForEachHistoryWindowAsync(
        OrderSubscription subscription,
        Func<DateTime, DateTime, CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var to = (subscription.To ?? now).ToUniversalTime().Min(now);
        var from = (subscription.From ?? to - TimeSpan.FromDays(7))
            .ToUniversalTime().Max(now - TimeSpan.FromDays(730));
        if (from > to)
            return;
        var cursor = to;
        while (cursor >= from)
        {
            var start = (cursor - TimeSpan.FromDays(7) +
                TimeSpan.FromMilliseconds(1)).Max(from);
            await action(start, cursor, cancellationToken);
            if (start <= from)
                break;
            cursor = start - TimeSpan.FromMilliseconds(1);
        }
    }

    private async ValueTask ClosePositionsAsync(ZoomexProduct product,
        long target, CancellationToken cancellationToken)
    {
        var categories = product is null
            ? GetEnabledCategories().Where(
                static category => category != ZoomexCategories.Spot).ToArray()
            : product.Category == ZoomexCategories.Spot
                ? []
                : [product.Category];
        foreach (var category in categories)
            foreach (var scope in GetScopes(category, product?.Symbol))
                foreach (var position in await LoadPositionsAsync(category,
                    scope.Symbol, scope.SettleCoin, cancellationToken))
                {
                    var volume = position.Size.ToDecimal()?.Abs() ?? 0m;
                    if (volume <= 0 || position.Side.IsEmpty() ||
                        position.Side.EqualsIgnoreCase("None"))
                        continue;
                    var linkId = CreateOrderLinkId(target, null);
                    var result = await RestClient.PlaceOrderAsync(new()
                    {
                        Category = category,
                        Symbol = position.Symbol,
                        Side = position.Side.ToStockSharp() == Sides.Buy
                            ? ZoomexSides.Sell
                            : ZoomexSides.Buy,
                        OrderType = ZoomexOrderTypes.Market,
                        Quantity = volume.ToNative(),
                        PositionIndex = position.PositionIndex,
                        OrderLinkId = linkId,
                        IsReduceOnly = true,
                    }, cancellationToken);
                    if (result?.OrderId.IsEmpty() != false)
                        continue;
                    var tracked = new TrackedOrder
                    {
                        TransactionId = target,
                        Category = category,
                        Symbol = position.Symbol,
                        OrderId = result.OrderId,
                        OrderLinkId = result.OrderLinkId.IsEmpty()
                            ? linkId
                            : result.OrderLinkId,
                        Side = position.Side.ToStockSharp() == Sides.Buy
                            ? Sides.Sell
                            : Sides.Buy,
                        OrderType = OrderTypes.Market,
                        Volume = volume,
                        State = OrderStates.Active,
                    };
                    TrackOrder(tracked);
                    await SendOrderAcknowledgementAsync(tracked, target,
                        OrderStates.Active, cancellationToken);
                }
    }

    private (string Symbol, string SettleCoin)[] GetScopes(
        ZoomexCategories category, string symbol)
    {
        if (!symbol.IsEmpty())
            return [(symbol.NormalizeSymbol(), null)];
        if (category == ZoomexCategories.Spot)
            return [(null, null)];
        using (_sync.EnterScope())
            return [.. _products.Values.Where(product =>
                    product.Category == category)
                .Select(product => product.SettleCoin.IsEmpty()
                    ? product.QuoteCoin
                    : product.SettleCoin)
                .Where(static value => !value.IsEmpty())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static value => ((string)null,
                    value.ToUpperInvariant()))];
    }

    private ZoomexCategories[] GetEnabledCategories()
        => [.. Sections.Select(static section => section.ToNative())];

    private long[] GetOrderTargets(ZoomexCategories category, string symbol,
        string orderId, string orderLinkId, ZoomexSides side, long timestamp)
    {
        var targets = new HashSet<long>();
        using (_sync.EnterScope())
        {
            var tracked = GetTrackedOrderUnsafe(orderId) ??
                GetTrackedOrderUnsafe(orderLinkId);
            if (tracked is not null)
                targets.Add(tracked.TransactionId);
            foreach (var pair in _orderSubscriptions)
            {
                var subscription = pair.Value;
                if (!subscription.Categories.Contains(category) ||
                    !subscription.Symbol.IsEmpty() &&
                        !subscription.Symbol.EqualsIgnoreCase(symbol) ||
                    !subscription.OrderId.IsEmpty() &&
                        !subscription.OrderId.EqualsIgnoreCase(orderId) ||
                    !subscription.OrderLinkId.IsEmpty() &&
                        !subscription.OrderLinkId.EqualsIgnoreCase(
                            orderLinkId) ||
                    subscription.Side is Sides expected &&
                        side.ToStockSharp() != expected ||
                    !IsInRange(timestamp, subscription.From,
                        subscription.To))
                    continue;
                targets.Add(pair.Key);
            }
        }
        return [.. targets];
    }

    private static bool MatchesOrder(OrderSubscription subscription,
        ZoomexOrder order)
    {
        if (order is null)
            return false;
        if (!subscription.Symbol.IsEmpty() &&
            !subscription.Symbol.EqualsIgnoreCase(order.Symbol))
            return false;
        if (!subscription.OrderId.IsEmpty() &&
            !subscription.OrderId.EqualsIgnoreCase(order.OrderId))
            return false;
        if (!subscription.OrderLinkId.IsEmpty() &&
            !subscription.OrderLinkId.EqualsIgnoreCase(order.OrderLinkId))
            return false;
        if (subscription.Side is Sides side &&
            order.Side.ToStockSharp() != side)
            return false;
        var time = GetOrderUpdateTime(order);
        return IsInRange(time, subscription.From, subscription.To);
    }

    private static bool MatchesExecution(OrderSubscription subscription,
        ZoomexExecution execution)
    {
        if (execution is null)
            return false;
        if (!subscription.Symbol.IsEmpty() &&
            !subscription.Symbol.EqualsIgnoreCase(execution.Symbol))
            return false;
        if (!subscription.OrderId.IsEmpty() &&
            !subscription.OrderId.EqualsIgnoreCase(execution.OrderId))
            return false;
        if (!subscription.OrderLinkId.IsEmpty() &&
            !subscription.OrderLinkId.EqualsIgnoreCase(
                execution.OrderLinkId))
            return false;
        if (subscription.Side is Sides side &&
            execution.Side.ToStockSharp() != side)
            return false;
        return IsInRange(execution.ExecutionTime.ToLong() ?? 0,
            subscription.From, subscription.To);
    }

    private static bool MatchesCancel(OrderGroupCancelMessage message,
        ZoomexOrder order)
    {
        if (message.Side is Sides side &&
            order.Side.ToStockSharp() != side)
            return false;
        var isStop = order.OrderType.ToStockSharpOrderType(
            order.StopOrderType) == OrderTypes.Conditional;
        return message.IsStop is null || message.IsStop == isStop;
    }

    private static bool IsInRange(long timestamp, DateTime? from,
        DateTime? to)
    {
        if (timestamp <= 0)
            return true;
        var time = timestamp.ToUtcTime();
        return (from is null || time >= from.Value.ToUniversalTime()) &&
            (to is null || time <= to.Value.ToUniversalTime());
    }

    private void TrackOrder(TrackedOrder order)
    {
        using (_sync.EnterScope())
        {
            if (!order.OrderId.IsEmpty())
                _trackedOrders[order.OrderId] = order;
            if (!order.OrderLinkId.IsEmpty())
                _trackedOrders[order.OrderLinkId] = order;
        }
    }

    private TrackedOrder GetTrackedOrder(string identity)
    {
        using (_sync.EnterScope())
            return GetTrackedOrderUnsafe(identity);
    }

    private TrackedOrder GetTrackedOrderUnsafe(string identity)
        => !identity.IsEmpty() && _trackedOrders.TryGetValue(identity,
            out var order) ? order : null;

    private void RemoveOrderSubscription(long target)
    {
        using (_sync.EnterScope())
        {
            _orderSubscriptions.Remove(target);
            RemoveFingerprintPrefix(_orderFingerprints, target);
            _seenExecutions.RemoveWhere(key =>
                key.SubscriptionId == target);
        }
    }

    private static void RemoveFingerprintPrefix<TValue>(
        IDictionary<string, TValue> values, long target)
    {
        var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
        foreach (var key in values.Keys.Where(key =>
            key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            values.Remove(key);
    }

    private async ValueTask CompleteOrderStatusAsync(
        OrderStatusMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }

    private static string CreateOrderLinkId(long transactionId,
        string userOrderId)
    {
        if (!userOrderId.IsEmpty() && userOrderId.Length <= 36 &&
            userOrderId.All(static ch => char.IsLetterOrDigit(ch) ||
                ch is '-' or '_'))
            return userOrderId;
        return $"ss{transactionId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static long ParseTransactionId(string orderLinkId)
        => orderLinkId?.StartsWith("ss", StringComparison.OrdinalIgnoreCase)
            == true && long.TryParse(orderLinkId.AsSpan(2),
                NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : 0;

    private static void ResolveOrderIdentity(long? numericId,
        string stringId, string userOrderId, out string orderId,
        out string orderLinkId, bool isRequired)
    {
        orderId = numericId?.ToString(CultureInfo.InvariantCulture);
        if (orderId.IsEmpty())
            orderId = stringId?.Trim();
        orderLinkId = orderId.IsEmpty() ? userOrderId?.Trim() : null;
        if (!orderId.IsEmpty() || !orderLinkId.IsEmpty() || !isRequired)
            return;
        throw new InvalidOperationException(
            "Zoomex order operation requires an order ID or order link ID.");
    }

    private static long GetOrderUpdateTime(ZoomexOrder order)
        => order?.UpdatedTime.ToLong() ?? order?.CreatedTime.ToLong() ?? 0;
}
