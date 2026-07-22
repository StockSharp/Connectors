namespace StockSharp.Rain;

public partial class RainMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var product = GetProduct(regMsg.SecurityId);
        var orderType = regMsg.OrderType ?? OrderTypes.Limit;
        if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
            throw new NotSupportedException(
                LocalizedStrings.OrderUnsupportedType.Put(orderType,
                    regMsg.TransactionId));
        var volume = regMsg.Volume.Abs();
        if (volume <= 0)
            throw new InvalidOperationException(
                "Rain order volume must be positive.");
        if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
            throw new InvalidOperationException(
                "A Rain limit order requires a positive price.");
        if (regMsg.PostOnly == true)
            throw new NotSupportedException(
                "Rain Pro does not document post-only orders.");
        if (regMsg.TimeInForce is not null and not TimeInForce.PutInQueue)
            throw new NotSupportedException(
                "Rain Pro documents GTC behavior only.");
        if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
            throw new NotSupportedException(
                "Rain Pro does not document iceberg orders.");
        if (regMsg.TillDate is not null)
            throw new NotSupportedException(
                "Rain Pro does not document GTD orders.");

        var condition = regMsg.Condition as RainOrderCondition ?? new();
        if (condition.QuoteAmount is <= 0)
            throw new InvalidOperationException(
                "Rain market-buy quote amount must be positive.");
        if (condition.QuoteAmount is not null &&
            (orderType != OrderTypes.Market || regMsg.Side != Sides.Buy))
            throw new InvalidOperationException(
                "Rain quote amount applies to market buys only.");
        var wireQuantity = orderType == OrderTypes.Market &&
            regMsg.Side == Sides.Buy
                ? condition.QuoteAmount ?? volume
                : volume;
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            Symbol = product.Symbol,
            Side = regMsg.Side,
            OrderType = orderType,
            Volume = volume,
            Price = regMsg.Price,
            Condition = condition.Clone() as RainOrderCondition,
            State = OrderStates.Active,
        };
        TrackOrder(tracked,
            $"transaction:{regMsg.TransactionId.ToString(CultureInfo.InvariantCulture)}");

        var order = await RestClient.PlaceOrderAsync(new()
        {
            Product = product.Symbol,
            Side = regMsg.Side.ToRain(),
            Type = orderType == OrderTypes.Market
                ? RainOrderTypes.Market
                : RainOrderTypes.Limit,
            Quantity = wireQuantity.ToWire(),
            Price = orderType == OrderTypes.Limit
                ? regMsg.Price.ToWire()
                : null,
        }, cancellationToken);
        if (order?.ClientOrderId.IsEmpty() == false)
        {
            tracked.ClientOrderId = order.ClientOrderId;
            TrackOrder(tracked, order.ClientOrderId);
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
        var identifier = GetCancelIdentifier(cancelMsg.OrderId,
            cancelMsg.OrderStringId, cancelMsg.OriginalTransactionId);
        await RestClient.CancelOrderAsync(identifier, cancellationToken);
        var tracked = GetTrackedOrder(identifier) ??
            GetTrackedOrder(cancelMsg.OriginalTransactionId);
        if (tracked is not null)
        {
            tracked.State = OrderStates.Done;
            await SendCanceledOrderAsync(tracked, identifier,
                cancelMsg.TransactionId, cancellationToken);
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
                "Rain spot cancellation cannot close positions.");
        var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetProduct(cancelMsg.SecurityId).Symbol;
        TrackedOrder[] orders;
        using (_sync.EnterScope())
            orders = [.. _trackedOrders.Values.Distinct().Where(order =>
                order.State != OrderStates.Done &&
                !order.ClientOrderId.IsEmpty() &&
                (symbol.IsEmpty() || order.Symbol.EqualsIgnoreCase(symbol)) &&
                (cancelMsg.Side is null || order.Side == cancelMsg.Side))];
        foreach (var order in orders)
        {
            await RestClient.CancelOrderAsync(order.ClientOrderId,
                cancellationToken);
            order.State = OrderStates.Done;
            await SendCanceledOrderAsync(order, order.ClientOrderId,
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
            var removed = false;
            using (_sync.EnterScope())
            {
                removed = _portfolioSubscriptions.Remove(
                    lookupMsg.OriginalTransactionId);
                RemoveFingerprintPrefix(_balanceFingerprints,
                    lookupMsg.OriginalTransactionId);
            }
            if (removed)
                await ReleaseStreamAsync(
                    RainSocketChannels.AccountBalance,
					RestClient.AccessToken, cancellationToken);
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = GetPortfolioName(),
            BoardCode = BoardCodes.Rain,
            OriginalTransactionId = lookupMsg.TransactionId,
        }, cancellationToken);
        var accounts = await RestClient.GetAccountsAsync(cancellationToken);
        foreach (var account in accounts)
            await SendBalanceAsync(account, lookupMsg.TransactionId, true,
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
            await AcquireStreamAsync(RainSocketChannels.AccountBalance,
				RestClient.AccessToken, cancellationToken);
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
        var clientOrderId = statusMsg.OrderStringId?.Trim();
        if (clientOrderId.IsEmpty() && statusMsg.OrderId is > 0)
            clientOrderId = statusMsg.OrderId.Value.ToString(
                CultureInfo.InvariantCulture);
        var subscription = new OrderSubscription
        {
            ClientOrderId = clientOrderId,
            Symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
                ? null
                : GetProduct(statusMsg.SecurityId).Symbol,
            Side = statusMsg.Side,
            From = statusMsg.From?.ToUniversalTime(),
            To = statusMsg.To?.ToUniversalTime(),
            Maximum = (statusMsg.Count ?? 1000).Min(5000).Max(1).To<int>(),
        };
        var orders = await LoadOrdersAsync(subscription, cancellationToken);
        foreach (var order in orders)
            await SendOrderWithTradesAsync(order, statusMsg.TransactionId,
                true, cancellationToken);
        if (statusMsg.IsHistoryOnly())
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _orderSubscriptions[statusMsg.TransactionId] = subscription;
        try
        {
            await AcquireStreamAsync(RainSocketChannels.Orders,
				RestClient.AccessToken, cancellationToken);
            await SendSubscriptionResultAsync(statusMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _orderSubscriptions.Remove(statusMsg.TransactionId);
            throw;
        }
    }

    private async ValueTask<RainOrder[]> LoadOrdersAsync(
        OrderSubscription subscription, CancellationToken cancellationToken)
    {
        if (!subscription.ClientOrderId.IsEmpty())
        {
            var order = await RestClient.GetOrderAsync(
                subscription.ClientOrderId, cancellationToken);
            return MatchesOrder(subscription, order) ? [order] : [];
        }

        var result = new List<RainOrder>();
        for (var offset = 0; result.Count < subscription.Maximum;)
        {
            var limit = (subscription.Maximum - result.Count).Min(100).Max(1);
            var page = await RestClient.GetClosedOrdersAsync(limit, offset,
                cancellationToken);
            result.AddRange(page.Where(order => MatchesOrder(subscription,
                order)));
            if (page.Length < limit)
                break;
            offset += page.Length;
        }
        return [.. result.Where(static order =>
                order?.ClientOrderId.IsEmpty() == false)
            .GroupBy(static order => order.ClientOrderId,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static order => order.Created)
            .TakeLast(subscription.Maximum)];
    }

    private async ValueTask OnSocketAccountsAsync(RainSocketAccounts payload,
        CancellationToken cancellationToken)
    {
        if (payload?.Accounts is not { Length: > 0 })
            return;
        long[] targets;
        using (_sync.EnterScope())
            targets = [.. _portfolioSubscriptions];
        foreach (var target in targets)
            foreach (var account in payload.Accounts)
                await SendBalanceAsync(account, target, false,
                    cancellationToken);
    }

    private async ValueTask OnSocketOrdersAsync(RainSocketOrders payload,
        CancellationToken cancellationToken)
    {
        if (payload?.Orders is not { Length: > 0 })
            return;
        foreach (var order in payload.Orders.Where(static value =>
            value is not null))
        {
            var targets = new HashSet<long>();
            using (_sync.EnterScope())
                foreach (var pair in _orderSubscriptions)
                    if (MatchesOrder(pair.Value, order))
                        targets.Add(pair.Key);
            var tracked = MatchTrackedOrder(order);
            if (tracked is not null)
                targets.Add(tracked.TransactionId);
            foreach (var target in targets)
                await SendOrderWithTradesAsync(order, target, false,
                    cancellationToken);
        }
    }

    private ValueTask SendBalanceAsync(RainAccount account, long targetId,
        bool isForced, CancellationToken cancellationToken)
    {
        if (account?.Currency.IsEmpty() != false)
            return default;
        var fingerprint = new BalanceFingerprint(account.Balance?.Amount);
        var key = $"{targetId}:{account.Currency}";
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
                SecurityCode = account.Currency.ToUpperInvariant(),
                BoardCode = BoardCodes.Rain,
            },
            ServerTime = CurrentTime,
            OriginalTransactionId = targetId,
        }.TryAdd(PositionChangeTypes.CurrentValue, account.Balance?.Amount,
            true), cancellationToken);
    }

    private async ValueTask SendOrderWithTradesAsync(RainOrder order,
        long targetId, bool isForced, CancellationToken cancellationToken)
    {
        await SendOrderAsync(order, targetId, isForced, cancellationToken);
        foreach (var trade in order?.Trades ?? [])
            await SendAccountTradeAsync(order, trade, targetId,
                cancellationToken);
    }

    private ValueTask SendOrderAsync(RainOrder order, long targetId,
        bool isForced, CancellationToken cancellationToken)
    {
        if (order?.ClientOrderId.IsEmpty() != false || order.Symbol.IsEmpty())
            return default;
        var tracked = MatchTrackedOrder(order);
        if (tracked is not null)
        {
            tracked.ClientOrderId = order.ClientOrderId;
            tracked.State = order.Status.ToStockSharp();
            TrackOrder(tracked, order.ClientOrderId);
        }
        var fingerprint = new OrderFingerprint(order.Status,
            order.Quantity?.Amount, order.FilledQuantity?.Amount,
            order.Limit?.Amount, order.FilledPrice?.Amount);
        var key = $"{targetId}:{order.ClientOrderId}";
        using (_sync.EnterScope())
        {
            if (!isForced && _orderFingerprints.TryGetValue(key,
                out var previous) && previous == fingerprint)
                return default;
            _orderFingerprints[key] = fingerprint;
        }
        var side = order.Side?.ToStockSharp() ?? tracked?.Side ??
            throw new InvalidDataException(
                $"Rain order '{order.ClientOrderId}' has no side.");
        var total = order.Quantity?.Amount ?? tracked?.Volume;
        decimal? balance = total is decimal volume
            ? (volume - (order.FilledQuantity?.Amount ?? 0m)).Max(0m)
            : null;
        if (order.Status.ToStockSharp() == OrderStates.Done)
            balance = 0m;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.Symbol.ToStockSharp(),
            ServerTime = GetTime(order.Closed ?? order.Created),
            PortfolioName = GetPortfolioName(),
            Side = side,
            OrderVolume = total,
            Balance = balance,
            OrderPrice = order.Limit?.Amount ?? tracked?.Price ?? 0m,
            AveragePrice = order.FilledPrice?.Amount,
            OrderType = order.Type.ToStockSharp(),
            OrderState = order.Status.ToStockSharp(),
            OrderStringId = order.ClientOrderId,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = targetId,
            Condition = tracked?.Condition,
        }, cancellationToken);
    }

    private ValueTask SendAccountTradeAsync(RainOrder order,
        RainOrderTrade trade, long targetId,
        CancellationToken cancellationToken)
    {
        if (trade is null || order?.ClientOrderId.IsEmpty() != false ||
            order.Symbol.IsEmpty())
            return default;
        var tradeId = trade.Id;
        if (tradeId.IsEmpty())
            tradeId = $"{order.ClientOrderId}:" +
                $"{GetTime(trade.Date):O}:" +
                $"{trade.Price?.Amount?.ToWire()}:" +
                trade.Quantity?.Amount?.ToWire();
        if (!AddAccountTrade(tradeId, targetId))
            return default;
        var tracked = GetTrackedOrder(order.ClientOrderId);
        var side = order.Side?.ToStockSharp() ?? tracked?.Side ??
            throw new InvalidDataException(
                $"Rain order '{order.ClientOrderId}' trade has no side.");
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = order.Symbol.ToStockSharp(),
            ServerTime = GetTime(trade.Date ?? order.Closed ?? order.Created),
            PortfolioName = GetPortfolioName(),
            Side = side,
            OrderStringId = order.ClientOrderId,
            TradeStringId = tradeId,
            TradePrice = trade.Price?.Amount,
            TradeVolume = trade.Quantity?.Amount,
            Commission = trade.Fee?.Amount,
            CommissionCurrency = trade.Fee?.Currency,
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
            TransactionId = order.TransactionId,
            OriginalTransactionId = targetId,
            Condition = order.Condition,
        }, cancellationToken);

    private ValueTask SendCanceledOrderAsync(TrackedOrder order,
        string identifier, long targetId,
        CancellationToken cancellationToken)
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
            OrderStringId = identifier,
            TransactionId = order.TransactionId,
            OriginalTransactionId = targetId,
            Condition = order.Condition,
        }, cancellationToken);

    private bool MatchesOrder(OrderSubscription subscription, RainOrder order)
    {
        if (order?.ClientOrderId.IsEmpty() != false)
            return false;
        if (!subscription.ClientOrderId.IsEmpty() &&
            !subscription.ClientOrderId.EqualsIgnoreCase(order.ClientOrderId))
            return false;
        if (!subscription.Symbol.IsEmpty() &&
            !subscription.Symbol.EqualsIgnoreCase(order.Symbol))
            return false;
        if (subscription.Side is Sides side &&
            order.Side?.ToStockSharp() != side)
            return false;
        var created = GetTime(order.Created);
        return (subscription.From is null || created >= subscription.From) &&
            (subscription.To is null || created <= subscription.To);
    }

    private async ValueTask RemoveOrderSubscriptionAsync(long targetId,
        CancellationToken cancellationToken)
    {
        var removed = false;
        using (_sync.EnterScope())
        {
            removed = _orderSubscriptions.Remove(targetId);
            RemoveFingerprintPrefix(_orderFingerprints, targetId);
            _seenAccountTrades.RemoveWhere(key => key.TargetId == targetId);
        }
        if (removed)
            await ReleaseStreamAsync(RainSocketChannels.Orders,
				RestClient.AccessToken, cancellationToken);
    }

    private async ValueTask CompleteOrderStatusAsync(
        OrderStatusMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }

    private string GetCancelIdentifier(long? numericId, string stringId,
        long originalTransactionId)
    {
        if (!stringId.IsEmpty())
            return stringId.Trim();
        if (numericId is > 0)
            return numericId.Value.ToString(CultureInfo.InvariantCulture);
        var tracked = GetTrackedOrder(originalTransactionId);
        if (tracked?.ClientOrderId.IsEmpty() == false)
            return tracked.ClientOrderId;
        throw new InvalidOperationException(
            "Rain cancellation requires a client order ID.");
    }

    private static void RemoveFingerprintPrefix<TValue>(
        Dictionary<string, TValue> values, long targetId)
    {
        var prefix = targetId.ToString(CultureInfo.InvariantCulture) + ":";
        foreach (var key in values.Keys.Where(key => key.StartsWith(prefix,
            StringComparison.Ordinal)).ToArray())
            values.Remove(key);
    }
}
