namespace StockSharp.Coinone;

public partial class CoinoneMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var market = GetMarket(regMsg.SecurityId);
        var request = CreateOrderRequest(market, regMsg);
        var result = await RestClient.PlaceOrderAsync(request, cancellationToken);
        if (result?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                "Coinone accepted an order without returning its identifier.");

        var condition = regMsg.Condition as CoinoneOrderCondition;
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            Symbol = market.Symbol,
            ExchangeOrderId = result.OrderId,
            UserOrderId = request.UserOrderId,
            Side = regMsg.Side,
            OrderType = regMsg.OrderType ?? OrderTypes.Limit,
            Volume = regMsg.Volume.Abs(),
            Price = regMsg.Price,
            IsPostOnly = regMsg.PostOnly,
            Condition = condition?.Clone() as CoinoneOrderCondition,
        };
        TrackOrder(tracked, result.OrderId, request.UserOrderId);
        await SendTrackedOrderAsync(tracked, OrderStates.Active,
            tracked.Volume, regMsg.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var identifier = ResolveOrderIdentifier(cancelMsg.OrderId,
            cancelMsg.OrderStringId, "cancellation");
        var tracked = GetTrackedOrder(identifier);
        var market = tracked is not null
            ? GetMarket(tracked.Symbol)
            : cancelMsg.SecurityId.SecurityCode.IsEmpty()
                ? throw new InvalidOperationException(
                    "Coinone cancellation requires the order market when the order was not registered by this adapter.")
                : GetMarket(cancelMsg.SecurityId);
        var orderId = tracked?.ExchangeOrderId.IsEmpty(identifier);
        var result = await RestClient.CancelOrderAsync(new()
        {
            OrderId = orderId,
            QuoteCurrency = market.QuoteCurrency,
            TargetCurrency = market.TargetCurrency,
        }, cancellationToken);
        await SendCanceledOrderAsync(result, market, tracked,
            cancelMsg.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
            throw new NotSupportedException(
                "Coinone spot bulk cancellation cannot close positions.");
        var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(cancelMsg.SecurityId);
        var response = await RestClient.GetActiveOrdersAsync(new()
        {
            QuoteCurrency = market?.QuoteCurrency,
            TargetCurrency = market?.TargetCurrency,
        }, cancellationToken);
        var selected = (response.ActiveOrders ?? []).Where(order =>
            order?.OrderId.IsEmpty() == false &&
            (cancelMsg.Side is null || order.Side.ToStockSharp() == cancelMsg.Side))
            .ToArray();

        if (cancelMsg.Side is null)
        {
            foreach (var group in selected.GroupBy(order =>
                CoinoneExtensions.ToSymbol(order.TargetCurrency,
                    order.QuoteCurrency), StringComparer.OrdinalIgnoreCase))
            {
                var groupMarket = GetMarket(group.Key);
                _ = await RestClient.CancelAllAsync(new()
                {
                    QuoteCurrency = groupMarket.QuoteCurrency,
                    TargetCurrency = groupMarket.TargetCurrency,
                }, cancellationToken);
                foreach (var order in group)
                    await SendOrderAsync(order, OrderStates.Done, 0m,
                        cancelMsg.TransactionId, cancellationToken);
            }
            return;
        }

        foreach (var order in selected)
        {
            var orderMarket = GetMarket(order.QuoteCurrency, order.TargetCurrency);
            var result = await RestClient.CancelOrderAsync(new()
            {
                OrderId = order.OrderId,
                QuoteCurrency = orderMarket.QuoteCurrency,
                TargetCurrency = orderMarket.TargetCurrency,
            }, cancellationToken);
            await SendCanceledOrderAsync(result, orderMarket,
                GetTrackedOrder(order.OrderId), cancelMsg.TransactionId,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(
        PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
        EnsurePrivateReady();
        if (!lookupMsg.IsSubscribe)
        {
            using (_sync.EnterScope())
                _portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
            return;
        }

        var portfolio = GetPortfolioName();
        if (lookupMsg.PortfolioName.IsEmpty() ||
            lookupMsg.PortfolioName.EqualsIgnoreCase(portfolio))
        {
            await SendOutMessageAsync(new PortfolioMessage
            {
                PortfolioName = portfolio,
                BoardCode = BoardCodes.Coinone,
                OriginalTransactionId = lookupMsg.TransactionId,
            }, cancellationToken);
            await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
                cancellationToken);
        }
        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
        if (lookupMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
                cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _portfolioSubscriptions.Add(lookupMsg.TransactionId);
    }

    /// <inheritdoc />
    protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
        EnsurePrivateReady();
        if (!statusMsg.IsSubscribe)
        {
            using (_sync.EnterScope())
                _orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
            return;
        }
        if (statusMsg.Count is <= 0)
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }

        var market = statusMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(statusMsg.SecurityId);
        var identifier = statusMsg.HasOrderId()
            ? ResolveOrderIdentifier(statusMsg.OrderId, statusMsg.OrderStringId,
                "lookup")
            : null;
        var tracked = GetTrackedOrder(identifier);
        if (market is null && tracked is not null)
            market = GetMarket(tracked.Symbol);
        var maximum = (statusMsg.Count ?? 500).Min(1000).Max(1).To<int>();

        if (!identifier.IsEmpty())
        {
            if (market is null)
                throw new InvalidOperationException(
                    "Coinone order lookup requires the order market when the order was not registered by this adapter.");
            var response = await RestClient.GetOrderAsync(new()
            {
                OrderId = tracked?.ExchangeOrderId.IsEmpty(identifier),
                QuoteCurrency = market.QuoteCurrency,
                TargetCurrency = market.TargetCurrency,
            }, cancellationToken);
            if (response.Order is null)
                throw new InvalidDataException(
                    "Coinone returned no matching order.");
            await SendOrderAsync(response.Order, null, null,
                statusMsg.TransactionId, cancellationToken);
        }
        else
        {
            var activeResponse = await RestClient.GetActiveOrdersAsync(new()
            {
                QuoteCurrency = market?.QuoteCurrency,
                TargetCurrency = market?.TargetCurrency,
            }, cancellationToken);
            var activeOrderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var order in (activeResponse.ActiveOrders ?? [])
                .Where(order => order is not null &&
                    (statusMsg.Side is null ||
                        order.Side.ToStockSharp() == statusMsg.Side))
                .Take(maximum))
            {
                activeOrderIds.Add(order.OrderId);
                await SendOrderAsync(order, OrderStates.Active,
                    order.RemainingQuantity, statusMsg.TransactionId,
                    cancellationToken);
            }

            var trades = await DownloadCompletedTradesAsync(market, statusMsg,
                maximum, cancellationToken);
            foreach (var group in trades.Where(trade => trade is not null &&
                !activeOrderIds.Contains(trade.OrderId) &&
                (statusMsg.Side is null ||
                    (trade.IsAsk ? Sides.Sell : Sides.Buy) == statusMsg.Side))
                .GroupBy(static trade => trade.OrderId,
                    StringComparer.OrdinalIgnoreCase))
                await SendCompletedOrderAsync(group, statusMsg.TransactionId,
                    cancellationToken);
            foreach (var trade in trades)
                if (statusMsg.Side is null ||
                    (trade.IsAsk ? Sides.Sell : Sides.Buy) == statusMsg.Side)
                    await SendAccountTradeAsync(trade, statusMsg.TransactionId,
                        cancellationToken);
        }

        await SendSubscriptionResultAsync(statusMsg, cancellationToken);
        if (statusMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
                cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _orderSubscriptions[statusMsg.TransactionId] = new()
            {
                Symbol = market?.Symbol,
                OrderId = identifier,
                Side = statusMsg.Side,
            };
    }

    private CoinonePlaceOrderRequest CreateOrderRequest(MarketDefinition market,
        OrderRegisterMessage message)
    {
        if (market.MaintenanceStatus != CoinoneMaintenanceStatuses.Normal ||
            market.TradeStatus == CoinoneTradeStatuses.Disabled ||
            message.Side == Sides.Buy &&
                market.TradeStatus == CoinoneTradeStatuses.BuyDisabled ||
            message.Side == Sides.Sell &&
                market.TradeStatus == CoinoneTradeStatuses.SellDisabled)
            throw new InvalidOperationException(
                $"Coinone market '{market.Symbol}' is not available for {message.Side} orders.");
        if (message.VisibleVolume is > 0 &&
            message.VisibleVolume != message.Volume.Abs())
            throw new NotSupportedException(
                "Coinone does not document iceberg orders.");
        if (message.TillDate is not null)
            throw new NotSupportedException("Coinone does not support GTD orders.");
        if (message.TimeInForce is not null and not TimeInForce.PutInQueue)
            throw new NotSupportedException(
                "Coinone does not support IOC or FOK order policies.");

        var orderType = message.OrderType ?? OrderTypes.Limit;
        if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
            OrderTypes.Conditional))
            throw new NotSupportedException(
                LocalizedStrings.OrderUnsupportedType.Put(orderType,
                    message.TransactionId));
        var condition = message.Condition as CoinoneOrderCondition;
        CoinonePrivateOrderTypes nativeType;
        decimal? price = null;
        decimal? quantity = null;
        decimal? amount = null;
        decimal? triggerPrice = null;
        decimal? limitPrice = null;
        bool? isPostOnly = null;

        switch (orderType)
        {
            case OrderTypes.Limit:
                if (message.Price <= 0)
                    throw new InvalidOperationException(
                        "Coinone limit orders require a positive price.");
                nativeType = CoinonePrivateOrderTypes.Limit;
                price = message.Price;
                quantity = message.Volume.Abs();
                isPostOnly = message.PostOnly == true;
                break;
            case OrderTypes.Market:
                if (message.PostOnly == true)
                    throw new InvalidOperationException(
                        "A market order cannot be post-only.");
                nativeType = CoinonePrivateOrderTypes.Market;
                limitPrice = condition?.LimitPrice;
                if (message.Side == Sides.Buy)
                {
                    amount = condition?.QuoteAmount;
                    if (amount is not > 0)
                        throw new InvalidOperationException(
                            "Coinone market buy orders require a positive CoinoneOrderCondition.QuoteAmount.");
                }
                else
                    quantity = message.Volume.Abs();
                break;
            default:
                if (message.Price <= 0 || condition?.TriggerPrice is not > 0)
                    throw new InvalidOperationException(
                        "Coinone stop-limit orders require positive limit and trigger prices.");
                if (message.PostOnly == true)
                    throw new InvalidOperationException(
                        "Coinone stop-limit orders cannot be post-only.");
                nativeType = CoinonePrivateOrderTypes.StopLimit;
                price = message.Price;
                quantity = message.Volume.Abs();
                triggerPrice = condition.TriggerPrice;
                break;
        }

        var publicType = nativeType switch
        {
            CoinonePrivateOrderTypes.Market => CoinoneOrderTypes.Market,
            CoinonePrivateOrderTypes.StopLimit => CoinoneOrderTypes.StopLimit,
            _ => CoinoneOrderTypes.Limit,
        };
        if (!market.OrderTypes.Contains(publicType))
            throw new NotSupportedException(
                $"Coinone does not support {nativeType} for '{market.Symbol}'.");
        if (price is decimal orderPrice)
        {
            ValidateRange(orderPrice, market.MinimumPrice, market.MaximumPrice,
                "price", market.Symbol);
            ValidateStep(orderPrice, market.PriceStep, "price", market.Symbol);
        }
        if (triggerPrice is decimal stopPrice)
            ValidateRange(stopPrice, market.MinimumPrice, market.MaximumPrice,
                "trigger price", market.Symbol);
        if (limitPrice is decimal boundaryPrice)
            ValidateRange(boundaryPrice, market.MinimumPrice, market.MaximumPrice,
                "market-order price limit", market.Symbol);
        if (quantity is decimal orderQuantity)
        {
            if (orderQuantity <= 0)
                throw new InvalidOperationException(
                    "Coinone order volume must be positive.");
            ValidateRange(orderQuantity, market.MinimumQuantity,
                market.MaximumQuantity, "volume", market.Symbol);
            ValidateStep(orderQuantity, market.QuantityStep, "volume",
                market.Symbol);
        }
        var notional = amount ??
            (price is > 0 && quantity is > 0 ? price * quantity : null);
        if (notional is decimal orderAmount)
            ValidateRange(orderAmount, market.MinimumOrderAmount,
                market.MaximumOrderAmount, "order amount", market.Symbol);

        return new()
        {
            Side = message.Side.ToCoinone(),
            QuoteCurrency = market.QuoteCurrency,
            TargetCurrency = market.TargetCurrency,
            Type = nativeType,
            Price = price?.ToWire(),
            Quantity = quantity?.ToWire(),
            Amount = amount?.ToWire(),
            IsPostOnly = isPostOnly,
            LimitPrice = limitPrice?.ToWire(),
            TriggerPrice = triggerPrice?.ToWire(),
            UserOrderId = CoinoneExtensions.CreateUserOrderId(
                message.TransactionId, message.UserOrderId),
        };
    }

    private static void ValidateRange(decimal value, decimal minimum,
        decimal maximum, string name, string symbol)
    {
        if (minimum > 0 && value < minimum)
            throw new InvalidOperationException(
                $"Coinone {name} must be at least {minimum} for '{symbol}'.");
        if (maximum > 0 && value > maximum)
            throw new InvalidOperationException(
                $"Coinone {name} must not exceed {maximum} for '{symbol}'.");
    }

    private static void ValidateStep(decimal value, decimal step, string name,
        string symbol)
    {
        if (step > 0 && value % step != 0)
            throw new InvalidOperationException(
                $"Coinone {name} must be aligned to step {step} for '{symbol}'.");
    }

    private async ValueTask<CoinoneCompletedTrade[]> DownloadCompletedTradesAsync(
        MarketDefinition market, OrderStatusMessage message, int maximum,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var to = (message.To ?? now).ToUniversalTime();
        if (to > now)
            to = now;
        var from = message.From?.ToUniversalTime() ?? to - TimeSpan.FromDays(7);
        if (to - from > TimeSpan.FromDays(90))
            from = to - TimeSpan.FromDays(90);
        var request = new CoinoneCompletedOrdersRequest
        {
            Size = maximum.Min(100).Max(1),
            FromTimestamp = new DateTimeOffset(from).ToUnixTimeMilliseconds(),
            ToTimestamp = new DateTimeOffset(to).ToUnixTimeMilliseconds(),
            QuoteCurrency = market?.QuoteCurrency,
            TargetCurrency = market?.TargetCurrency,
        };
        var trades = new List<CoinoneCompletedTrade>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (trades.Count < maximum)
        {
            var response = await RestClient.GetCompletedOrdersAsync(request,
                market is null, cancellationToken);
            var page = response.CompletedOrders ?? [];
            foreach (var trade in page)
                if (trade?.TradeId.IsEmpty() == false && seen.Add(trade.TradeId))
                    trades.Add(trade);
            if (page.Length < request.Size || trades.Count >= maximum)
                break;
            var cursor = page.LastOrDefault()?.TradeId;
            if (cursor.IsEmpty() || cursor.EqualsIgnoreCase(request.ToTradeId))
                break;
            request.ToTradeId = cursor;
        }
        return [.. trades.Take(maximum)];
    }

    private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var response = await RestClient.GetBalancesAsync(cancellationToken);
        foreach (var balance in response.Balances ?? [])
            await SendBalanceAsync(balance.Currency, balance.Available,
                balance.Locked, balance.AveragePrice, originalTransactionId,
                CurrentTime, cancellationToken);
    }

    private ValueTask SendBalanceAsync(string currency, decimal available,
        decimal locked, decimal? averagePrice, long originalTransactionId,
        DateTime serverTime, CancellationToken cancellationToken)
    {
        if (currency.IsEmpty())
            return default;
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = GetPortfolioName(),
            SecurityId = new SecurityId
            {
                SecurityCode = currency.NormalizeCurrency(),
                BoardCode = BoardCodes.Coinone,
            },
            ServerTime = serverTime,
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, available, true)
        .TryAdd(PositionChangeTypes.BlockedValue, locked, true)
        .TryAdd(PositionChangeTypes.AveragePrice,
            averagePrice is > 0 ? averagePrice : null, true), cancellationToken);
    }

    private ValueTask SendTrackedOrderAsync(TrackedOrder tracked,
        OrderStates state, decimal balance, long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var market = GetMarket(tracked.Symbol);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                market.QuoteCurrency),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = tracked.Side,
            OrderVolume = tracked.Volume,
            Balance = balance,
            OrderPrice = tracked.Price,
            OrderType = tracked.OrderType,
            OrderState = state,
            OrderStringId = tracked.ExchangeOrderId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            PostOnly = tracked.IsPostOnly,
            Condition = tracked.Condition,
        }, cancellationToken);
    }

    private async ValueTask SendOrderAsync(CoinoneOrder order,
        OrderStates? state, decimal? balance, long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (order?.OrderId.IsEmpty() != false || order.QuoteCurrency.IsEmpty() ||
            order.TargetCurrency.IsEmpty())
            return;
        var market = GetMarket(order.QuoteCurrency, order.TargetCurrency);
        var tracked = GetTrackedOrder(order.OrderId) ??
            GetTrackedOrder(order.UserOrderId) ?? new TrackedOrder
            {
                Symbol = market.Symbol,
                ExchangeOrderId = order.OrderId,
                UserOrderId = order.UserOrderId,
                Side = order.Side.ToStockSharp(),
                OrderType = order.Type.ToStockSharp(),
                Volume = order.OriginalQuantity ?? 0m,
                Price = order.Price ?? 0m,
                Condition = CreateCondition(order.Type, order.TriggerPrice,
                    order.LimitPrice, order.OriginalAmount),
            };
        tracked.ExchangeOrderId = order.OrderId;
        TrackOrder(tracked, order.OrderId, order.UserOrderId);
        var orderState = state ?? order.Status.ToStockSharp();
        var volume = order.OriginalQuantity ?? tracked.Volume;
        var remaining = balance ?? order.RemainingQuantity ??
            (orderState == OrderStates.Active
                ? (volume - order.ExecutedQuantity - order.CanceledQuantity).Max(0m)
                : 0m);
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                market.QuoteCurrency),
            ServerTime = (order.UpdatedAt > 0 ? order.UpdatedAt : order.OrderedAt)
                .FromCoinoneTimestamp(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderVolume = volume,
            Balance = remaining,
            OrderPrice = order.Price ?? tracked.Price,
            AveragePrice = order.AverageExecutedPrice > 0
                ? order.AverageExecutedPrice
                : null,
            OrderType = order.Type.ToStockSharp(),
            OrderState = orderState,
            OrderStringId = order.OrderId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            Condition = tracked.Condition ?? CreateCondition(order.Type,
                order.TriggerPrice, order.LimitPrice, order.OriginalAmount),
            Commission = order.Fee > 0 ? order.Fee : null,
        }, cancellationToken);
    }

    private async ValueTask SendCanceledOrderAsync(
        CoinoneCancelOrderResponse response, MarketDefinition market,
        TrackedOrder tracked, long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (response?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                "Coinone returned no canceled order identifier.");
        tracked ??= GetTrackedOrder(response.OrderId);
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                market.QuoteCurrency),
            ServerTime = response.CanceledAt.FromCoinoneTimestamp(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = response.Side.ToStockSharp(),
            OrderVolume = response.OriginalQuantity > 0
                ? response.OriginalQuantity
                : tracked?.Volume,
            Balance = response.RemainingQuantity,
            OrderPrice = response.Price > 0
                ? response.Price
                : tracked?.Price ?? 0m,
            AveragePrice = response.AveragePrice > 0
                ? response.AveragePrice
                : null,
            OrderType = tracked?.OrderType ?? OrderTypes.Limit,
            OrderState = OrderStates.Done,
            OrderStringId = response.OrderId,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = originalTransactionId,
            Condition = tracked?.Condition,
            Commission = response.Fee > 0 ? response.Fee : null,
        }, cancellationToken);
    }

    private async ValueTask SendCompletedOrderAsync(
        IEnumerable<CoinoneCompletedTrade> completedTrades,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        var trades = completedTrades.ToArray();
        var first = trades.FirstOrDefault();
        if (first is null || first.OrderId.IsEmpty())
            return;
        var market = GetMarket(first.QuoteCurrency, first.TargetCurrency);
        var tracked = GetTrackedOrder(first.OrderId);
        var quantity = trades.Sum(static trade => trade.Quantity);
        var average = quantity > 0
            ? trades.Sum(static trade => trade.Price * trade.Quantity) / quantity
            : 0m;
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                market.QuoteCurrency),
            ServerTime = trades.Max(static trade => trade.Timestamp)
                .FromCoinoneTimestamp(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = first.IsAsk ? Sides.Sell : Sides.Buy,
            OrderVolume = tracked?.Volume > 0 ? tracked.Volume : quantity,
            Balance = 0m,
            OrderPrice = tracked?.Price ?? average,
            AveragePrice = average > 0 ? average : null,
            OrderType = first.OrderType.ToStockSharp(),
            OrderState = OrderStates.Done,
            OrderStringId = first.OrderId,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = originalTransactionId,
            Condition = tracked?.Condition,
            Commission = trades.Sum(static trade => trade.Fee),
        }, cancellationToken);
    }

    private ValueTask SendAccountTradeAsync(CoinoneCompletedTrade trade,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        if (trade is null || trade.TradeId.IsEmpty() || trade.OrderId.IsEmpty() ||
            trade.Price <= 0 || trade.Quantity <= 0 ||
            !AddAccountTrade(trade.TradeId))
            return default;
        var market = GetMarket(trade.QuoteCurrency, trade.TargetCurrency);
        var tracked = GetTrackedOrder(trade.OrderId);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                market.QuoteCurrency),
            ServerTime = trade.Timestamp.FromCoinoneTimestamp(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = trade.IsAsk ? Sides.Sell : Sides.Buy,
            OrderStringId = trade.OrderId,
            TradeStringId = trade.TradeId,
            TradePrice = trade.Price,
            TradeVolume = trade.Quantity,
            Commission = trade.Fee > 0 ? trade.Fee : null,
            IsMarketMaker = trade.IsMaker,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = originalTransactionId,
        }, cancellationToken);
    }

    private async ValueTask OnMyOrderAsync(CoinoneMyOrderUpdate update,
        CancellationToken cancellationToken)
    {
        if (update?.OrderId.IsEmpty() != false || update.QuoteCurrency.IsEmpty() ||
            update.TargetCurrency.IsEmpty())
            return;
        var market = GetMarket(update.QuoteCurrency, update.TargetCurrency);
        var tracked = GetTrackedOrder(update.OrderId) ??
            GetTrackedOrder(update.UserOrderId) ?? new TrackedOrder
            {
                TransactionId = ParseTransactionId(update.UserOrderId),
                Symbol = market.Symbol,
                ExchangeOrderId = update.OrderId,
                UserOrderId = update.UserOrderId,
                Side = update.Side.ToStockSharp(),
                OrderType = update.Type.ToStockSharp(),
                Volume = update.OrderQuantity ?? 0m,
                Price = update.OrderPrice ?? 0m,
            };
        tracked.ExchangeOrderId = update.OrderId;
        TrackOrder(tracked, update.OrderId, update.UserOrderId);
        var side = update.Side.ToStockSharp();
        KeyValuePair<long, OrderSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _orderSubscriptions.Where(pair =>
                MatchesOrderSubscription(pair.Value, market.Symbol,
                    update.OrderId, update.UserOrderId, side))];
        var state = update.Status.ToStockSharp();
        var serverTime = (update.ExecutedTimestamp ?? update.OrderTimestamp ??
            update.Timestamp).FromCoinoneTimestamp(CurrentTime);
        foreach (var pair in subscriptions)
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                    market.QuoteCurrency),
                ServerTime = serverTime,
                PortfolioName = GetPortfolioName(),
                Side = side,
                OrderVolume = update.OrderQuantity ?? tracked.Volume,
                Balance = update.RemainingQuantity ??
                    (state == OrderStates.Active ? tracked.Volume : 0m),
                OrderPrice = update.OrderPrice ?? tracked.Price,
                OrderType = update.Type.ToStockSharp(),
                OrderState = state,
                OrderStringId = update.OrderId,
                TransactionId = tracked.TransactionId,
                OriginalTransactionId = pair.Key,
                PostOnly = tracked.IsPostOnly,
                Condition = tracked.Condition,
                Commission = update.ExecutedFee,
                Error = state == OrderStates.Failed
                    ? new InvalidOperationException(
                        "Coinone canceled the post-only order because it would execute immediately.")
                    : null,
            }, cancellationToken);

        if (update.TradeId.IsEmpty() || update.ExecutedPrice is not > 0 ||
            update.ExecutedQuantity is not > 0 ||
            !AddAccountTrade(update.TradeId))
            return;
        foreach (var pair in subscriptions)
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                SecurityId = CoinoneExtensions.ToStockSharp(market.TargetCurrency,
                    market.QuoteCurrency),
                ServerTime = serverTime,
                PortfolioName = GetPortfolioName(),
                Side = side,
                OrderStringId = update.OrderId,
                TradeStringId = update.TradeId,
                TradePrice = update.ExecutedPrice,
                TradeVolume = update.ExecutedQuantity,
                Commission = update.ExecutedFee,
                IsMarketMaker = update.IsMaker,
                TransactionId = tracked.TransactionId,
                OriginalTransactionId = pair.Key,
            }, cancellationToken);
    }

    private async ValueTask OnMyAssetAsync(CoinoneMyAssetUpdate update,
        CancellationToken cancellationToken)
    {
        if (update is null)
            return;
        long[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _portfolioSubscriptions];
        var serverTime = update.Timestamp.FromCoinoneTimestamp(CurrentTime);
        foreach (var subscriptionId in subscriptions)
            foreach (var asset in update.Assets ?? [])
                await SendBalanceAsync(asset.Currency, asset.Available,
                    asset.Locked, null, subscriptionId, serverTime,
                    cancellationToken);
    }

    private static CoinoneOrderCondition CreateCondition(
        CoinonePrivateOrderTypes type, decimal? triggerPrice,
        decimal? limitPrice, decimal? quoteAmount)
        => type == CoinonePrivateOrderTypes.StopLimit || triggerPrice is > 0 ||
            limitPrice is > 0 || quoteAmount is > 0
                ? new()
                {
                    TriggerPrice = triggerPrice,
                    LimitPrice = limitPrice,
                    QuoteAmount = quoteAmount,
                }
                : null;

    private static bool MatchesOrderSubscription(OrderSubscription subscription,
        string symbol, string orderId, string userOrderId, Sides side)
        => (subscription.Symbol.IsEmpty() ||
            subscription.Symbol.EqualsIgnoreCase(symbol)) &&
            (subscription.OrderId.IsEmpty() ||
            subscription.OrderId.EqualsIgnoreCase(orderId) ||
            subscription.OrderId.EqualsIgnoreCase(userOrderId)) &&
            (subscription.Side is null || subscription.Side == side);

    private static long ParseTransactionId(string userOrderId)
    {
        if (userOrderId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) ==
            true && long.TryParse(userOrderId.AsSpan(3), NumberStyles.None,
            CultureInfo.InvariantCulture, out var transactionId))
            return transactionId;
        return 0;
    }

    private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
