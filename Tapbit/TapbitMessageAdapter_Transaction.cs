namespace StockSharp.Tapbit;

public partial class TapbitMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var product = GetProduct(regMsg.SecurityId);
        EnsureSpotTrading(product);
        if (regMsg.OrderType is not (null or OrderTypes.Limit))
            throw new NotSupportedException(
                "Tapbit Spot V2 documents limit orders only.");
        if (regMsg.Condition is not null)
            throw new NotSupportedException(
                "Tapbit Spot V2 does not document conditional orders.");
        if (regMsg.PostOnly == true)
            throw new NotSupportedException(
                "Tapbit Spot V2 does not document post-only orders.");
        if (regMsg.TimeInForce is not (null or TimeInForce.PutInQueue))
            throw new NotSupportedException(
                "Tapbit Spot V2 does not expose a time-in-force field.");
        if (!regMsg.UserOrderId.IsEmpty())
            throw new NotSupportedException(
                "Tapbit Spot V2 does not expose a client-order ID field.");
        var volume = regMsg.Volume.Abs();
        if (volume <= 0 || regMsg.Price <= 0)
            throw new InvalidOperationException(
                "Tapbit limit-order price and volume must be positive.");

        var result = await RestClient.PlaceOrderAsync(new()
        {
            Symbol = product.Symbol,
            Direction = regMsg.Side.ToNative(),
            Price = regMsg.Price.ToNative(),
            Quantity = volume.ToNative(),
        }, cancellationToken);
        if (result?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                "Tapbit returned no order identifier.");

        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            OrderId = result.OrderId,
            Symbol = product.Symbol,
            Side = regMsg.Side,
            Volume = volume,
            Price = regMsg.Price,
            State = OrderStates.Active,
        };
        using (_sync.EnterScope())
        {
            _trackedOrders[result.OrderId] = tracked;
            _transactionSymbols.Add(product.Symbol);
        }
        await SendTrackedOrderAsync(tracked, regMsg.TransactionId,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
    {
        _ = replaceMsg;
        _ = cancellationToken;
        throw new NotSupportedException(
            "Tapbit Spot V2 does not document an order-replace operation.");
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var product = GetProduct(cancelMsg.SecurityId);
        EnsureSpotTrading(product);
        var orderId = ResolveOrderId(cancelMsg.OrderId,
            cancelMsg.OrderStringId);
        var result = await RestClient.CancelOrderAsync(orderId,
            cancellationToken);
        if (result?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                "Tapbit returned no canceled order identifier.");
        TrackedOrder tracked;
        using (_sync.EnterScope())
        {
            _trackedOrders.TryGetValue(orderId, out tracked);
            if (tracked is not null)
                tracked.State = OrderStates.Done;
        }
        if (tracked is not null)
            await SendTrackedOrderAsync(tracked, cancelMsg.TransactionId,
                cancellationToken);
        else
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                SecurityId = product.Symbol.ToStockSharp(
                    product.ProductType),
                ServerTime = CurrentTime,
                PortfolioName = GetPortfolioName(),
                OrderId = result.OrderId.ToLong(),
                OrderStringId = result.OrderId.ToLong() is null
                    ? result.OrderId
                    : null,
                OrderState = OrderStates.Done,
                OriginalTransactionId = cancelMsg.TransactionId,
            }, cancellationToken);
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
                "Tapbit Spot has no derivative positions to close.");

        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!cancelMsg.SecurityId.SecurityCode.IsEmpty())
        {
            var product = GetProduct(cancelMsg.SecurityId);
            EnsureSpotTrading(product);
            symbols.Add(product.Symbol);
        }
        else
        {
            using (_sync.EnterScope())
            {
                symbols.AddRange(_transactionSymbols);
                symbols.AddRange(_orderSubscriptions.Values
                    .Select(static value => value.Symbol)
                    .Where(static value => !value.IsEmpty()));
            }
        }
        if (symbols.Count == 0)
            throw new InvalidOperationException(
                "Tapbit batch cancellation requires a Spot security because " +
                "the official open-order endpoint requires instrument_id.");

        foreach (var symbol in symbols)
        {
            var orders = await LoadOrderListAsync(symbol, true, 500,
                cancellationToken);
            var ids = orders.Where(order => cancelMsg.Side is null ||
                    order.Direction.ToStockSharp() == cancelMsg.Side)
                .Where(order => cancelMsg.IsStop is not true)
                .Select(static order => order.OrderId)
                .Where(static id => !id.IsEmpty()).Distinct(
                    StringComparer.OrdinalIgnoreCase).ToArray();
            for (var index = 0; index < ids.Length; index += 20)
            {
                var batch = ids.Skip(index).Take(20).ToArray();
                var results = await RestClient.CancelOrdersAsync(batch,
                    cancellationToken) ?? [];
                foreach (var result in results)
                {
                    if (result.Code is not (null or "" or "200"))
                        throw new InvalidOperationException(
                            $"Tapbit failed to cancel order " +
                            $"'{result.OrderId}' ({result.Code}): " +
                            (result.Message ?? "request rejected"));
                    TrackedOrder tracked;
                    using (_sync.EnterScope())
                    {
                        _trackedOrders.TryGetValue(result.OrderId,
                            out tracked);
                        if (tracked is not null)
                            tracked.State = OrderStates.Done;
                    }
                    if (tracked is not null)
                        await SendTrackedOrderAsync(tracked,
                            cancelMsg.TransactionId, cancellationToken);
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
            }
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = GetPortfolioName(),
            BoardCode = BoardCodes.Tapbit,
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
        if (!statusMsg.UserId.IsEmpty())
            throw new NotSupportedException(
                "Tapbit Spot V2 does not expose a user-id order filter.");
        if (statusMsg.SecurityIds.Length > 0)
            throw new NotSupportedException(
                "Tapbit Spot V2 order lists accept one instrument_id per " +
                "request.");
        var orderId = statusMsg.OrderId?.ToString(
            CultureInfo.InvariantCulture);
        if (!statusMsg.OrderStringId.IsEmpty())
            orderId = statusMsg.OrderStringId.Trim();
        var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetProduct(statusMsg.SecurityId).Symbol;
        if (!symbol.IsEmpty())
            EnsureSpotTrading(GetProduct(statusMsg.SecurityId));
        if (orderId.IsEmpty() && symbol.IsEmpty())
        {
            using (_sync.EnterScope())
                symbol = _transactionSymbols.Count == 1
                    ? _transactionSymbols.First()
                    : null;
            if (symbol.IsEmpty())
                throw new InvalidOperationException(
                    "Tapbit order-list requests require a Spot security " +
                    "because instrument_id is mandatory.");
        }
        var subscription = new OrderSubscription
        {
            Symbol = symbol,
            OrderId = orderId,
            Side = statusMsg.Side,
            Volume = statusMsg.Volume,
            States = statusMsg.States,
            From = statusMsg.From?.ToUniversalTime(),
            To = statusMsg.To?.ToUniversalTime(),
            Skip = Math.Max(0, statusMsg.Skip ?? 0).Min(int.MaxValue).To<int>(),
            Maximum = (statusMsg.Count ?? 1000).Min(5000).Max(1).To<int>(),
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

    private async ValueTask PollPrivateAsync(
        CancellationToken cancellationToken)
    {
        long[] portfolioTargets;
        KeyValuePair<long, OrderSubscription>[] orderTargets;
        TrackedOrder[] tracked;
        using (_sync.EnterScope())
        {
            portfolioTargets = [.. _portfolioSubscriptions];
            orderTargets = [.. _orderSubscriptions];
            tracked = [.. _trackedOrders.Values.Where(static order =>
                order.State == OrderStates.Active)];
        }
        if (portfolioTargets.Length > 0)
        {
            var balances = await RestClient.GetBalancesAsync(
                cancellationToken) ?? [];
            foreach (var target in portfolioTargets)
                await SendPortfolioSnapshotAsync(target, false, balances,
                    cancellationToken);
        }
        foreach (var target in orderTargets)
            await SendOrderSnapshotAsync(target.Value, target.Key, false,
                cancellationToken);

        var subscribedOrderIds = new HashSet<string>(orderTargets
            .Select(static pair => pair.Value.OrderId)
            .Where(static value => !value.IsEmpty()),
            StringComparer.OrdinalIgnoreCase);
        foreach (var order in tracked.Where(order =>
            !subscribedOrderIds.Contains(order.OrderId)))
        {
            var current = await RestClient.GetOrderAsync(order.OrderId,
                cancellationToken);
            if (current is not null)
                await SendOrderAsync(current, order.TransactionId, false,
                    cancellationToken);
        }
    }

    private async ValueTask SendPortfolioSnapshotAsync(long target,
        bool isForced, CancellationToken cancellationToken)
        => await SendPortfolioSnapshotAsync(target, isForced,
            await RestClient.GetBalancesAsync(cancellationToken) ?? [],
            cancellationToken);

    private async ValueTask SendPortfolioSnapshotAsync(long target,
        bool isForced, TapbitBalance[] balances,
        CancellationToken cancellationToken)
    {
        foreach (var balance in balances)
            await SendBalanceAsync(balance, target, isForced,
                cancellationToken);
    }

    private async ValueTask SendOrderSnapshotAsync(
        OrderSubscription subscription, long target, bool isForced,
        CancellationToken cancellationToken)
    {
        if (!subscription.OrderId.IsEmpty())
        {
            var order = await RestClient.GetOrderAsync(subscription.OrderId,
                cancellationToken);
            if (order is not null && Matches(subscription, order))
                await SendOrderAsync(order, target, isForced,
                    cancellationToken);
            return;
        }
        foreach (var order in await LoadOrdersAsync(subscription,
            cancellationToken))
            await SendOrderAsync(order, target, isForced,
                cancellationToken);
    }

    private async ValueTask<TapbitOrder[]> LoadOrdersAsync(
        OrderSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (subscription.Symbol.IsEmpty())
            return [];
        var open = await LoadOrderListAsync(subscription.Symbol, true,
            subscription.Maximum, cancellationToken);
        var closed = await LoadOrderListAsync(subscription.Symbol, false,
            subscription.Maximum, cancellationToken);
        return [.. open.Concat(closed)
            .Where(order => Matches(subscription, order))
            .GroupBy(static order => order.OrderId,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static order => order.OrderTime)
            .Skip(subscription.Skip)
            .TakeLast(subscription.Maximum)];
    }

    private async ValueTask<TapbitOrder[]> LoadOrderListAsync(string symbol,
        bool isOpen, int maximum, CancellationToken cancellationToken)
    {
        maximum = maximum.Min(5000).Max(1);
        var result = new List<TapbitOrder>();
        string nextOrderId = null;
        for (var page = 0; result.Count < maximum && page < 250; page++)
        {
            var items = isOpen
                ? await RestClient.GetOpenOrdersAsync(symbol, nextOrderId,
                    cancellationToken)
                : await RestClient.GetClosedOrdersAsync(symbol, nextOrderId,
                    cancellationToken);
            if (items is not { Length: > 0 })
                break;
            result.AddRange(items.Where(static item => item is not null &&
                !item.OrderId.IsEmpty()));
            if (items.Length < 20)
                break;
            var last = items[^1]?.OrderId.ToLong();
            if (last is null || last <= 0)
                break;
            var next = (last.Value - 1).ToString(
                CultureInfo.InvariantCulture);
            if (next.EqualsIgnoreCase(nextOrderId))
                break;
            nextOrderId = next;
        }
        return [.. result.Take(maximum)];
    }

    private ValueTask SendBalanceAsync(TapbitBalance balance, long target,
        bool isForced, CancellationToken cancellationToken)
    {
        if (balance?.Asset.IsEmpty() != false)
            return default;
        var available = balance.Available.ToDecimal() ?? 0m;
        var blocked = balance.Frozen.ToDecimal() ?? 0m;
        var reportedTotal = balance.Total.ToDecimal() ?? 0m;
        var current = reportedTotal.Max(available + blocked);
        var fingerprint = new BalanceFingerprint(current, available, blocked);
        var key = $"{target}:{balance.Asset.ToUpperInvariant()}";
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
            SecurityId = new()
            {
                SecurityCode = balance.Asset.ToUpperInvariant(),
                BoardCode = BoardCodes.Tapbit,
            },
            ServerTime = CurrentTime,
            OriginalTransactionId = target,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, current, true)
        .TryAdd(PositionChangeTypes.BlockedValue, blocked, true),
            cancellationToken);
    }

    private ValueTask SendOrderAsync(TapbitOrder order, long target,
        bool isForced, CancellationToken cancellationToken)
    {
        if (order?.OrderId.IsEmpty() != false)
            return default;
        TrackedOrder tracked;
        using (_sync.EnterScope())
            _trackedOrders.TryGetValue(order.OrderId, out tracked);
        var symbol = order.Symbol.IsEmpty() ? tracked?.Symbol : order.Symbol;
        if (symbol.IsEmpty())
            return default;
        var total = order.Quantity.ToDecimal() ?? tracked?.Volume;
        var filled = order.FilledQuantity.ToDecimal() ?? 0m;
        var average = order.AveragePrice.ToDecimal();
        if (average is null && filled > 0 &&
            order.FilledAmount.ToDecimal() is decimal filledAmount)
            average = filledAmount / filled;
        var state = order.Status.ToStockSharp();
        var fingerprint = new OrderFingerprint(order.Status, filled, average,
            order.OrderTime);
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
        var side = order.Direction.ToStockSharp();
        decimal? balance = total is decimal volume
            ? (volume - filled).Max(0m)
            : null;
        if (state == OrderStates.Done)
            balance = 0m;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = symbol.NormalizeTapbitSymbol().ToStockSharp(
                TapbitProductTypes.Spot),
            ServerTime = order.OrderTime > 0
                ? order.OrderTime.ToUtcTime()
                : CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = side,
            OrderVolume = total,
            Balance = balance,
            OrderPrice = order.Price.ToDecimal() ?? tracked?.Price ?? 0m,
            AveragePrice = average,
            OrderType = OrderTypes.Limit,
            OrderState = state,
            OrderId = order.OrderId.ToLong(),
            OrderStringId = order.OrderId.ToLong() is null
                ? order.OrderId
                : null,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = target,
            Commission = order.Fee.ToDecimal(),
            CommissionCurrency = order.QuoteAsset,
        }, cancellationToken);
    }

    private ValueTask SendTrackedOrderAsync(TrackedOrder order, long target,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.Symbol.ToStockSharp(TapbitProductTypes.Spot),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = order.Side,
            OrderVolume = order.Volume,
            Balance = order.State == OrderStates.Active
                ? order.Volume
                : 0m,
            OrderPrice = order.Price,
            OrderType = OrderTypes.Limit,
            OrderState = order.State,
            OrderId = order.OrderId.ToLong(),
            OrderStringId = order.OrderId.ToLong() is null
                ? order.OrderId
                : null,
            TransactionId = order.TransactionId,
            OriginalTransactionId = target,
        }, cancellationToken);

    private static bool Matches(OrderSubscription subscription,
        TapbitOrder order)
    {
        if (order is null || order.OrderId.IsEmpty())
            return false;
        if (!subscription.OrderId.IsEmpty() &&
            !subscription.OrderId.EqualsIgnoreCase(order.OrderId))
            return false;
        if (!subscription.Symbol.IsEmpty() &&
            !subscription.Symbol.EqualsIgnoreCase(order.Symbol))
            return false;
        if (subscription.Side is Sides side &&
            order.Direction.ToStockSharp() != side)
            return false;
        if (subscription.Volume is decimal volume &&
            order.Quantity.ToDecimal() != volume)
            return false;
        if (subscription.States is { Length: > 0 } states &&
            !states.Contains(order.Status.ToStockSharp()))
            return false;
        if (order.OrderTime <= 0)
            return true;
        var time = order.OrderTime.ToUtcTime();
        return (subscription.From is null || time >= subscription.From) &&
            (subscription.To is null || time <= subscription.To);
    }

    private void RemoveOrderSubscription(long target)
    {
        using (_sync.EnterScope())
        {
            _orderSubscriptions.Remove(target);
            RemoveFingerprintPrefix(_orderFingerprints, target);
        }
    }

    private async ValueTask CompleteOrderStatusAsync(
        OrderStatusMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }

    private static string ResolveOrderId(long? numericId, string stringId)
    {
        if (numericId is long id)
            return id.ToString(CultureInfo.InvariantCulture);
        stringId = stringId?.Trim();
        if (stringId.IsEmpty())
            throw new InvalidOperationException(
                "Tapbit order operation requires an order ID.");
        return stringId;
    }

    private static void EnsureSpotTrading(TapbitInstrument product)
    {
        if (product.ProductType != TapbitProductTypes.Spot)
            throw new NotSupportedException(
                "Tapbit's current public futures documentation does not " +
                "publish private trading endpoints.");
    }
}
