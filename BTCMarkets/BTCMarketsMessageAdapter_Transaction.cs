namespace StockSharp.BTCMarkets;

public partial class BTCMarketsMessageAdapter
{
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
        var condition = regMsg.Condition as BTCMarketsOrderCondition ?? new();
        var baseType = stockSharpType == OrderTypes.Conditional
            ? regMsg.Price > 0 ? OrderTypes.Limit : OrderTypes.Market
            : stockSharpType;
        var volume = regMsg.Volume.Abs();
        if (condition.TargetAmount is null && volume <= 0)
            throw new InvalidOperationException("Order volume must be positive.");
        if (condition.TargetAmount is <= 0)
            throw new InvalidOperationException("Target amount must be positive.");
        if (condition.TriggerPrice is <= 0)
            throw new InvalidOperationException("Trigger price must be positive.");
        if (baseType == OrderTypes.Limit && regMsg.Price <= 0)
            throw new InvalidOperationException(
                "A positive price is required for a limit order.");
        if (condition.TargetAmount is not null &&
            (baseType != OrderTypes.Market || condition.TriggerPrice is not null))
            throw new InvalidOperationException(
                "BTC Markets target amount applies to regular market orders only.");
        if (regMsg.PostOnly == true &&
            (baseType != OrderTypes.Limit || condition.TriggerPrice is not null))
            throw new InvalidOperationException(
                "BTC Markets post-only applies to regular limit orders only.");
        if (regMsg.PostOnly == true && regMsg.TimeInForce is not null and
            not TimeInForce.PutInQueue)
            throw new InvalidOperationException(
                "A post-only order must use GTC time-in-force.");
        if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
            throw new NotSupportedException(
                "BTC Markets does not document iceberg orders.");
        if (regMsg.TillDate is not null)
            throw new NotSupportedException(
                "BTC Markets does not support GTD orders.");

