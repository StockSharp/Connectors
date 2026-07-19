namespace StockSharp.BYDFi;

public partial class BYDFiMessageAdapter
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
        var condition = regMsg.Condition as BYDFiOrderCondition ?? new();
        var orderType = ResolveOrderType(regMsg, condition);
        var isConditional = orderType is not (BYDFiOrderTypes.Limit or
            BYDFiOrderTypes.Market);
        var isLimit = orderType is BYDFiOrderTypes.Limit or
            BYDFiOrderTypes.Stop or BYDFiOrderTypes.TakeProfit;
        if (isLimit && regMsg.Price <= 0)
            throw new InvalidOperationException(
                "A positive limit price is required.");
        if (!isLimit && regMsg.PostOnly == true)
            throw new InvalidOperationException(
                "Market and trigger-market orders cannot be post-only.");
        if (isConditional && orderType !=
                BYDFiOrderTypes.TrailingStopMarket &&
            condition.TriggerPrice is not (> 0m))
            throw new InvalidOperationException(
                "A positive trigger price is required.");
        if (orderType == BYDFiOrderTypes.TrailingStopMarket &&
            condition.CallbackRate is not (>= 0.1m and <= 5m))
            throw new InvalidOperationException(
                "Trailing-stop callback rate must be between 0.1 and 5 percent.");
        if (condition.IsClosePosition && orderType is not
            (BYDFiOrderTypes.StopMarket or
                BYDFiOrderTypes.TakeProfitMarket))
            throw new InvalidOperationException(
                "BYDFi closePosition is supported only by stop-market and " +
                "take-profit-market orders.");

        var clientOrderId = CreateClientOrderId(regMsg.TransactionId,
            regMsg.UserOrderId);
        var isReduceOnly = condition.IsReduceOnly ||
            regMsg.PositionEffect == OrderPositionEffects.CloseOnly;
        var result = await RestClient.PlaceOrderAsync(new()
        {
            Wallet = Wallet,
            Symbol = product.Symbol,
            Side = regMsg.Side.ToNative(),
            PositionSide = ToNative(condition.PositionSide),
            Type = orderType,
            IsReduceOnly = condition.IsClosePosition ? null : isReduceOnly,
            IsClosePosition = condition.IsClosePosition ? true : null,
            Quantity = condition.IsClosePosition ? null : volume,
            Price = isLimit ? regMsg.Price : null,
            ClientOrderId = clientOrderId,
            StopPrice = isConditional && orderType !=
                BYDFiOrderTypes.TrailingStopMarket
                    ? condition.TriggerPrice
                    : null,
            ActivationPrice = orderType ==
                BYDFiOrderTypes.TrailingStopMarket
                    ? condition.ActivationPrice
                    : null,
            CallbackRate = orderType ==
                BYDFiOrderTypes.TrailingStopMarket
                    ? condition.CallbackRate
                    : null,
            TimeInForce = isLimit
                ? regMsg.TimeInForce.ToNative(regMsg.PostOnly == true)
                : null,
            WorkingType = isConditional
                ? ToNative(condition.TriggerPriceType)
                : null,
        }, cancellationToken);
        if (result?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                "BYDFi returned no order identifier.");

        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            OrderId = result.OrderId,
            ClientOrderId = clientOrderId,
            Symbol = product.Symbol,
            Side = regMsg.Side,
            OrderType = isConditional
                ? OrderTypes.Conditional
                : regMsg.OrderType ?? OrderTypes.Limit,
            Volume = volume,
            Condition = condition,
            State = OrderStates.Active,
        };
        TrackOrder(tracked);
        await SendOrderAsync(result, regMsg.TransactionId, true,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(replaceMsg.PortfolioName);
        var product = GetProduct(replaceMsg.SecurityId);
        if (replaceMsg.OrderType is OrderTypes.Market or
            OrderTypes.Conditional)
            throw new NotSupportedException(
                "BYDFi can amend active limit orders only.");
        var volume = replaceMsg.Volume.Abs();
        if (volume <= 0 || replaceMsg.Price <= 0)
            throw new InvalidOperationException(
                "Replacement price and volume must be positive.");
        ResolveOrderIdentity(replaceMsg.OldOrderId,
            replaceMsg.OldOrderStringId, out var orderId,
            out var clientOrderId);
        var result = await RestClient.EditOrderAsync(new()
        {
            Wallet = Wallet,
            OrderId = orderId,
            ClientOrderId = clientOrderId,
            Symbol = product.Symbol,
            Side = replaceMsg.Side.ToNative(),
            Quantity = volume,
            Price = replaceMsg.Price,
        }, cancellationToken);
        if (result is null)
            throw new InvalidDataException(
                "BYDFi returned no amended order.");
        var condition = replaceMsg.Condition as BYDFiOrderCondition;
        var tracked = new TrackedOrder
        {
            TransactionId = replaceMsg.TransactionId,
            OrderId = result.OrderId.IsEmpty()
                ? orderId?.ToString(CultureInfo.InvariantCulture)
                : result.OrderId,
            ClientOrderId = result.ClientOrderId.IsEmpty()
                ? clientOrderId
                : result.ClientOrderId,
            Symbol = product.Symbol,
            Side = replaceMsg.Side,
            OrderType = OrderTypes.Limit,
            Volume = volume,
            Condition = condition,
            State = OrderStates.Active,
        };
        TrackOrder(tracked);
        await SendOrderAsync(result, replaceMsg.TransactionId, true,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var product = GetProduct(cancelMsg.SecurityId);
        ResolveOrderIdentity(cancelMsg.OrderId, cancelMsg.OrderStringId,
            out var orderId, out var clientOrderId);
        var result = await RestClient.CancelOrderAsync(new()
        {
            Wallet = Wallet,
            OrderId = orderId,
            ClientOrderId = clientOrderId,
            Symbol = product.Symbol,
        }, cancellationToken);
        if (result is null)
            throw new InvalidDataException(
                "BYDFi returned no canceled order.");
        await SendOrderAsync(result, cancelMsg.TransactionId, true,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetProduct(cancelMsg.SecurityId).Symbol;

        if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
            await ClosePositionsAsync(symbol, cancelMsg.TransactionId,
                cancellationToken);

        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!symbol.IsEmpty())
            symbols.Add(symbol);
        else
        {
            using (_sync.EnterScope())
                symbols.AddRange(_transactionSymbols);
            foreach (var order in await RestClient.GetHistoryOrdersAsync(
                Wallet, null, DateTime.UtcNow - TimeSpan.FromDays(7),
                DateTime.UtcNow, 1000, cancellationToken) ?? [])
                if (order?.Symbol.IsEmpty() == false &&
                    order.Status.ToStockSharpOrderState() ==
                        OrderStates.Active)
                    symbols.Add(order.Symbol.NormalizeSymbol());
        }
        foreach (var item in symbols)
        {
            if (cancelMsg.Side is null && cancelMsg.IsStop is null)
            {
                foreach (var order in await RestClient.CancelAllOrdersAsync(
                    new() { Wallet = Wallet, Symbol = item },
                    cancellationToken) ?? [])
                    await SendOrderAsync(order, cancelMsg.TransactionId,
                        true, cancellationToken);
                continue;
            }

            foreach (var order in await RestClient.GetOpenOrdersAsync(Wallet,
                item, null, null, cancellationToken) ?? [])
            {
                if (!MatchesCancel(cancelMsg, order))
                    continue;
                ResolveOrderIdentity(order.OrderId.ToLong(),
                    order.OrderId, out var orderId, out var clientOrderId);
                var cancelled = await RestClient.CancelOrderAsync(new()
                {
                    Wallet = Wallet,
                    OrderId = orderId,
                    ClientOrderId = clientOrderId,
                    Symbol = item,
                }, cancellationToken);
                if (cancelled is not null)
                    await SendOrderAsync(cancelled,
                        cancelMsg.TransactionId, true, cancellationToken);
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
            BoardCode = BoardCodes.BYDFi,
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
            Symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
                ? null
                : GetProduct(statusMsg.SecurityId).Symbol,
            OrderId = orderId,
            ClientOrderId = clientOrderId,
            Side = statusMsg.Side,
            From = statusMsg.From?.ToUniversalTime(),
            To = statusMsg.To?.ToUniversalTime(),
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
            tracked = [.. _trackedOrders.Values.Distinct()
                .Where(static order => order.State == OrderStates.Active)];
        }
        if (portfolioTargets.Length > 0)
        {
            var balances = await RestClient.GetBalancesAsync(Wallet,
                cancellationToken) ?? [];
            var positions = await RestClient.GetPositionsAsync(Wallet, null,
                cancellationToken) ?? [];
            foreach (var target in portfolioTargets)
                await SendPortfolioSnapshotAsync(target, false, balances,
                    positions, cancellationToken);
        }
        foreach (var target in orderTargets)
            await SendOrderSnapshotAsync(target.Value, target.Key, false,
                cancellationToken);
        foreach (var group in tracked.GroupBy(static order => order.Symbol,
            StringComparer.OrdinalIgnoreCase))
        {
            var subscription = new OrderSubscription
            {
                Symbol = group.Key,
                From = DateTime.UtcNow - TimeSpan.FromDays(7),
                To = DateTime.UtcNow,
                Maximum = 200,
            };
            var orders = await LoadOrdersAsync(subscription,
                cancellationToken);
            var trades = await LoadTradesAsync(subscription,
                cancellationToken);
            foreach (var item in group)
            {
                foreach (var order in orders.Where(order =>
                    MatchesTracked(item, order)))
                    await SendOrderAsync(order, item.TransactionId, false,
                        cancellationToken);
                foreach (var trade in trades.Where(trade =>
                    MatchesTracked(item, trade)))
                    await SendAccountTradeAsync(trade, item.TransactionId,
                        cancellationToken);
            }
        }
    }

    private async ValueTask SendPortfolioSnapshotAsync(long target,
        bool isForced, CancellationToken cancellationToken)
        => await SendPortfolioSnapshotAsync(target, isForced,
            await RestClient.GetBalancesAsync(Wallet, cancellationToken) ?? [],
            await RestClient.GetPositionsAsync(Wallet, null,
                cancellationToken) ?? [], cancellationToken);

    private async ValueTask SendPortfolioSnapshotAsync(long target,
        bool isForced, BYDFiBalance[] balances, BYDFiPosition[] positions,
        CancellationToken cancellationToken)
    {
        foreach (var balance in balances)
            await SendBalanceAsync(balance, target, isForced,
                cancellationToken);
        foreach (var position in positions)
            await SendPositionAsync(position, target, isForced,
                cancellationToken);
    }

    private async ValueTask SendOrderSnapshotAsync(
        OrderSubscription subscription, long target, bool isForced,
        CancellationToken cancellationToken)
    {
        foreach (var order in await LoadOrdersAsync(subscription,
            cancellationToken))
            await SendOrderAsync(order, target, isForced,
                cancellationToken);
        foreach (var trade in await LoadTradesAsync(subscription,
            cancellationToken))
            await SendAccountTradeAsync(trade, target, cancellationToken);
    }

    private async ValueTask<BYDFiOrder[]> LoadOrdersAsync(
        OrderSubscription subscription, CancellationToken cancellationToken)
    {
        var result = new List<BYDFiOrder>();
        if (!subscription.Symbol.IsEmpty())
            result.AddRange((await RestClient.GetOpenOrdersAsync(Wallet,
                subscription.Symbol, subscription.OrderId,
                subscription.ClientOrderId, cancellationToken) ?? [])
                .Where(order => MatchesOrder(subscription, order)));
        await ForEachHistoryWindowAsync(subscription,
            async (from, to, limit, token) =>
            {
                var items = await RestClient.GetHistoryOrdersAsync(Wallet,
                    subscription.Symbol, from, to, limit, token) ?? [];
                result.AddRange(items.Where(order =>
                    MatchesOrder(subscription, order)));
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

    private async ValueTask<BYDFiUserTrade[]> LoadTradesAsync(
        OrderSubscription subscription, CancellationToken cancellationToken)
    {
        var result = new List<BYDFiUserTrade>();
        await ForEachHistoryWindowAsync(subscription,
            async (from, to, limit, token) =>
            {
                var items = await RestClient.GetUserTradesAsync(Wallet,
                    subscription.Symbol, from, to, limit, token) ?? [];
                result.AddRange(items.Where(trade =>
                    MatchesTrade(subscription, trade)));
            }, cancellationToken);
        return [.. result.OrderBy(static trade => trade.Time)
            .TakeLast(subscription.Maximum)];
    }

    private static async ValueTask ForEachHistoryWindowAsync(
        OrderSubscription subscription,
        Func<DateTime, DateTime, int, CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var to = (subscription.To ?? now).ToUniversalTime().Min(now);
        var from = (subscription.From ?? to - TimeSpan.FromDays(7))
            .ToUniversalTime().Max(now - TimeSpan.FromDays(183));
        if (from > to)
            return;
        var cursor = from;
        while (cursor <= to)
        {
            var end = (cursor + TimeSpan.FromDays(7) -
                TimeSpan.FromMilliseconds(1)).Min(to);
            await action(cursor, end,
                subscription.Maximum.Min(1000).Max(1), cancellationToken);
            if (end >= to)
                break;
            cursor = end + TimeSpan.FromMilliseconds(1);
        }
    }

    private ValueTask SendBalanceAsync(BYDFiBalance balance, long target,
        bool isForced, CancellationToken cancellationToken)
    {
        if (balance?.Asset.IsEmpty() != false)
            return default;
        var current = balance.Balance.ToDecimal() ?? 0m;
        var available = balance.AvailableBalance.ToDecimal() ?? 0m;
        var reserved = (balance.Frozen.ToDecimal() ?? 0m) +
            (balance.PositionMargin.ToDecimal() ?? 0m);
        var blocked = (current - available).Max(reserved).Max(0m);
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
                BoardCode = BoardCodes.BYDFi,
            },
            ServerTime = CurrentTime,
            OriginalTransactionId = target,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, current, true)
        .TryAdd(PositionChangeTypes.BlockedValue, blocked, true),
            cancellationToken);
    }

    private ValueTask SendPositionAsync(BYDFiPosition position, long target,
        bool isForced, CancellationToken cancellationToken)
    {
        if (position?.Symbol.IsEmpty() != false)
            return default;
        var side = position.Side.ToStockSharpSide();
        var current = position.Quantity.ToDecimal()?.Abs() ?? 0m;
        var average = position.AveragePrice.ToDecimal();
        var unrealized = position.UnrealizedPnl.ToDecimal();
        var liquidation = position.LiquidationPrice.ToDecimal();
        var key = $"{target}:{position.Symbol}:{side}";
        var fingerprint = new PositionFingerprint(current, average,
            unrealized, liquidation);
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
            SecurityId = position.Symbol.ToStockSharp(),
            Side = side,
            ServerTime = CurrentTime,
            OriginalTransactionId = target,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, current, true)
        .TryAdd(PositionChangeTypes.AveragePrice, average, true)
        .TryAdd(PositionChangeTypes.CurrentPrice,
            position.MarkPrice.ToDecimal(), true)
        .TryAdd(PositionChangeTypes.UnrealizedPnL, unrealized, true)
        .TryAdd(PositionChangeTypes.LiquidationPrice, liquidation, true),
            cancellationToken);
    }

    private ValueTask SendOrderAsync(BYDFiOrder order, long target,
        bool isForced, CancellationToken cancellationToken)
    {
        if (order?.Symbol.IsEmpty() != false || order.OrderId.IsEmpty())
            return default;
        var tracked = MatchTrackedOrder(order);
        var total = order.Quantity.ToDecimal() ?? tracked?.Volume;
        var filled = order.FilledQuantity.ToDecimal() ?? 0m;
        var state = order.Status.ToStockSharpOrderState();
        if (state == OrderStates.None && tracked is not null)
            state = tracked.State;
        var price = order.Price.ToDecimal() ?? 0m;
        var average = filled > 0 ? order.AveragePrice.ToDecimal() : null;
        var updateTime = GetOrderUpdateTime(order);
        var key = $"{target}:{order.OrderId}";
        var fingerprint = new OrderFingerprint(order.Status, filled, price,
            average, updateTime);
        using (_sync.EnterScope())
        {
            if (!isForced && _orderFingerprints.TryGetValue(key,
                out var previous) && previous == fingerprint)
                return default;
            _orderFingerprints[key] = fingerprint;
            if (tracked is not null)
            {
                tracked.OrderId = order.OrderId;
                tracked.State = state;
                if (state != OrderStates.Active &&
                    !_trackedOrders.Values.Any(item =>
                        !ReferenceEquals(item, tracked) &&
                        item.Symbol.EqualsIgnoreCase(tracked.Symbol) &&
                        item.State == OrderStates.Active))
                    _transactionSymbols.Remove(tracked.Symbol);
            }
        }
        var side = order.Side.IsEmpty()
            ? tracked?.Side ?? throw new InvalidDataException(
                $"BYDFi order '{order.OrderId}' has no side.")
            : order.Side.ToStockSharpSide();
        var nativeOrderType = order.OrderType.IsEmpty()
            ? order.LegacyOrderType
            : order.OrderType;
        decimal? balance = total is decimal volume
            ? (volume - filled).Max(0m)
            : null;
        if (state == OrderStates.Done)
            balance = 0m;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.Symbol.ToStockSharp(),
            ServerTime = updateTime > 0
                ? updateTime.ToUtcTime()
                : CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = side,
            OrderVolume = total,
            Balance = balance,
            OrderPrice = price,
            AveragePrice = average,
            OrderType = nativeOrderType.ToStockSharpOrderType(),
            OrderState = state,
            OrderId = order.OrderId.ToLong(),
            OrderStringId = order.OrderId.ToLong() is null
                ? order.OrderId
                : null,
            UserOrderId = order.ClientOrderId,
            TransactionId = tracked?.TransactionId ??
                ParseTransactionId(order.ClientOrderId),
            OriginalTransactionId = target,
            TimeInForce = order.TimeInForce.ToStockSharpTimeInForce(),
            PostOnly = order.TimeInForce.EqualsIgnoreCase("POST_ONLY"),
            PositionEffect = order.IsReduceOnly || order.IsClosePosition
                ? OrderPositionEffects.CloseOnly
                : null,
            Condition = tracked?.Condition,
        }, cancellationToken);
    }

    private ValueTask SendAccountTradeAsync(BYDFiUserTrade trade,
        long target, CancellationToken cancellationToken)
    {
        if (trade?.Symbol.IsEmpty() != false || trade.OrderId.IsEmpty())
            return default;
        var tradeId = $"{trade.OrderId}:{trade.Time}:" +
            $"{trade.Price}:{trade.Quantity}";
        using (_sync.EnterScope())
            if (!_seenAccountTrades.Add(new(target, tradeId)))
                return default;
        var tracked = GetTrackedOrder(trade.OrderId);
        BYDFiProduct product;
        using (_sync.EnterScope())
            _products.TryGetValue(trade.Symbol, out product);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = trade.Symbol.ToStockSharp(),
            ServerTime = trade.Time > 0
                ? trade.Time.ToUtcTime()
                : CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = trade.Side.ToStockSharpSide(),
            OrderId = trade.OrderId.ToLong(),
            OrderStringId = trade.OrderId.ToLong() is null
                ? trade.OrderId
                : null,
            TradeStringId = tradeId,
            TradePrice = trade.Price.ToDecimal(),
            TradeVolume = trade.Quantity.ToDecimal(),
            Commission = trade.Fee.ToDecimal(),
            CommissionCurrency = product?.MarginAsset,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = target,
        }, cancellationToken);
    }

    private async ValueTask ClosePositionsAsync(string symbol, long target,
        CancellationToken cancellationToken)
    {
        foreach (var position in await RestClient.GetPositionsAsync(Wallet,
            symbol, cancellationToken) ?? [])
        {
            var volume = position.Quantity.ToDecimal()?.Abs() ?? 0m;
            if (volume <= 0 || position.Symbol.IsEmpty())
                continue;
            var side = position.Side.ToStockSharpSide() == Sides.Buy
                ? Sides.Sell
                : Sides.Buy;
            var result = await RestClient.PlaceOrderAsync(new()
            {
                Wallet = Wallet,
                Symbol = position.Symbol.NormalizeSymbol(),
                Side = side.ToNative(),
                PositionSide = position.Side.EqualsIgnoreCase("SELL")
                    ? BYDFiPositionSides.Short
                    : BYDFiPositionSides.Long,
                Type = BYDFiOrderTypes.Market,
                IsReduceOnly = true,
                Quantity = volume,
                ClientOrderId = CreateClientOrderId(target, null),
            }, cancellationToken);
            if (result is not null)
                await SendOrderAsync(result, target, true,
                    cancellationToken);
        }
    }

    private void TrackOrder(TrackedOrder order)
    {
        using (_sync.EnterScope())
        {
            if (!order.OrderId.IsEmpty())
                _trackedOrders[order.OrderId] = order;
            if (!order.ClientOrderId.IsEmpty())
                _trackedOrders[order.ClientOrderId] = order;
            _transactionSymbols.Add(order.Symbol);
        }
    }

    private TrackedOrder GetTrackedOrder(string identity)
    {
        if (identity.IsEmpty())
            return null;
        using (_sync.EnterScope())
            return _trackedOrders.TryGetValue(identity, out var order)
                ? order
                : null;
    }

    private TrackedOrder MatchTrackedOrder(BYDFiOrder order)
        => GetTrackedOrder(order?.OrderId) ??
            GetTrackedOrder(order?.ClientOrderId);

    private static bool MatchesTracked(TrackedOrder tracked,
        BYDFiOrder order)
        => tracked is not null && order is not null &&
            (!tracked.OrderId.IsEmpty() &&
                tracked.OrderId.EqualsIgnoreCase(order.OrderId) ||
             !tracked.ClientOrderId.IsEmpty() &&
                tracked.ClientOrderId.EqualsIgnoreCase(order.ClientOrderId));

    private static bool MatchesTracked(TrackedOrder tracked,
        BYDFiUserTrade trade)
        => tracked is not null && trade is not null &&
            tracked.OrderId.EqualsIgnoreCase(trade.OrderId);

    private static bool MatchesOrder(OrderSubscription subscription,
        BYDFiOrder order)
    {
        if (order is null)
            return false;
        if (!subscription.Symbol.IsEmpty() &&
            !subscription.Symbol.EqualsIgnoreCase(order.Symbol))
            return false;
        if (!subscription.OrderId.IsEmpty() &&
            !subscription.OrderId.EqualsIgnoreCase(order.OrderId))
            return false;
        if (!subscription.ClientOrderId.IsEmpty() &&
            !subscription.ClientOrderId.EqualsIgnoreCase(
                order.ClientOrderId))
            return false;
        if (subscription.Side is Sides side && !order.Side.IsEmpty() &&
            order.Side.ToStockSharpSide() != side)
            return false;
        var time = GetOrderUpdateTime(order);
        return IsInRange(time, subscription.From, subscription.To);
    }

    private static bool MatchesTrade(OrderSubscription subscription,
        BYDFiUserTrade trade)
    {
        if (trade is null)
            return false;
        if (!subscription.Symbol.IsEmpty() &&
            !subscription.Symbol.EqualsIgnoreCase(trade.Symbol))
            return false;
        if (!subscription.OrderId.IsEmpty() &&
            !subscription.OrderId.EqualsIgnoreCase(trade.OrderId))
            return false;
        if (subscription.Side is Sides side && !trade.Side.IsEmpty() &&
            trade.Side.ToStockSharpSide() != side)
            return false;
        return IsInRange(trade.Time, subscription.From, subscription.To);
    }

    private static bool MatchesCancel(OrderGroupCancelMessage message,
        BYDFiOrder order)
    {
        if (message.Side is Sides side && !order.Side.IsEmpty() &&
            order.Side.ToStockSharpSide() != side)
            return false;
        var type = (order.OrderType.IsEmpty()
            ? order.LegacyOrderType
            : order.OrderType).ToStockSharpOrderType();
        return message.IsStop is null ||
            message.IsStop == (type == OrderTypes.Conditional);
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

    private void RemoveOrderSubscription(long target)
    {
        using (_sync.EnterScope())
        {
            _orderSubscriptions.Remove(target);
            RemoveFingerprintPrefix(_orderFingerprints, target);
            _seenAccountTrades.RemoveWhere(key =>
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

    private static void ResolveOrderIdentity(long? numericId,
        string stringId, out long? orderId, out string clientOrderId)
    {
        orderId = numericId;
        clientOrderId = null;
        if (orderId is not null)
            return;
        stringId = stringId?.Trim();
        if (stringId.IsEmpty())
            throw new InvalidOperationException(
                "BYDFi order operation requires an order ID or client order ID.");
        if (long.TryParse(stringId, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var parsed))
            orderId = parsed;
        else
            clientOrderId = stringId;
    }

    private static BYDFiOrderTypes ResolveOrderType(
        OrderRegisterMessage message, BYDFiOrderCondition condition)
    {
        if (message.OrderType != OrderTypes.Conditional &&
            condition.TriggerPrice is null &&
            condition.TriggerKind != BYDFiTriggerKinds.TrailingStop)
            return message.OrderType switch
            {
                null or OrderTypes.Limit => BYDFiOrderTypes.Limit,
                OrderTypes.Market => BYDFiOrderTypes.Market,
                _ => throw new NotSupportedException(
                    LocalizedStrings.OrderUnsupportedType.Put(
                        message.OrderType, 0)),
            };
        if (condition.TriggerKind == BYDFiTriggerKinds.TrailingStop)
            return BYDFiOrderTypes.TrailingStopMarket;
        var isLimit = message.Price > 0;
        return (condition.TriggerKind, isLimit) switch
        {
            (BYDFiTriggerKinds.StopLoss, true) => BYDFiOrderTypes.Stop,
            (BYDFiTriggerKinds.StopLoss, false) =>
                BYDFiOrderTypes.StopMarket,
            (BYDFiTriggerKinds.TakeProfit, true) =>
                BYDFiOrderTypes.TakeProfit,
            (BYDFiTriggerKinds.TakeProfit, false) =>
                BYDFiOrderTypes.TakeProfitMarket,
            _ => throw new ArgumentOutOfRangeException(
                nameof(condition.TriggerKind), condition.TriggerKind, null),
        };
    }

    private static BYDFiPositionSides ToNative(BYDFiPositionSide side)
        => side switch
        {
            BYDFiPositionSide.Both => BYDFiPositionSides.Both,
            BYDFiPositionSide.Long => BYDFiPositionSides.Long,
            BYDFiPositionSide.Short => BYDFiPositionSides.Short,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side,
                null),
        };

    private static BYDFiWorkingTypes ToNative(BYDFiTriggerPriceTypes type)
        => type switch
        {
            BYDFiTriggerPriceTypes.ContractPrice =>
                BYDFiWorkingTypes.ContractPrice,
            BYDFiTriggerPriceTypes.MarkPrice => BYDFiWorkingTypes.MarkPrice,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type,
                null),
        };

    private static long GetOrderUpdateTime(BYDFiOrder order)
        => order?.UpdateTime > 0 ? order.UpdateTime
            : order?.LegacyUpdateTime > 0 ? order.LegacyUpdateTime
            : order?.CreateTime > 0 ? order.CreateTime
            : order?.LegacyCreateTime ?? 0;
}
