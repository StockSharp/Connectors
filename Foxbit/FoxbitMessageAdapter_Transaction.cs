namespace StockSharp.Foxbit;

public partial class FoxbitMessageAdapter
{
    private const long _maximumClientOrderId = 9007199254740991;

    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var market = GetMarket(regMsg.SecurityId);
        var stockSharpType = regMsg.OrderType ?? OrderTypes.Limit;
        if (stockSharpType is not (OrderTypes.Limit or OrderTypes.Market or
            OrderTypes.Conditional))
            throw new NotSupportedException(
                LocalizedStrings.OrderUnsupportedType.Put(stockSharpType,
                    regMsg.TransactionId));
        var condition = regMsg.Condition as FoxbitOrderCondition ?? new();
        var isStop = stockSharpType == OrderTypes.Conditional;
        var baseType = isStop
            ? regMsg.Price > 0 ? OrderTypes.Limit : OrderTypes.Market
            : stockSharpType;
        var volume = regMsg.Volume.Abs();
        if (volume <= 0 && condition.QuoteAmount is null)
            throw new InvalidOperationException(
                "Foxbit order volume must be positive.");
        if (condition.QuoteAmount is <= 0)
            throw new InvalidOperationException(
                "Foxbit instant-order amount must be positive.");
        if (condition.TriggerPrice is <= 0)
            throw new InvalidOperationException(
                "Foxbit stop trigger price must be positive.");
        if (isStop && condition.TriggerPrice is null)
            throw new InvalidOperationException(
                "A Foxbit conditional order requires a trigger price.");
        if (!isStop && condition.TriggerPrice is not null)
            throw new InvalidOperationException(
                "A Foxbit trigger price requires a conditional order type.");
        if (baseType == OrderTypes.Limit && regMsg.Price <= 0)
            throw new InvalidOperationException(
                "A Foxbit limit order requires a positive price.");
        if (condition.QuoteAmount is not null &&
            (baseType != OrderTypes.Market || isStop))
            throw new InvalidOperationException(
                "Foxbit quote amount applies to regular market orders only.");
        if (condition.SlippageTolerance is < 0 or > 1)
            throw new InvalidOperationException(
                "Foxbit slippage tolerance must be between 0 and 1.");
        if (regMsg.PostOnly == true &&
            (baseType != OrderTypes.Limit ||
                regMsg.TimeInForce is not null and not TimeInForce.PutInQueue))
            throw new InvalidOperationException(
                "Foxbit post-only orders must be GTC limit orders.");
        if (baseType == OrderTypes.Market && regMsg.TimeInForce is not null)
            throw new NotSupportedException(
                "Foxbit time-in-force applies to limit orders only.");
        if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
            throw new NotSupportedException(
                "Foxbit does not document iceberg orders.");
        if (regMsg.TillDate is not null)
            throw new NotSupportedException(
                "Foxbit does not support GTD orders.");