        var orderType = condition.TriggerPrice is null
            ? baseType == OrderTypes.Market
                ? BTCMarketsOrderTypes.Market
                : BTCMarketsOrderTypes.Limit
            : condition.IsTakeProfit
                ? BTCMarketsOrderTypes.TakeProfit
                : baseType == OrderTypes.Market
                    ? BTCMarketsOrderTypes.Stop
                    : BTCMarketsOrderTypes.StopLimit;
        var clientOrderId = CreateClientOrderId(regMsg.TransactionId,
            regMsg.UserOrderId);
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            MarketId = market.MarketId,
            ClientOrderId = clientOrderId,
            Side = regMsg.Side,
            OrderType = baseType,
            Volume = volume,
            Price = regMsg.Price,
            Condition = condition.Clone() as BTCMarketsOrderCondition,
        };
        TrackOrder(tracked, clientOrderId,
            regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));

        var result = await RestClient.PlaceOrderAsync(new()
        {
            MarketId = market.MarketId,
            Price = baseType == OrderTypes.Limit ? regMsg.Price.ToWire() : null,
            Amount = condition.TargetAmount is null ? volume.ToWire() : null,
            OrderType = orderType,
            Side = regMsg.Side.ToBTCMarkets(),
            TriggerPrice = condition.TriggerPrice?.ToWire(),
            TargetAmount = condition.TargetAmount?.ToWire(),
            TimeInForce = regMsg.TimeInForce.ToBTCMarkets(),
            IsPostOnly = regMsg.PostOnly,
            SelfTrade = condition.IsSelfTradePrevented
                ? BTCMarketsSelfTradeModes.Prevented
                : null,
            ClientOrderId = clientOrderId,
        }, cancellationToken);
        ValidateOrder(result, "accepted");
        result.ClientOrderId ??= clientOrderId;
        tracked.ExchangeOrderId = result.OrderId;
        TrackOrder(tracked, result.OrderId, clientOrderId);
        await SendOrderAsync(result, regMsg.TransactionId, true,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(replaceMsg.PortfolioName);
        if (replaceMsg.Price <= 0)
            throw new InvalidOperationException(
                "Replacement price must be positive.");
        var volume = replaceMsg.Volume.Abs();
        if (volume <= 0)
            throw new InvalidOperationException(
                "Replacement volume must be positive.");
        var identifier = ResolveOrderIdentifier(replaceMsg.OldOrderId,
            replaceMsg.OldOrderStringId, "replacement");
        var clientOrderId = CreateClientOrderId(replaceMsg.TransactionId,
            replaceMsg.UserOrderId);
        var result = await RestClient.ReplaceOrderAsync(identifier, new()
        {
            Price = replaceMsg.Price.ToWire(),
            Amount = volume.ToWire(),
            ClientOrderId = clientOrderId,
        }, cancellationToken);
        ValidateOrder(result, "replaced");
        result.ClientOrderId ??= clientOrderId;
        var market = GetMarket(result.MarketId);
        var condition = replaceMsg.Condition as BTCMarketsOrderCondition;
        var tracked = new TrackedOrder
        {
            TransactionId = replaceMsg.TransactionId,
            MarketId = market.MarketId,
            ExchangeOrderId = result.OrderId,
            ClientOrderId = clientOrderId,
            Side = result.Side?.ToStockSharp() ?? replaceMsg.Side,
            OrderType = result.OrderType.ToStockSharp(),
            Volume = volume,
            Price = replaceMsg.Price,
            Condition = condition?.Clone() as BTCMarketsOrderCondition,
        };
        TrackOrder(tracked, result.OrderId, clientOrderId);
        await SendOrderAsync(result, replaceMsg.TransactionId, true,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var identifier = ResolveOrderIdentifier(cancelMsg.OrderId,
            cancelMsg.OrderStringId, "cancellation");
        var result = await RestClient.CancelOrderAsync(identifier,
            cancellationToken);
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = cancelMsg.SecurityId,
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            OrderStringId = result?.OrderId ?? identifier,
            OrderState = OrderStates.Done,
            Balance = 0m,
            OriginalTransactionId = cancelMsg.TransactionId,
        }, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
            throw new NotSupportedException(
                "BTC Markets spot cancellation cannot close positions.");
        var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(cancelMsg.SecurityId);
        if (cancelMsg.Side is null && cancelMsg.IsStop is null)
        {
            var canceled = await RestClient.CancelOrdersAsync(new()
            {
                MarketIds = market is null ? [] : [market.MarketId],
            }, cancellationToken);
            foreach (var order in canceled ?? [])
                await SendCanceledOrderAsync(order, market?.MarketId,
                    cancelMsg.TransactionId, cancellationToken);
            return;
        }

        var orders = await LoadOpenOrdersAsync(market?.MarketId,
            cancellationToken);
        foreach (var order in orders)
        {
            if (order?.OrderId.IsEmpty() != false || order.Side is null)
                continue;
            if (cancelMsg.Side is Sides side &&
                order.Side.Value.ToStockSharp() != side)
                continue;
            var isStop = order.OrderType is BTCMarketsOrderTypes.Stop or
                BTCMarketsOrderTypes.StopLimit or BTCMarketsOrderTypes.TakeProfit;
            if (cancelMsg.IsStop is bool requestedStop && requestedStop != isStop)
                continue;
            var canceled = await RestClient.CancelOrderAsync(order.OrderId,
                cancellationToken);
            await SendCanceledOrderAsync(canceled, order.MarketId,
                cancelMsg.TransactionId, cancellationToken);
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
            var removed = false;
            using (_sync.EnterScope())
                removed = _portfolioSubscriptions.Remove(
                    lookupMsg.OriginalTransactionId);
            if (removed)
            {
                await ReleasePrivateStreamAsync(
                    BTCMarketsSocketChannels.FundChange, cancellationToken);
                await ReleasePrivateStreamAsync(
                    BTCMarketsSocketChannels.OrderChange, cancellationToken);
            }
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = GetPortfolioName(),
            BoardCode = BoardCodes.BTCMarkets,
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
            await AcquirePrivateStreamAsync(BTCMarketsSocketChannels.FundChange,
                cancellationToken);
            await AcquirePrivateStreamAsync(BTCMarketsSocketChannels.OrderChange,
                cancellationToken);
            await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _portfolioSubscriptions.Remove(lookupMsg.TransactionId);
            await ReleasePrivateStreamAsync(BTCMarketsSocketChannels.FundChange,
                cancellationToken);
            await ReleasePrivateStreamAsync(BTCMarketsSocketChannels.OrderChange,
                cancellationToken);
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
            var removed = false;
            using (_sync.EnterScope())
                removed = _orderSubscriptions.Remove(
                    statusMsg.OriginalTransactionId);
            if (removed)
                await ReleasePrivateStreamAsync(
                    BTCMarketsSocketChannels.OrderChange, cancellationToken);
            return;
        }
        if (statusMsg.Count is <= 0)
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        ValidatePortfolio(statusMsg.PortfolioName);
        var marketId = statusMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(statusMsg.SecurityId).MarketId;
        string orderId = null;
        if (statusMsg.HasOrderId())
            orderId = ResolveOrderIdentifier(statusMsg.OrderId,
                statusMsg.OrderStringId, "lookup");
        marketId ??= GetTrackedOrder(orderId)?.MarketId;
        var subscription = new OrderSubscription
        {
            MarketId = marketId,
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
            await AcquirePrivateStreamAsync(BTCMarketsSocketChannels.OrderChange,
                cancellationToken);
            await SendSubscriptionResultAsync(statusMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _orderSubscriptions.Remove(statusMsg.TransactionId);
            await ReleasePrivateStreamAsync(BTCMarketsSocketChannels.OrderChange,
                cancellationToken);
            throw;
        }
    }

    private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
        bool force, CancellationToken cancellationToken)
    {
        var balances = await RestClient.GetBalancesAsync(cancellationToken);
        foreach (var balance in balances ?? [])
            await SendBalanceAsync(balance, transactionId, force,
                cancellationToken);
    }

    private async ValueTask SendOrderSnapshotAsync(long transactionId,
        OrderSubscription subscription, DateTime? from, DateTime? to, int maximum,
        bool force, CancellationToken cancellationToken)
    {
        if (!subscription.OrderId.IsEmpty())
        {
            var order = await RestClient.GetOrderAsync(subscription.OrderId,
                cancellationToken);
            if (MatchesOrder(subscription, from, to, order))
                await SendOrderAsync(order, transactionId, force,
                    cancellationToken);
        }
        else
        {
            var orders = await LoadOrdersAsync(subscription.MarketId, from, to,
                maximum, cancellationToken);
            foreach (var order in orders.Where(order =>
                MatchesOrder(subscription, from, to, order)))
                await SendOrderAsync(order, transactionId, force,
                    cancellationToken);
        }

        var trades = await LoadUserTradesAsync(subscription.MarketId,
            subscription.OrderId, from, to, maximum, cancellationToken);
        foreach (var trade in trades.Where(trade =>
            MatchesTrade(subscription, from, to, trade)))
            await SendUserTradeAsync(trade, transactionId, !force,
                cancellationToken);
    }

    private async ValueTask<BTCMarketsOrder[]> LoadOrdersAsync(string marketId,
        DateTime? from, DateTime? to, int maximum,
        CancellationToken cancellationToken)
    {
        var upperBound = (to ?? DateTime.UtcNow).ToUniversalTime();
        var lowerBound = from?.ToUniversalTime() ?? upperBound - TimeSpan.FromDays(7);
        var result = new List<BTCMarketsOrder>();
        string before = null;
        while (result.Count < maximum)
        {
            var page = await RestClient.GetOrdersAsync(new()
            {
                MarketId = marketId,
                Status = BTCMarketsOrderQueryStatuses.All,
                Limit = (maximum - result.Count).Min(200).Max(1),
                Before = before,
            }, cancellationToken);
            var items = page?.Items ?? [];
            if (items.Length == 0)
                break;
            result.AddRange(items.Where(order => order is not null &&
                GetOrderTime(order) >= lowerBound &&
                GetOrderTime(order) <= upperBound));
            var earliest = items.Min(GetOrderTime);
            if (earliest <= lowerBound || page.Before.IsEmpty() ||
                page.Before.EqualsIgnoreCase(before))
                break;
            before = page.Before;
        }
        return [.. result.Where(static order => !order.OrderId.IsEmpty())
            .GroupBy(static order => order.OrderId,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(GetOrderTime)
            .TakeLast(maximum)];
    }

    private async ValueTask<BTCMarketsOrder[]> LoadOpenOrdersAsync(
        string marketId, CancellationToken cancellationToken)
    {
        var result = new List<BTCMarketsOrder>();
        string before = null;
        while (true)
        {
            var page = await RestClient.GetOrdersAsync(new()
            {
                MarketId = marketId,
                Status = BTCMarketsOrderQueryStatuses.Open,
                Limit = 200,
                Before = before,
            }, cancellationToken);
            var items = page?.Items ?? [];
            if (items.Length == 0)
                break;
            result.AddRange(items.Where(static order => order is not null));
            if (items.Length < 200 || page.Before.IsEmpty() ||
                page.Before.EqualsIgnoreCase(before))
                break;
            before = page.Before;
        }
        return [.. result.Where(static order => !order.OrderId.IsEmpty())
            .GroupBy(static order => order.OrderId,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())];
    }

    private async ValueTask<BTCMarketsUserTrade[]> LoadUserTradesAsync(
        string marketId, string orderId, DateTime? from, DateTime? to, int maximum,
        CancellationToken cancellationToken)
    {
        var upperBound = (to ?? DateTime.UtcNow).ToUniversalTime();
        var lowerBound = from?.ToUniversalTime() ?? upperBound - TimeSpan.FromDays(7);
        var result = new List<BTCMarketsUserTrade>();
        string before = null;
        while (result.Count < maximum)
        {
            var page = await RestClient.GetUserTradesAsync(new()
            {
                MarketId = marketId,
                OrderId = orderId,
                Limit = (maximum - result.Count).Min(200).Max(1),
                Before = before,
            }, cancellationToken);
            var items = page?.Items ?? [];
            if (items.Length == 0)
                break;
            result.AddRange(items.Where(trade => trade is not null &&
                GetTime(trade.Timestamp) >= lowerBound &&
                GetTime(trade.Timestamp) <= upperBound));
            var earliest = items.Min(trade => GetTime(trade.Timestamp));
            if (earliest <= lowerBound || page.Before.IsEmpty() ||
                page.Before.EqualsIgnoreCase(before))
                break;
            before = page.Before;
        }
        return [.. result.Where(static trade => !trade.Id.IsEmpty())
            .GroupBy(static trade => trade.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static trade => trade.Timestamp)
            .TakeLast(maximum)];
    }

    private ValueTask SendBalanceAsync(BTCMarketsBalance balance,
        long transactionId, bool force, CancellationToken cancellationToken)
    {
        if (balance?.AssetName.IsEmpty() != false)
            return default;
        var fingerprint = new BalanceFingerprint(balance.Balance,
            balance.Available, balance.Locked);
        var key = $"{transactionId}:{balance.AssetName}";
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
            SecurityId = balance.AssetName.ToStockSharp(),
            ServerTime = CurrentTime,
            OriginalTransactionId = transactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, balance.Balance, true)
        .TryAdd(PositionChangeTypes.BlockedValue, balance.Locked, true),
            cancellationToken);
    }

    private ValueTask SendOrderAsync(BTCMarketsOrder order,
        long transactionId, bool force, CancellationToken cancellationToken)
    {
        if (order?.OrderId.IsEmpty() != false || order.MarketId.IsEmpty())
            return default;
        var tracked = GetTrackedOrder(order.OrderId, order.ClientOrderId);
        var fingerprint = new OrderFingerprint(order.Status, order.OpenAmount,
            order.Amount, order.Price);
        var key = $"{transactionId}:{order.OrderId}";
        using (_sync.EnterScope())
        {
            if (!force && _orderFingerprints.TryGetValue(key, out var previous) &&
                previous == fingerprint)
                return default;
            _orderFingerprints[key] = fingerprint;
        }
        if (tracked is not null)
        {
            tracked.ExchangeOrderId = order.OrderId;
            TrackOrder(tracked, order.OrderId, order.ClientOrderId);
        }
        var side = order.Side?.ToStockSharp() ?? tracked?.Side ??
            throw new InvalidDataException(
                $"BTC Markets order '{order.OrderId}' has no side.");
        var condition = tracked?.Condition ?? new BTCMarketsOrderCondition
        {
            TriggerPrice = order.TriggerPrice,
            TargetAmount = order.TargetAmount,
            IsTakeProfit = order.OrderType == BTCMarketsOrderTypes.TakeProfit,
            IsSelfTradePrevented =
                order.SelfTrade == BTCMarketsSelfTradeModes.Prevented,
        };
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.MarketId.ToStockSharp(),
            ServerTime = GetOrderTime(order),
            PortfolioName = GetPortfolioName(),
            Side = side,
            OrderVolume = order.Amount ?? tracked?.Volume,
            Balance = order.OpenAmount,
            OrderPrice = order.Price ?? tracked?.Price ?? 0m,
            OrderType = order.OrderType.ToStockSharp(),
            OrderState = order.Status.ToStockSharp(),
            OrderStringId = order.OrderId,
            TransactionId = tracked?.TransactionId ??
                GetTransactionId(order.ClientOrderId),
            OriginalTransactionId = transactionId,
            TimeInForce = order.TimeInForce.ToStockSharp(),
            PostOnly = order.IsPostOnly,
            Condition = condition,
        }, cancellationToken);
    }

    private ValueTask SendUserTradeAsync(BTCMarketsUserTrade trade,
        long transactionId, bool onlyNew, CancellationToken cancellationToken)
    {
        if (trade?.Id.IsEmpty() != false || trade.MarketId.IsEmpty())
            return default;
        var added = AddAccountTrade(trade.Id, transactionId);
        if (onlyNew && !added)
            return default;
        var tracked = GetTrackedOrder(trade.OrderId, trade.ClientOrderId);
        string commissionCurrency = null;
        using (_sync.EnterScope())
            if (_markets.TryGetValue(trade.MarketId, out var market))
                commissionCurrency = market.QuoteAsset;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = trade.MarketId.ToStockSharp(),
            ServerTime = GetTime(trade.Timestamp),
            PortfolioName = GetPortfolioName(),
            Side = trade.Side.ToStockSharp(),
            OrderStringId = trade.OrderId,
            TradeStringId = trade.Id,
            TradePrice = trade.Price,
            TradeVolume = trade.Amount,
            Commission = trade.Fee,
            CommissionCurrency = commissionCurrency,
            TransactionId = tracked?.TransactionId ??
                GetTransactionId(trade.ClientOrderId),
            OriginalTransactionId = transactionId,
        }, cancellationToken);
    }

    private async ValueTask OnSocketOrderChangedAsync(
        BTCMarketsSocketOrderChange update, CancellationToken cancellationToken)
    {
        if (update?.OrderId.IsEmpty() != false || update.MarketId.IsEmpty())
            return;
        var tracked = GetTrackedOrder(update.OrderId, update.ClientOrderId);
        if (tracked is not null)
        {
            tracked.ExchangeOrderId = update.OrderId;
            TrackOrder(tracked, update.OrderId, update.ClientOrderId);
        }
        var order = new BTCMarketsOrder
        {
            OrderId = update.OrderId,
            ClientOrderId = update.ClientOrderId,
            MarketId = update.MarketId,
            Side = update.Side,
            OrderType = update.OrderType,
            CreationTime = update.Timestamp,
            Price = tracked?.Price,
            Amount = tracked?.Volume,
            OpenAmount = update.OpenVolume,
            Status = update.Status,
            TriggerPrice = tracked?.Condition?.TriggerPrice,
            TargetAmount = tracked?.Condition?.TargetAmount,
        };
        (long Id, OrderSubscription Subscription)[] subscriptions;
        long[] portfolios;
        using (_sync.EnterScope())
        {
            subscriptions = [.. _orderSubscriptions
                .Where(pair => MatchesOrder(pair.Value, null, null, order))
                .Select(static pair => (pair.Key, pair.Value))];
            portfolios = [.. _portfolioSubscriptions];
        }
        foreach (var item in subscriptions)
        {
            await SendOrderAsync(order, item.Id, false, cancellationToken);
            foreach (var trade in update.Trades ?? [])
                await SendSocketUserTradeAsync(update, trade, item.Id,
                    cancellationToken);
        }
        foreach (var transactionId in portfolios)
            await SendPortfolioSnapshotAsync(transactionId, false,
                cancellationToken);
    }

    private async ValueTask OnSocketFundChangedAsync(
        BTCMarketsSocketFundChange update, CancellationToken cancellationToken)
    {
        _ = update;
        long[] portfolios;
        using (_sync.EnterScope())
            portfolios = [.. _portfolioSubscriptions];
        foreach (var transactionId in portfolios)
            await SendPortfolioSnapshotAsync(transactionId, false,
                cancellationToken);
    }

    private ValueTask SendSocketUserTradeAsync(
        BTCMarketsSocketOrderChange order, BTCMarketsSocketOrderTrade trade,
        long transactionId, CancellationToken cancellationToken)
    {
        if (trade?.TradeId.IsEmpty() != false ||
            !AddAccountTrade(trade.TradeId, transactionId))
            return default;
        var tracked = GetTrackedOrder(order.OrderId, order.ClientOrderId);
        string commissionCurrency = null;
        using (_sync.EnterScope())
            if (_markets.TryGetValue(order.MarketId, out var market))
                commissionCurrency = market.QuoteAsset;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = order.MarketId.ToStockSharp(),
            ServerTime = GetTime(order.Timestamp),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderStringId = order.OrderId,
            TradeStringId = trade.TradeId,
            TradePrice = trade.Price,
            TradeVolume = trade.Volume,
            Commission = trade.Fee,
            CommissionCurrency = commissionCurrency,
            TransactionId = tracked?.TransactionId ??
                GetTransactionId(order.ClientOrderId),
            OriginalTransactionId = transactionId,
        }, cancellationToken);
    }

    private ValueTask SendCanceledOrderAsync(BTCMarketsOrderReference order,
        string marketId, long transactionId,
        CancellationToken cancellationToken)
    {
        if (order?.OrderId.IsEmpty() != false)
            return default;
        var tracked = GetTrackedOrder(order.OrderId, order.ClientOrderId);
        marketId ??= tracked?.MarketId;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = marketId.IsEmpty() ? default : marketId.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            OrderStringId = order.OrderId,
            OrderState = OrderStates.Done,
            Balance = 0m,
            TransactionId = tracked?.TransactionId ??
                GetTransactionId(order.ClientOrderId),
            OriginalTransactionId = transactionId,
        }, cancellationToken);
    }

    private bool MatchesOrder(OrderSubscription subscription, DateTime? from,
        DateTime? to, BTCMarketsOrder order)
    {
        if (order is null)
            return false;
        if (!subscription.MarketId.IsEmpty() &&
            !subscription.MarketId.EqualsIgnoreCase(order.MarketId))
            return false;
        if (!subscription.OrderId.IsEmpty() &&
            !subscription.OrderId.EqualsIgnoreCase(order.OrderId) &&
            !subscription.OrderId.EqualsIgnoreCase(order.ClientOrderId))
            return false;
        if (subscription.Side is Sides side &&
            order.Side?.ToStockSharp() != side)
            return false;
        var time = GetOrderTime(order);
        return (from is null || time >= from.Value.ToUniversalTime()) &&
            (to is null || time <= to.Value.ToUniversalTime());
    }

    private bool MatchesTrade(OrderSubscription subscription, DateTime? from,
        DateTime? to, BTCMarketsUserTrade trade)
    {
        if (trade is null)
            return false;
        if (!subscription.MarketId.IsEmpty() &&
            !subscription.MarketId.EqualsIgnoreCase(trade.MarketId))
            return false;
        if (!subscription.OrderId.IsEmpty() &&
            !subscription.OrderId.EqualsIgnoreCase(trade.OrderId) &&
            !subscription.OrderId.EqualsIgnoreCase(trade.ClientOrderId))
            return false;
        if (subscription.Side is Sides side && trade.Side.ToStockSharp() != side)
            return false;
        var time = GetTime(trade.Timestamp);
        return (from is null || time >= from.Value.ToUniversalTime()) &&
            (to is null || time <= to.Value.ToUniversalTime());
    }

    private DateTime GetOrderTime(BTCMarketsOrder order)
        => order.CreationTime == default
            ? CurrentTime
            : order.CreationTime.ToUtcTime();

    private static string CreateClientOrderId(long transactionId,
        string userOrderId)
    {
        var value = userOrderId.IsEmpty()
            ? $"S2-{transactionId}"
            : userOrderId.Trim();
        if (value.Length > 64 || value.Any(static character =>
            !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            value = $"S2-{transactionId}";
        return value;
    }

    private static void ValidateOrder(BTCMarketsOrder order, string operation)
    {
        if (order?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                $"BTC Markets {operation} an order without returning its ID.");
    }

    private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
