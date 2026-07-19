namespace StockSharp.CoinJar;

public partial class CoinJarMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var product = GetProduct(regMsg.SecurityId);
        var orderType = regMsg.OrderType ?? OrderTypes.Limit;
        if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
            OrderTypes.Conditional))
            throw new NotSupportedException(
                LocalizedStrings.OrderUnsupportedType.Put(orderType,
                    regMsg.TransactionId));
        var condition = regMsg.Condition as CoinJarOrderCondition ?? new();
        var volume = regMsg.Volume.Abs();
        if (volume <= 0)
            throw new InvalidOperationException("Order volume must be positive.");
        if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
            throw new NotSupportedException(
                "CoinJar does not document iceberg orders.");
        if (regMsg.TillDate is not null)
            throw new NotSupportedException(
                "CoinJar does not support GTD orders.");

        CoinJarOrderTypes nativeType;
        CoinJarTimeInForces nativeTimeInForce;
        decimal? validationPrice;
        switch (orderType)
        {
            case OrderTypes.Limit:
                if (regMsg.Price <= 0)
                    throw new InvalidOperationException(
                        "A positive price is required for a CoinJar limit order.");
                if (condition.TriggerPrice is not null)
                    throw new InvalidOperationException(
                        "Use the conditional order type for CoinJar stop-limit orders.");
                if (condition.IsAuctionOnly && regMsg.PostOnly == true)
                    throw new InvalidOperationException(
                        "A CoinJar order cannot be both auction-only and maker-or-cancel.");
                if (condition.IsAuctionOnly && regMsg.TimeInForce is not null and
                    not TimeInForce.PutInQueue)
                    throw new InvalidOperationException(
                        "A CoinJar auction-only order cannot use IOC or FOK.");
                nativeType = CoinJarOrderTypes.Limit;
                nativeTimeInForce = condition.IsAuctionOnly
                    ? CoinJarTimeInForces.AO
                    : (regMsg.TimeInForce ?? TimeInForce.PutInQueue).ToCoinJar(
                        regMsg.PostOnly == true);
                validationPrice = regMsg.Price;
                break;
            case OrderTypes.Market:
                if (regMsg.Price != 0)
                    throw new InvalidOperationException(
                        "CoinJar market orders do not accept a price.");
                if (regMsg.PostOnly == true || condition.IsAuctionOnly)
                    throw new InvalidOperationException(
                        "CoinJar market orders cannot be post-only or auction-only.");
                if (condition.TriggerPrice is not null)
                    throw new InvalidOperationException(
                        "CoinJar supports stop-limit orders, not stop-market orders.");
                if (regMsg.TimeInForce is not null and
                    not TimeInForce.CancelBalance)
                    throw new InvalidOperationException(
                        "CoinJar market orders use IOC time-in-force.");
                nativeType = CoinJarOrderTypes.Market;
                nativeTimeInForce = CoinJarTimeInForces.IOC;
                var ticker = await RestClient.GetTickerAsync(product.Id,
                    cancellationToken);
                validationPrice = regMsg.Side == Sides.Buy
                    ? ticker.Ask ?? ticker.Last
                    : ticker.Bid ?? ticker.Last;
                break;
            default:
                if (regMsg.Price <= 0)
                    throw new InvalidOperationException(
                        "A CoinJar stop-limit order requires a positive limit price.");
                if (condition.TriggerPrice is not > 0)
                    throw new InvalidOperationException(
                        "A CoinJar stop-limit order requires a positive trigger price.");
                if (regMsg.PostOnly == true || condition.IsAuctionOnly)
                    throw new InvalidOperationException(
                        "CoinJar stop-limit orders cannot be post-only or auction-only.");
                if (regMsg.TimeInForce is not null and
                    not TimeInForce.PutInQueue)
                    throw new InvalidOperationException(
                        "CoinJar stop-limit orders support GTC only.");
                nativeType = CoinJarOrderTypes.StopLimit;
                nativeTimeInForce = CoinJarTimeInForces.GTC;
                validationPrice = regMsg.Price;
                ValidatePrice(product, condition.TriggerPrice.Value,
                    "trigger price");
                break;
        }

        if (validationPrice is > 0)
        {
            ValidatePrice(product, validationPrice.Value, "price");
            ValidateVolume(product, validationPrice.Value, volume);
        }
        else
            ValidateVolumeSubunit(product, volume);

        var result = await RestClient.PlaceOrderAsync(new()
        {
            ProductId = product.Id,
            OrderType = nativeType,
            Side = regMsg.Side.ToCoinJar(),
            Price = nativeType is CoinJarOrderTypes.Limit or
                CoinJarOrderTypes.StopLimit ? regMsg.Price.ToCoinJarWire() : null,
            Size = volume.ToCoinJarWire(),
            TriggerPrice = nativeType == CoinJarOrderTypes.StopLimit
                ? condition.TriggerPrice?.ToCoinJarWire()
                : null,
            TimeInForce = nativeTimeInForce,
        }, cancellationToken);
        ValidateOrder(result, "accepted");
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            ExchangeOrderId = result.OrderId,
            ProductId = product.Id,
            Reference = result.Reference,
            Side = regMsg.Side,
            OrderType = orderType,
            Volume = volume,
            Price = regMsg.Price,
            IsPostOnly = regMsg.PostOnly == true,
            Condition = condition.Clone() as CoinJarOrderCondition,
        };
        TrackOrder(tracked);
        await SendOrderAsync(result, regMsg.TransactionId, true,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var orderId = ResolveOrderId(cancelMsg.OrderId,
            cancelMsg.OrderStringId, "cancellation");
        var result = await RestClient.CancelOrderAsync(orderId,
            cancellationToken);
        ValidateOrder(result, "cancelled");
        await SendOrderAsync(result, cancelMsg.TransactionId, true,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
            throw new NotSupportedException(
                "CoinJar spot cancellation cannot close positions.");
        var productId = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetProduct(cancelMsg.SecurityId).Id;
        var orders = await LoadOpenOrdersAsync(cancellationToken);
        var selected = orders.Where(order => MatchesCancellation(cancelMsg,
            productId, order)).ToArray();

        if (productId.IsEmpty() && cancelMsg.Side is null &&
            cancelMsg.IsStop is null)
        {
            var summary = await RestClient.CancelAllOrdersAsync(cancellationToken);
            var remaining = summary.ErrorCount > 0
                ? (await LoadOpenOrdersAsync(cancellationToken)).Select(static order =>
                    order.OrderId).ToHashSet()
                : [];
            if (summary.ErrorCount > 0)
                this.AddWarningLog(
                    "CoinJar cancel-all completed with {0} errors and {1} successes.",
                    summary.ErrorCount, summary.SuccessCount);
            foreach (var order in selected.Where(order =>
                !remaining.Contains(order.OrderId)))
                await SendCanceledOrderAsync(order, cancelMsg.TransactionId,
                    cancellationToken);
            return;
        }

        foreach (var order in selected)
        {
            var cancelled = await RestClient.CancelOrderAsync(order.OrderId,
                cancellationToken);
            await SendOrderAsync(cancelled, cancelMsg.TransactionId, true,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(
        PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
            cancellationToken);
        EnsurePrivateReady();
        if (!lookupMsg.IsSubscribe)
        {
            bool removed;
            using (_sync.EnterScope())
                removed = _portfolioSubscriptions.Remove(
                    lookupMsg.OriginalTransactionId);
            if (removed)
                await ReleasePrivateAsync(cancellationToken);
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = GetPortfolioName(),
            BoardCode = BoardCodes.CoinJar,
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
        try
        {
            await AcquirePrivateAsync(cancellationToken);
            await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _portfolioSubscriptions.Remove(lookupMsg.TransactionId);
            await ReleasePrivateAsync(cancellationToken);
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
            bool removed;
            using (_sync.EnterScope())
                removed = _orderSubscriptions.Remove(
                    statusMsg.OriginalTransactionId);
            if (removed)
                await ReleasePrivateAsync(cancellationToken);
            return;
        }
        if (statusMsg.Count is <= 0)
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        ValidatePortfolio(statusMsg.PortfolioName);
        var productId = statusMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetProduct(statusMsg.SecurityId).Id;
        long? orderId = null;
        if (statusMsg.HasOrderId())
            orderId = ResolveOrderId(statusMsg.OrderId,
                statusMsg.OrderStringId, "lookup");
        productId ??= orderId is long value
            ? GetTrackedOrder(value)?.ProductId
            : null;
        var subscription = new OrderSubscription
        {
            ProductId = productId,
            OrderId = orderId,
            Side = statusMsg.Side,
        };
        var maximum = (statusMsg.Count ?? 200).Min(5000).Max(1).To<int>();
        await SendOrderSnapshotAsync(statusMsg.TransactionId, subscription,
            statusMsg.From, statusMsg.To, maximum, true, cancellationToken);
        if (statusMsg.IsHistoryOnly())
        {
            await SendSubscriptionResultAsync(statusMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
                cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _orderSubscriptions[statusMsg.TransactionId] = subscription;
        try
        {
            await AcquirePrivateAsync(cancellationToken);
            await SendSubscriptionResultAsync(statusMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _orderSubscriptions.Remove(statusMsg.TransactionId);
            await ReleasePrivateAsync(cancellationToken);
            throw;
        }
    }

    private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
        bool force, CancellationToken cancellationToken)
    {
        var accounts = await RestClient.GetAccountsAsync(cancellationToken);
        foreach (var account in accounts ?? [])
            await SendBalanceAsync(account, transactionId, force,
                cancellationToken);
    }

    private async ValueTask SendOrderSnapshotAsync(long transactionId,
        OrderSubscription subscription, DateTime? from, DateTime? to, int maximum,
        bool force, CancellationToken cancellationToken)
    {
        if (subscription.OrderId is long orderId)
        {
            var order = await RestClient.GetOrderAsync(orderId, cancellationToken);
            if (MatchesOrder(subscription, from, to, order))
                await SendOrderAsync(order, transactionId, force,
                    cancellationToken);
        }
        else
        {
            var scanMaximum = !subscription.ProductId.IsEmpty() ||
                subscription.Side is not null ? 5000 : maximum;
            var orders = await LoadOrdersAsync(from, to, scanMaximum,
                cancellationToken);
            foreach (var order in orders.Where(order =>
                MatchesOrder(subscription, from, to, order)).TakeLast(maximum))
                await SendOrderAsync(order, transactionId, force,
                    cancellationToken);
        }

        var fillScanMaximum = subscription.OrderId is not null ||
            !subscription.ProductId.IsEmpty() || subscription.Side is not null
                ? 5000
                : maximum;
        var fills = await LoadFillsAsync(from, to, fillScanMaximum,
            cancellationToken);
        foreach (var fill in fills.Where(fill =>
            MatchesFill(subscription, from, to, fill)).TakeLast(maximum))
            await SendFillAsync(fill, transactionId, !force, cancellationToken);
    }

    private async ValueTask<CoinJarOrder[]> LoadOrdersAsync(DateTime? from,
        DateTime? to, int maximum, CancellationToken cancellationToken)
    {
        var upperBound = (to ?? DateTime.UtcNow).ToUniversalTime();
        var lowerBound = from?.ToUniversalTime() ??
            upperBound - TimeSpan.FromDays(7);
        var result = new Dictionary<long, CoinJarOrder>();
        string cursor = null;
        while (result.Count < maximum)
        {
            var page = await RestClient.GetOrdersAsync(true, cursor,
                cancellationToken);
            var items = page?.Items ?? [];
            if (items.Length == 0)
                break;
            foreach (var order in items.Where(order => order is not null &&
                GetOrderTime(order) >= lowerBound &&
                GetOrderTime(order) <= upperBound))
                result[order.OrderId] = order;
            var earliest = items.Where(static order => order is not null)
                .Select(GetOrderTime).DefaultIfEmpty(lowerBound).Min();
            if (earliest <= lowerBound || page.Cursor.IsEmpty() ||
                page.Cursor.EqualsIgnoreCase(cursor))
                break;
            cursor = page.Cursor;
        }
        return [.. result.Values.OrderBy(GetOrderTime).TakeLast(maximum)];
    }

    private async ValueTask<CoinJarOrder[]> LoadOpenOrdersAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, CoinJarOrder>();
        string cursor = null;
        while (true)
        {
            var page = await RestClient.GetOrdersAsync(false, cursor,
                cancellationToken);
            var items = page?.Items ?? [];
            foreach (var order in items.Where(static order => order is not null))
                result[order.OrderId] = order;
            if (items.Length == 0 || page.Cursor.IsEmpty() ||
                page.Cursor.EqualsIgnoreCase(cursor))
                break;
            cursor = page.Cursor;
        }
        return [.. result.Values];
    }

    private async ValueTask<CoinJarFill[]> LoadFillsAsync(DateTime? from,
        DateTime? to, int maximum, CancellationToken cancellationToken)
    {
        var upperBound = (to ?? DateTime.UtcNow).ToUniversalTime();
        var lowerBound = from?.ToUniversalTime() ??
            upperBound - TimeSpan.FromDays(7);
        var result = new Dictionary<long, CoinJarFill>();
        string cursor = null;
        while (result.Count < maximum)
        {
            var page = await RestClient.GetFillsAsync(cursor, cancellationToken);
            var items = page?.Items ?? [];
            if (items.Length == 0)
                break;
            foreach (var fill in items.Where(fill => fill is not null &&
                GetTime(fill.Timestamp) >= lowerBound &&
                GetTime(fill.Timestamp) <= upperBound))
                result[fill.TradeId] = fill;
            var earliest = items.Where(static fill => fill is not null)
                .Select(fill => GetTime(fill.Timestamp)).DefaultIfEmpty(lowerBound)
                .Min();
            if (earliest <= lowerBound || page.Cursor.IsEmpty() ||
                page.Cursor.EqualsIgnoreCase(cursor))
                break;
            cursor = page.Cursor;
        }
        return [.. result.Values.OrderBy(static fill => fill.Timestamp)
            .TakeLast(maximum)];
    }

    private ValueTask SendBalanceAsync(CoinJarAccount account,
        long transactionId, bool force, CancellationToken cancellationToken)
    {
        if (account?.AssetCode.IsEmpty() != false)
            return default;
        var fingerprint = new BalanceFingerprint(account.Balance,
            account.Available, account.Hold);
        var key = $"{transactionId}:{account.Number}:{account.AssetCode}";
        using (_sync.EnterScope())
        {
            if (!force && _balanceFingerprints.TryGetValue(key, out var previous) &&
                previous == fingerprint)
                return default;
            _balanceFingerprints[key] = fingerprint;
        }
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = GetPortfolioName(),
            SecurityId = account.AssetCode.ToStockSharp(),
            ServerTime = CurrentTime,
            OriginalTransactionId = transactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, account.Balance, true)
        .TryAdd(PositionChangeTypes.BlockedValue, account.Hold, true),
            cancellationToken);
    }

    private ValueTask SendOrderAsync(CoinJarOrder order, long transactionId,
        bool force, CancellationToken cancellationToken)
    {
        if (order is null || order.OrderId <= 0 || order.ProductId.IsEmpty())
            return default;
        var tracked = GetTrackedOrder(order.OrderId, order.Reference);
        var fingerprint = new OrderFingerprint(order.Status, order.Filled,
            order.Size, order.Price);
        var fingerprintKey = $"{transactionId}:{order.OrderId}";
        using (_sync.EnterScope())
        {
            if (!force && _orderFingerprints.TryGetValue(fingerprintKey,
                out var previous) && previous == fingerprint)
                return default;
            _orderFingerprints[fingerprintKey] = fingerprint;
        }
        if (tracked is not null)
        {
            tracked.ExchangeOrderId = order.OrderId;
            TrackOrder(tracked);
        }
        var condition = tracked?.Condition ?? new CoinJarOrderCondition
        {
            TriggerPrice = order.TriggerPrice,
            IsAuctionOnly = order.TimeInForce == CoinJarTimeInForces.AO,
        };
        var isDone = order.Status is CoinJarOrderStatuses.Filled or
            CoinJarOrderStatuses.Cancelled;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.ProductId.ToStockSharp(),
            ServerTime = GetOrderTime(order),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderVolume = order.Size,
            Balance = isDone ? 0m : (order.Size - order.Filled).Max(0m),
            OrderPrice = order.Price ?? tracked?.Price ?? 0m,
            OrderType = order.OrderType.ToStockSharp(),
            OrderState = order.Status.ToStockSharp(),
            OrderId = order.OrderId,
            OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = transactionId,
            TimeInForce = order.TimeInForce.ToStockSharp(),
            PostOnly = order.TimeInForce == CoinJarTimeInForces.MOC,
            Condition = condition,
        }, cancellationToken);
    }

    private ValueTask SendFillAsync(CoinJarFill fill, long transactionId,
        bool onlyNew, CancellationToken cancellationToken)
    {
        if (fill is null || fill.TradeId <= 0 || fill.OrderId <= 0 ||
            fill.ProductId.IsEmpty())
            return default;
        var added = AddAccountTrade(fill.TradeId, transactionId);
        if (onlyNew && !added)
            return default;
        var tracked = GetTrackedOrder(fill.OrderId);
        string commissionCurrency = null;
        using (_sync.EnterScope())
            if (_products.TryGetValue(fill.ProductId, out var product))
                commissionCurrency = product.CounterCurrency.Code;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = fill.ProductId.ToStockSharp(),
            ServerTime = GetTime(fill.Timestamp),
            PortfolioName = GetPortfolioName(),
            Side = fill.Side.ToStockSharp(),
            OrderId = fill.OrderId,
            OrderStringId = fill.OrderId.ToString(CultureInfo.InvariantCulture),
            TradeId = fill.TradeId,
            TradePrice = fill.Price,
            TradeVolume = fill.Size,
            Commission = fill.EstimatedFee,
            CommissionCurrency = commissionCurrency,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = transactionId,
        }, cancellationToken);
    }

    private async ValueTask OnSocketOrderAsync(CoinJarOrder order,
        CancellationToken cancellationToken)
    {
        if (order is null || order.OrderId <= 0)
            return;
        var tracked = GetTrackedOrder(order.OrderId, order.Reference);
        if (tracked is not null)
        {
            tracked.ExchangeOrderId = order.OrderId;
            TrackOrder(tracked);
        }
        (long Id, OrderSubscription Subscription)[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _orderSubscriptions.Where(pair =>
                MatchesOrder(pair.Value, null, null, order)).Select(static pair =>
                    (pair.Key, pair.Value))];
        foreach (var item in subscriptions)
            await SendOrderAsync(order, item.Id, false, cancellationToken);
    }

    private async ValueTask OnSocketFillAsync(CoinJarFill fill,
        CancellationToken cancellationToken)
    {
        if (fill is null || fill.TradeId <= 0)
            return;
        (long Id, OrderSubscription Subscription)[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _orderSubscriptions.Where(pair =>
                MatchesFill(pair.Value, null, null, fill)).Select(static pair =>
                    (pair.Key, pair.Value))];
        foreach (var item in subscriptions)
            await SendFillAsync(fill, item.Id, true, cancellationToken);
    }

    private async ValueTask OnSocketAccountAsync(CoinJarAccount account,
        CancellationToken cancellationToken)
    {
        long[] transactionIds;
        using (_sync.EnterScope())
            transactionIds = [.. _portfolioSubscriptions];
        foreach (var transactionId in transactionIds)
            await SendBalanceAsync(account, transactionId, false,
                cancellationToken);
    }

    private ValueTask SendCanceledOrderAsync(CoinJarOrder order,
        long transactionId, CancellationToken cancellationToken)
    {
        if (order is null)
            return default;
        var cancelled = new CoinJarOrder
        {
            OrderId = order.OrderId,
            OrderType = order.OrderType,
            ProductId = order.ProductId,
            Side = order.Side,
            Price = order.Price,
            Size = order.Size,
            TriggerPrice = order.TriggerPrice,
            TimeInForce = order.TimeInForce,
            Filled = order.Filled,
            Status = CoinJarOrderStatuses.Cancelled,
            Reference = order.Reference,
            Timestamp = CurrentTime,
        };
        return SendOrderAsync(cancelled, transactionId, true, cancellationToken);
    }

    private bool MatchesOrder(OrderSubscription subscription, DateTime? from,
        DateTime? to, CoinJarOrder order)
    {
        if (order is null)
            return false;
        if (!subscription.ProductId.IsEmpty() &&
            !subscription.ProductId.EqualsIgnoreCase(order.ProductId))
            return false;
        if (subscription.OrderId is long orderId && orderId != order.OrderId)
            return false;
        if (subscription.Side is Sides side && order.Side.ToStockSharp() != side)
            return false;
        var time = GetOrderTime(order);
        return (from is null || time >= from.Value.ToUniversalTime()) &&
            (to is null || time <= to.Value.ToUniversalTime());
    }

    private bool MatchesFill(OrderSubscription subscription, DateTime? from,
        DateTime? to, CoinJarFill fill)
    {
        if (fill is null)
            return false;
        if (!subscription.ProductId.IsEmpty() &&
            !subscription.ProductId.EqualsIgnoreCase(fill.ProductId))
            return false;
        if (subscription.OrderId is long orderId && orderId != fill.OrderId)
            return false;
        if (subscription.Side is Sides side && fill.Side.ToStockSharp() != side)
            return false;
        var time = GetTime(fill.Timestamp);
        return (from is null || time >= from.Value.ToUniversalTime()) &&
            (to is null || time <= to.Value.ToUniversalTime());
    }

    private static bool MatchesCancellation(OrderGroupCancelMessage message,
        string productId, CoinJarOrder order)
    {
        if (order is null)
            return false;
        if (!productId.IsEmpty() && !productId.EqualsIgnoreCase(order.ProductId))
            return false;
        if (message.Side is Sides side && order.Side.ToStockSharp() != side)
            return false;
        var isStop = order.OrderType == CoinJarOrderTypes.StopLimit;
        return message.IsStop is not bool requestedStop || requestedStop == isStop;
    }

    private DateTime GetOrderTime(CoinJarOrder order)
        => order.Timestamp == default ? CurrentTime : order.Timestamp.ToUtcTime();

    private static void ValidatePrice(CoinJarProduct product, decimal price,
        string name)
    {
        var level = product.GetPriceLevel(price);
        if (level.TickSize <= 0 || price % level.TickSize != 0)
            throw new InvalidOperationException(
                $"CoinJar {name} {price} must be aligned to tick size " +
                $"{level.TickSize} for product '{product.Id}'.");
    }

    private static void ValidateVolume(CoinJarProduct product, decimal price,
        decimal volume)
    {
        ValidateVolumeSubunit(product, volume);
        var level = product.GetPriceLevel(price);
        if (level.TradeSize > 0 && volume < level.TradeSize)
            throw new InvalidOperationException(
                $"CoinJar volume {volume} is below minimum {level.TradeSize} " +
                $"at price {price} for product '{product.Id}'.");
    }

    private static void ValidateVolumeSubunit(CoinJarProduct product,
        decimal volume)
    {
        var step = GetSubunitStep(product.BaseCurrency);
        if (step > 0 && volume % step != 0)
            throw new InvalidOperationException(
                $"CoinJar volume {volume} must be aligned to {step} for " +
                $"product '{product.Id}'.");
    }

    private static void ValidateOrder(CoinJarOrder order, string operation)
    {
        if (order is null || order.OrderId <= 0)
            throw new InvalidDataException(
                $"CoinJar {operation} an order without returning its ID.");
    }

    private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