        var nativeType = isStop
            ? baseType == OrderTypes.Limit
                ? FoxbitOrderTypes.StopLimit
                : FoxbitOrderTypes.StopMarket
            : condition.QuoteAmount is not null
                ? FoxbitOrderTypes.Instant
                : baseType == OrderTypes.Market
                    ? FoxbitOrderTypes.Market
                    : FoxbitOrderTypes.Limit;
        if (market.OrderTypes is { Length: > 0 } &&
            !market.OrderTypes.Contains(nativeType))
            throw new NotSupportedException(
                $"Foxbit market '{market.Symbol}' does not allow {nativeType} orders.");
        var clientOrderId = CreateClientOrderId(regMsg.TransactionId,
            regMsg.UserOrderId);
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            ClientOrderId = clientOrderId,
            MarketSymbol = market.Symbol,
            Side = regMsg.Side,
            OrderType = stockSharpType,
            Volume = volume,
            Price = regMsg.Price,
            Condition = condition.Clone() as FoxbitOrderCondition,
        };
        TrackOrder(tracked, clientOrderId);

        var request = new FoxbitPlaceOrderRequest
        {
            Side = regMsg.Side.ToFoxbit(),
            Type = nativeType,
            MarketSymbol = market.Symbol,
            ClientOrderId = clientOrderId,
            Quantity = nativeType == FoxbitOrderTypes.Instant
                ? null
                : volume.ToWire(),
            Amount = condition.QuoteAmount?.ToWire(),
            Price = baseType == OrderTypes.Limit
                ? regMsg.Price.ToWire()
                : null,
            StopPrice = condition.TriggerPrice?.ToWire(),
            IsPostOnly = regMsg.PostOnly == true ? true : null,
            TimeInForce = baseType == OrderTypes.Limit
                ? regMsg.TimeInForce.ToFoxbit()
                : null,
            SelfTradeMode = condition.IsSelfTradePrevented
                ? FoxbitSelfTradeModes.ExpireTaker
                : null,
            SlippageTolerance = condition.SlippageTolerance?.ToWire(),
        };
        var order = await PlaceWithReconciliationAsync(request,
            cancellationToken);
        tracked.ExchangeOrderId = order.Id;
        TrackOrder(tracked, order.Id, order.ClientOrderId, clientOrderId);
        await SendOrderAsync(order, regMsg.TransactionId, true,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var request = CreateCancelRequest(cancelMsg.OrderId,
            cancelMsg.OrderStringId, "cancellation");
        var canceled = await RestClient.CancelOrdersAsync(request,
            cancellationToken);
        if (canceled.Length == 0)
            throw new InvalidDataException(
                "Foxbit accepted a cancellation without returning an order ID.");
        foreach (var order in canceled)
            await SendCanceledOrderAsync(order.Id, cancelMsg.TransactionId,
                cancellationToken);
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
                "Foxbit spot cancellation cannot close positions.");
        var marketSymbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(cancelMsg.SecurityId).Symbol;

        if (cancelMsg.IsStop is null)
        {
            var request = new FoxbitCancelRequest
            {
                Type = marketSymbol.IsEmpty()
                    ? FoxbitCancelTypes.All
                    : cancelMsg.Side is null
                        ? FoxbitCancelTypes.Market
                        : FoxbitCancelTypes.MarketSide,
                MarketSymbol = marketSymbol,
                Side = cancelMsg.Side?.ToFoxbit(),
            };
            var canceled = await RestClient.CancelOrdersAsync(request,
                cancellationToken);
            foreach (var order in canceled)
                await SendCanceledOrderAsync(order.Id,
                    cancelMsg.TransactionId, cancellationToken);
            return;
        }

        var subscription = new OrderSubscription
        {
            MarketSymbol = marketSymbol,
            Side = cancelMsg.Side,
            From = DateTime.UtcNow - TimeSpan.FromDays(90),
            To = DateTime.UtcNow,
            Maximum = 5000,
        };
        var orders = await LoadOrdersAsync(subscription, false,
            cancellationToken);
        foreach (var order in orders.Where(order => order.State is
            FoxbitOrderStates.Active or FoxbitOrderStates.PartiallyFilled or
            FoxbitOrderStates.PendingCancel))
        {
            var isStop = order.Type is FoxbitOrderTypes.StopLimit or
                FoxbitOrderTypes.StopMarket;
            if (isStop != cancelMsg.IsStop.Value)
                continue;
            var canceled = await RestClient.CancelOrdersAsync(new()
            {
                Type = FoxbitCancelTypes.Id,
                Id = ParseOrderId(order.Id),
            }, cancellationToken);
            foreach (var item in canceled)
                await SendCanceledOrderAsync(item.Id,
                    cancelMsg.TransactionId, cancellationToken);
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
                foreach (var key in _balanceFingerprints.Keys.Where(key =>
                    key.StartsWith(lookupMsg.OriginalTransactionId.ToString(
                        CultureInfo.InvariantCulture) + ":",
                        StringComparison.Ordinal)).ToArray())
                    _balanceFingerprints.Remove(key);
            }
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = GetPortfolioName(),
            BoardCode = BoardCodes.Foxbit,
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

        string orderId = null;
        string clientOrderId = null;
        if (statusMsg.OrderId is > 0)
            orderId = statusMsg.OrderId.Value.ToString(
                CultureInfo.InvariantCulture);
        else if (!statusMsg.OrderStringId.IsEmpty())
        {
            var identifier = statusMsg.OrderStringId.Trim();
            var tracked = GetTrackedOrder(identifier);
            if (tracked is not null &&
                identifier.EqualsIgnoreCase(tracked.ClientOrderId))
                clientOrderId = identifier;
            else if (long.TryParse(identifier, NumberStyles.None,
                CultureInfo.InvariantCulture, out _))
                orderId = identifier;
            else
                throw new InvalidOperationException(
                    "Foxbit order and client-order IDs must be numeric.");
        }
        var marketSymbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(statusMsg.SecurityId).Symbol;
        var trackedOrder = GetTrackedOrder(orderId, clientOrderId);
        marketSymbol ??= trackedOrder?.MarketSymbol;
        var subscription = new OrderSubscription
        {
            MarketSymbol = marketSymbol,
            OrderId = orderId,
            ClientOrderId = clientOrderId,
            Side = statusMsg.Side,
            From = statusMsg.From?.ToUniversalTime(),
            To = statusMsg.To?.ToUniversalTime(),
            Maximum = (statusMsg.Count ?? 1000).Min(5000).Max(1).To<int>(),
        };
        await SendOrderSnapshotAsync(statusMsg.TransactionId, subscription,
            false, true, cancellationToken);
        if (statusMsg.IsHistoryOnly())
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _orderSubscriptions[statusMsg.TransactionId] = subscription;
        await SendSubscriptionResultAsync(statusMsg, cancellationToken);
    }

    private async ValueTask<FoxbitOrder> PlaceWithReconciliationAsync(
        FoxbitPlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        Exception placementError = null;
        FoxbitOrderCreated created = null;
        try
        {
            created = await RestClient.PlaceOrderAsync(request,
                cancellationToken);
            if (created?.Id.IsEmpty() != false)
                placementError = new InvalidDataException(
                    "Foxbit accepted an order without returning its ID.");
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            IsUncertainOperation(error))
        {
            placementError = error;
        }

        if (created?.Id.IsEmpty() == false)
            return await GetOrderWithRetryAsync(created.Id, null,
                cancellationToken);
        if (placementError is null)
            throw new InvalidOperationException(
                "Foxbit order placement failed.");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt),
                cancellationToken);
            try
            {
                var order = await RestClient.GetOrderByClientIdAsync(
                    request.ClientOrderId, cancellationToken);
                if (order?.Id.IsEmpty() == false)
                    return order;
            }
            catch (FoxbitApiException error) when (
                error.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }
        throw placementError;
    }

    private async ValueTask<FoxbitOrder> GetOrderWithRetryAsync(string orderId,
        string clientOrderId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return !orderId.IsEmpty()
                    ? await RestClient.GetOrderAsync(orderId,
                        cancellationToken)
                    : await RestClient.GetOrderByClientIdAsync(clientOrderId,
                        cancellationToken);
            }
            catch (FoxbitApiException error) when (
                error.StatusCode == HttpStatusCode.NotFound && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt),
                    cancellationToken);
            }
        }
    }

    private FoxbitCancelRequest CreateCancelRequest(long? numericOrderId,
        string stringOrderId, string operation)
    {
        if (numericOrderId is > 0)
            return new()
            {
                Type = FoxbitCancelTypes.Id,
                Id = numericOrderId.Value,
            };
        var identifier = stringOrderId?.Trim();
        if (identifier.IsEmpty())
            throw new InvalidOperationException(
                $"Foxbit {operation} requires an order ID.");
        if (!long.TryParse(identifier, NumberStyles.None,
            CultureInfo.InvariantCulture, out _))
            throw new InvalidOperationException(
                "Foxbit order and client-order IDs must be numeric.");
        var tracked = GetTrackedOrder(identifier);
        if (tracked is not null &&
            identifier.EqualsIgnoreCase(tracked.ClientOrderId) &&
            !identifier.EqualsIgnoreCase(tracked.ExchangeOrderId))
            return new()
            {
                Type = FoxbitCancelTypes.ClientOrderId,
                ClientOrderId = identifier,
            };
        return new()
        {
            Type = FoxbitCancelTypes.Id,
            Id = ParseOrderId(identifier),
        };
    }

    private async ValueTask SendCanceledOrderAsync(string orderId,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (orderId.IsEmpty())
            return;
        FoxbitOrder order = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            order = await GetOrderWithRetryAsync(orderId, null,
                cancellationToken);
            if (order.State is not (FoxbitOrderStates.Active or
                FoxbitOrderStates.PartiallyFilled))
                break;
            await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt),
                cancellationToken);
        }
        await SendOrderAsync(order, originalTransactionId, true,
            cancellationToken);
    }

    private async ValueTask SendPortfolioSnapshotAsync(long targetId,
        bool isForced, CancellationToken cancellationToken)
    {
        var accounts = await RestClient.GetAccountsAsync(cancellationToken);
        foreach (var account in accounts)
            await SendBalanceAsync(account, targetId, isForced,
                cancellationToken);
    }

    private async ValueTask SendOrderSnapshotAsync(long targetId,
        OrderSubscription subscription, bool isRefresh, bool isForced,
        CancellationToken cancellationToken)
    {
        var orders = await LoadOrdersAsync(subscription, isRefresh,
            cancellationToken);
        foreach (var order in orders)
            await SendOrderAsync(order, targetId, isForced,
                cancellationToken);

        var markets = subscription.MarketSymbol.IsEmpty()
            ? orders.Select(static order => order.MarketSymbol)
                .Where(static value => !value.IsEmpty())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [subscription.MarketSymbol];
        foreach (var marketSymbol in markets)
        {
            var trades = await LoadTradesAsync(marketSymbol,
                subscription.OrderId, subscription.From, subscription.To,
                isRefresh ? subscription.Maximum.Min(200) :
                    subscription.Maximum, cancellationToken);
            foreach (var trade in trades.Where(trade =>
                MatchesTrade(subscription, trade)))
                await SendTradeAsync(trade, targetId, !isForced,
                    cancellationToken);
        }
    }

    private async ValueTask<FoxbitOrder[]> LoadOrdersAsync(
        OrderSubscription subscription, bool isRefresh,
        CancellationToken cancellationToken)
    {
        if (!subscription.OrderId.IsEmpty())
        {
            var order = await RestClient.GetOrderAsync(subscription.OrderId,
                cancellationToken);
            return MatchesOrder(subscription, order) ? [order] : [];
        }
        if (!subscription.ClientOrderId.IsEmpty())
        {
            var order = await RestClient.GetOrderByClientIdAsync(
                subscription.ClientOrderId, cancellationToken);
            return MatchesOrder(subscription, order) ? [order] : [];
        }

        var upperBound = (subscription.To ?? DateTime.UtcNow).ToUniversalTime();
        var lowerBound = subscription.From?.ToUniversalTime() ??
            upperBound - TimeSpan.FromDays(7);
        if (isRefresh)
            lowerBound = lowerBound.Max(DateTime.UtcNow - TimeSpan.FromDays(1));
        var maximum = isRefresh
            ? subscription.Maximum.Min(200)
            : subscription.Maximum;
        var result = new List<FoxbitOrder>();
        var windowEnd = upperBound;
        while (result.Count < maximum && windowEnd >= lowerBound)
        {
            var windowStart = lowerBound.Max(windowEnd - TimeSpan.FromDays(89));
            for (var page = 1; result.Count < maximum; page++)
            {
                var pageSize = (maximum - result.Count).Min(100).Max(1);
                var items = await RestClient.GetOrdersAsync(new()
                {
                    From = windowStart,
                    To = windowEnd,
                    PageSize = pageSize,
                    Page = page,
                    MarketSymbol = subscription.MarketSymbol,
                    Side = subscription.Side?.ToFoxbit(),
                }, cancellationToken);
                result.AddRange(items.Where(order =>
                    MatchesOrder(subscription, order)));
                if (items.Length < pageSize)
                    break;
            }
            if (windowStart <= lowerBound)
                break;
            windowEnd = windowStart.AddMilliseconds(-1);
        }
        return [.. result.Where(static order => !order.Id.IsEmpty())
            .GroupBy(static order => order.Id,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static order => order.CreatedAt)
            .TakeLast(maximum)];
    }

    private async ValueTask<FoxbitTrade[]> LoadTradesAsync(
        string marketSymbol, string orderId, DateTime? from, DateTime? to,
        int maximum, CancellationToken cancellationToken)
    {
        var upperBound = (to ?? DateTime.UtcNow).ToUniversalTime();
        var lowerBound = from?.ToUniversalTime() ??
            upperBound - TimeSpan.FromDays(7);
        var availableFrom = DateTime.UtcNow - TimeSpan.FromDays(90);
        if (lowerBound < availableFrom)
            lowerBound = availableFrom;
        var result = new List<FoxbitTrade>();
        var windowEnd = upperBound;
        while (result.Count < maximum && windowEnd >= lowerBound)
        {
            var windowStart = lowerBound.Max(windowEnd - TimeSpan.FromDays(89));
            for (var page = 1; result.Count < maximum; page++)
            {
                var pageSize = (maximum - result.Count).Min(100).Max(1);
                var items = await RestClient.GetTradesAsync(new()
                {
                    MarketSymbol = marketSymbol,
                    OrderId = orderId,
                    From = windowStart,
                    To = windowEnd,
                    Page = page,
                    PageSize = pageSize,
                }, cancellationToken);
                result.AddRange(items);
                if (items.Length < pageSize)
                    break;
            }
            if (windowStart <= lowerBound)
                break;
            windowEnd = windowStart.AddMilliseconds(-1);
        }
        return [.. result.Where(static trade => !trade.Id.IsEmpty())
            .GroupBy(static trade => trade.Id,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static trade => trade.CreatedAt)
            .TakeLast(maximum)];
    }

    private ValueTask SendBalanceAsync(FoxbitAccount account, long targetId,
        bool isForced, CancellationToken cancellationToken)
    {
        if (account?.CurrencySymbol.IsEmpty() != false)
            return default;
        var fingerprint = new BalanceFingerprint(account.Balance,
            account.Available, account.Locked);
        var key = $"{targetId}:{account.CurrencySymbol}";
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
                SecurityCode = account.CurrencySymbol.ToUpperInvariant(),
                BoardCode = BoardCodes.Foxbit,
            },
            ServerTime = CurrentTime,
            OriginalTransactionId = targetId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, account.Balance, true)
        .TryAdd(PositionChangeTypes.BlockedValue, account.Locked, true),
            cancellationToken);
    }

    private ValueTask SendOrderAsync(FoxbitOrder order, long targetId,
        bool isForced, CancellationToken cancellationToken)
    {
        if (order?.Id.IsEmpty() != false || order.MarketSymbol.IsEmpty())
            return default;
        var tracked = GetTrackedOrder(order.Id, order.ClientOrderId);
        var fingerprint = new OrderFingerprint(order.State, order.Quantity,
            order.ExecutedQuantity, order.Price, order.AveragePrice);
        var key = $"{targetId}:{order.Id}";
        using (_sync.EnterScope())
        {
            if (!isForced && _orderFingerprints.TryGetValue(key,
                out var previous) && previous == fingerprint)
                return default;
            _orderFingerprints[key] = fingerprint;
        }
        if (tracked is not null)
        {
            tracked.ExchangeOrderId = order.Id;
            TrackOrder(tracked, order.Id, order.ClientOrderId);
        }
        var side = order.Side?.ToStockSharp() ?? tracked?.Side ??
            throw new InvalidDataException(
                $"Foxbit order '{order.Id}' has no side.");
        var condition = tracked?.Condition ?? new FoxbitOrderCondition
        {
            TriggerPrice = order.StopPrice,
            QuoteAmount = order.Type == FoxbitOrderTypes.Instant
                ? order.InstantAmount
                : null,
            SlippageTolerance = order.SlippageTolerance,
            IsSelfTradePrevented = order.SelfTradeMode is not null,
        };
        var volume = order.Quantity ?? tracked?.Volume;
        decimal? balance = volume is decimal total
            ? (total - (order.ExecutedQuantity ?? 0m)).Max(0m)
            : null;
        if (order.State.ToStockSharp() == OrderStates.Done)
            balance = 0m;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.MarketSymbol.ToStockSharp(),
            ServerTime = GetTime(order.CreatedAt),
            PortfolioName = GetPortfolioName(),
            Side = side,
            OrderVolume = volume,
            Balance = balance,
            OrderPrice = order.Price ?? tracked?.Price ?? 0m,
            AveragePrice = order.AveragePrice,
            OrderType = order.Type.ToStockSharp(),
            OrderState = order.State.ToStockSharp(),
            OrderStringId = order.Id,
            UserOrderId = order.ClientOrderId,
            TransactionId = tracked?.TransactionId ??
                GetTransactionId(order.ClientOrderId),
            OriginalTransactionId = targetId,
            TimeInForce = order.TimeInForce.ToStockSharp(),
            PostOnly = order.IsPostOnly,
            Condition = condition,
        }, cancellationToken);
    }

    private ValueTask SendTradeAsync(FoxbitTrade trade, long targetId,
        bool onlyNew, CancellationToken cancellationToken)
    {
        if (trade?.Id.IsEmpty() != false || trade.MarketSymbol.IsEmpty())
            return default;
        var added = AddAccountTrade(trade.Id, targetId);
        if (onlyNew && !added)
            return default;
        var tracked = GetTrackedOrder(trade.OrderId);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = trade.MarketSymbol.ToStockSharp(),
            ServerTime = GetTime(trade.CreatedAt),
            PortfolioName = GetPortfolioName(),
            Side = trade.Side.ToStockSharp(),
            OrderStringId = trade.OrderId,
            TradeStringId = trade.Id,
            TradePrice = trade.Price,
            TradeVolume = trade.Quantity,
            Commission = trade.Fee,
            CommissionCurrency = trade.FeeCurrencySymbol,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = targetId,
        }, cancellationToken);
    }

    private bool MatchesOrder(OrderSubscription subscription,
        FoxbitOrder order)
    {
        if (order is null || order.Id.IsEmpty())
            return false;
        if (!subscription.MarketSymbol.IsEmpty() &&
            !subscription.MarketSymbol.EqualsIgnoreCase(order.MarketSymbol))
            return false;
        if (!subscription.OrderId.IsEmpty() &&
            !subscription.OrderId.EqualsIgnoreCase(order.Id))
            return false;
        if (!subscription.ClientOrderId.IsEmpty() &&
            !subscription.ClientOrderId.EqualsIgnoreCase(order.ClientOrderId))
            return false;
        if (subscription.Side is Sides side &&
            order.Side?.ToStockSharp() != side)
            return false;
        var time = GetTime(order.CreatedAt);
        return (subscription.From is null || time >= subscription.From) &&
            (subscription.To is null || time <= subscription.To);
    }

    private bool MatchesTrade(OrderSubscription subscription,
        FoxbitTrade trade)
        => trade is not null &&
            (subscription.MarketSymbol.IsEmpty() ||
                subscription.MarketSymbol.EqualsIgnoreCase(
                    trade.MarketSymbol)) &&
            (subscription.OrderId.IsEmpty() ||
                subscription.OrderId.EqualsIgnoreCase(trade.OrderId)) &&
            (subscription.Side is null ||
                trade.Side.ToStockSharp() == subscription.Side) &&
            (subscription.From is null ||
                GetTime(trade.CreatedAt) >= subscription.From) &&
            (subscription.To is null ||
                GetTime(trade.CreatedAt) <= subscription.To);

    private async ValueTask RefreshOrderSubscriptionsAsync(
        CancellationToken cancellationToken)
    {
        KeyValuePair<long, OrderSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _orderSubscriptions];
        foreach (var pair in subscriptions)
            await SendOrderSnapshotAsync(pair.Key, pair.Value, true, false,
                cancellationToken);
    }

    private async ValueTask RefreshPortfolioSubscriptionsAsync(
        CancellationToken cancellationToken)
    {
        long[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _portfolioSubscriptions];
        if (subscriptions.Length == 0)
            return;
        var accounts = await RestClient.GetAccountsAsync(cancellationToken);
        foreach (var targetId in subscriptions)
            foreach (var account in accounts)
                await SendBalanceAsync(account, targetId, false,
                    cancellationToken);
    }

    private void RemoveOrderSubscription(long targetId)
    {
        using (_sync.EnterScope())
        {
            _orderSubscriptions.Remove(targetId);
            foreach (var key in _orderFingerprints.Keys.Where(key =>
                key.StartsWith(targetId.ToString(CultureInfo.InvariantCulture) +
                    ":", StringComparison.Ordinal)).ToArray())
                _orderFingerprints.Remove(key);
            _seenAccountTrades.RemoveWhere(key => key.TargetId == targetId);
        }
    }

    private async ValueTask CompleteOrderStatusAsync(
        OrderStatusMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }

    private static string CreateClientOrderId(long transactionId,
        string userOrderId)
    {
        if (!userOrderId.IsEmpty() &&
            long.TryParse(userOrderId.Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var requested) &&
            requested is > 0 and <= _maximumClientOrderId)
            return requested.ToString(CultureInfo.InvariantCulture);
        if (transactionId is > 0 and <= _maximumClientOrderId)
            return transactionId.ToString(CultureInfo.InvariantCulture);
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(
            CultureInfo.InvariantCulture);
    }

    private static long ParseOrderId(string value)
        => long.TryParse(value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var orderId) && orderId > 0
            ? orderId
            : throw new InvalidDataException(
                $"Foxbit returned invalid order ID '{value}'.");

    private static bool IsUncertainOperation(Exception error)
        => error is HttpRequestException or TaskCanceledException ||
            error is FoxbitApiException api && (int)api.StatusCode >= 500;
}
