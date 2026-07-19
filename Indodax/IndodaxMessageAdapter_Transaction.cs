namespace StockSharp.Indodax;

public partial class IndodaxMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var market = GetMarket(regMsg.SecurityId);
        var orderType = (regMsg.OrderType ?? OrderTypes.Limit).ToIndodax();
        var volume = regMsg.Volume.Abs();
        var condition = regMsg.Condition as IndodaxOrderCondition ?? new();
        var quoteAmount = condition.QuoteAmount;

        if (orderType == IndodaxOrderTypes.Limit)
        {
            if (regMsg.Price <= 0 || volume <= 0)
                throw new InvalidOperationException(
                    "An Indodax limit order requires positive price and volume.");
            if (quoteAmount is not null)
                throw new InvalidOperationException(
                    "Quote amount is supported for market buy orders only.");
        }
        else if (regMsg.Side == Sides.Buy)
        {
            if (quoteAmount is null or <= 0)
                throw new InvalidOperationException(
                    "An Indodax market buy requires QuoteAmount.");
        }
        else if (volume <= 0)
            throw new InvalidOperationException(
                "An Indodax market sell requires positive volume.");

        if (regMsg.PostOnly == true && orderType != IndodaxOrderTypes.Limit)
            throw new InvalidOperationException(
                "Indodax maker-only execution is supported for limit orders only.");
        if (regMsg.TimeInForce is not null and not TimeInForce.PutInQueue)
            throw new NotSupportedException(
                "Indodax documents GTC and maker-only limit execution only.");
        if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
            throw new NotSupportedException(
                "Indodax does not document iceberg orders.");
        if (regMsg.TillDate is not null)
            throw new NotSupportedException(
                "Indodax does not support GTD orders.");

        market.Pair.ValidateOrder(
            orderType == IndodaxOrderTypes.Limit ? regMsg.Price : 0m,
            orderType == IndodaxOrderTypes.Market && regMsg.Side == Sides.Buy
                ? 0m
                : volume,
            quoteAmount);

        var clientOrderId = GetClientOrderId(regMsg.TransactionId,
            regMsg.UserOrderId);
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            PairId = market.PairId,
            ClientOrderId = clientOrderId,
            Side = regMsg.Side,
            OrderType = orderType.ToStockSharp(),
            Volume = volume,
            Price = regMsg.Price,
            IsPostOnly = regMsg.PostOnly == true,
            QuoteAmount = quoteAmount,
        };
        TrackOrder(tracked, clientOrderId,
            regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));

        var parameters = new IndodaxTradeParameters
        {
            Pair = market.TapiPair,
            Side = regMsg.Side.ToIndodax(),
            OrderType = orderType,
            Price = orderType == IndodaxOrderTypes.Limit ? regMsg.Price : null,
            AmountCurrency = orderType == IndodaxOrderTypes.Market &&
                regMsg.Side == Sides.Buy
                    ? market.QuoteCurrency
                    : market.BaseCurrency,
            Amount = orderType == IndodaxOrderTypes.Market &&
                regMsg.Side == Sides.Buy
                    ? quoteAmount.Value
                    : volume,
            ClientOrderId = clientOrderId,
            IsMakerOnly = regMsg.PostOnly == true,
        };
        var result = await PlaceWithReconciliationAsync(parameters,
            cancellationToken);
        if (result?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                "Indodax accepted an order without returning its order ID.");
        tracked.ExchangeOrderId = result.OrderId;
        TrackOrder(tracked, result.OrderId, result.ClientOrderId, clientOrderId);
        await SendTrackedOrderAsync(tracked, OrderStates.Active, volume,
            regMsg.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var identifier = ResolveOrderIdentifier(cancelMsg.OrderId,
            cancelMsg.OrderStringId, "cancellation");
        var tracked = GetTrackedOrder(identifier);
        var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? tracked is null ? null : GetMarket(tracked.PairId)
            : GetMarket(cancelMsg.SecurityId);
        if (market is null)
            throw new InvalidOperationException(
                "Indodax cancellation requires the order market.");

        if (tracked is not null &&
            identifier.EqualsIgnoreCase(tracked.ClientOrderId) &&
            !identifier.EqualsIgnoreCase(tracked.ExchangeOrderId))
            await RestClient.CancelByClientIdAsync(identifier,
                cancellationToken);
        else
            await RestClient.CancelOrderAsync(new()
            {
                Pair = market.TapiPair,
                OrderId = identifier,
                Side = (tracked?.Side ?? cancelMsg.Side ??
                    throw new InvalidOperationException(
                        "Indodax cancellation requires the order side."))
                    .ToIndodax(),
                OrderType = tracked?.OrderType.ToIndodax() ??
                    IndodaxOrderTypes.Limit,
            }, cancellationToken);

        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = market.PairId.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            OrderStringId = tracked?.ExchangeOrderId ?? identifier,
            UserOrderId = tracked?.ClientOrderId,
            OrderState = OrderStates.Done,
            Balance = 0m,
            TransactionId = tracked?.TransactionId ?? 0,
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
                "Indodax spot cancellation cannot close positions.");
        if (cancelMsg.IsStop == true)
            return;

        var requestedMarket = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(cancelMsg.SecurityId);
        var openOrders = await LoadOpenOrdersAsync(requestedMarket,
            cancellationToken);
        foreach (var order in openOrders.Where(order =>
            (cancelMsg.Side is null || order.Side.ToStockSharp() ==
                cancelMsg.Side)))
        {
            var market = GetMarket(order.Pair);
            await RestClient.CancelOrderAsync(new()
            {
                Pair = market.TapiPair,
                OrderId = order.OrderId,
                Side = order.Side,
                OrderType = order.OrderType,
            }, cancellationToken);
            await SendLegacyOrderAsync(order, cancelMsg.TransactionId,
                cancellationToken, OrderStates.Done, 0m);
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
                _portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = GetPortfolioName(),
            BoardCode = BoardCodes.Indodax,
            OriginalTransactionId = lookupMsg.TransactionId,
        }, cancellationToken);
        await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
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
            using (_sync.EnterScope())
                _orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
            return;
        }
        if (statusMsg.Count is <= 0)
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        ValidatePortfolio(statusMsg.PortfolioName);
        var pairId = statusMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(statusMsg.SecurityId).PairId;
        string identifier = null;
        if (statusMsg.HasOrderId())
            identifier = ResolveOrderIdentifier(statusMsg.OrderId,
                statusMsg.OrderStringId, "lookup");
        pairId ??= GetTrackedOrder(identifier)?.PairId;
        var subscription = new OrderSubscription
        {
            PairId = pairId,
            OrderIdentifier = identifier,
            Side = statusMsg.Side,
        };
        var maximum = (statusMsg.Count ?? 1000).Min(5000).Max(1).To<int>();
        await SendOrderSnapshotAsync(statusMsg, subscription, maximum,
            cancellationToken);
        if (statusMsg.IsHistoryOnly())
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _orderSubscriptions[statusMsg.TransactionId] = subscription;
        await SendSubscriptionResultAsync(statusMsg, cancellationToken);
    }

    private async ValueTask<IndodaxPlaceOrderData>
        PlaceWithReconciliationAsync(IndodaxTradeParameters parameters,
            CancellationToken cancellationToken)
    {
        try
        {
            return await RestClient.PlaceOrderAsync(parameters,
                cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            IsUncertainPlacement(error))
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt),
                    cancellationToken);
                try
                {
                    var order = await RestClient.GetOrderByClientIdAsync(
                        parameters.ClientOrderId, cancellationToken);
                    if (order?.Order?.OrderId.IsEmpty() == false)
                        return new()
                        {
                            OrderId = order.Order.OrderId,
                            ClientOrderId = order.Order.ClientOrderId,
                        };
                }
                catch (IndodaxApiException lookupError) when (
                    lookupError.Code is "order_not_found" or "1112")
                {
                }
            }
            throw;
        }
    }

    private async ValueTask SendPortfolioSnapshotAsync(
        long originalTransactionId, CancellationToken cancellationToken)
    {
        var account = await RestClient.GetAccountAsync(cancellationToken);
        var held = (account?.Held ?? []).ToDictionary(
            static value => NormalizeAccountCurrency(value.Name),
            static value => value.Amount, StringComparer.OrdinalIgnoreCase);
        foreach (var asset in account?.Available ?? [])
        {
            var currency = NormalizeAccountCurrency(asset.Name);
            held.TryGetValue(currency, out var blocked);
            await SendBalanceAsync(currency, asset.Amount, blocked,
                originalTransactionId,
                account.ServerTime.FromIndodaxSeconds(CurrentTime),
                cancellationToken);
            held.Remove(currency);
        }
        foreach (var asset in held)
            await SendBalanceAsync(asset.Key, 0m, asset.Value,
                originalTransactionId,
                account?.ServerTime.FromIndodaxSeconds(CurrentTime) ?? CurrentTime,
                cancellationToken);
    }

    private async ValueTask SendOrderSnapshotAsync(OrderStatusMessage message,
        OrderSubscription subscription, int maximum,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var to = (message.To ?? now).ToUniversalTime();
        if (to > now)
            to = now;
        var from = (message.From ?? to - TimeSpan.FromHours(24) +
            TimeSpan.FromSeconds(1)).ToUniversalTime();
        if (from >= to)
            from = to - TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1);

        if (!subscription.OrderIdentifier.IsEmpty())
        {
            var tracked = GetTrackedOrder(subscription.OrderIdentifier);
            IndodaxOrderData response;
            if (tracked is not null &&
                subscription.OrderIdentifier.EqualsIgnoreCase(
                    tracked.ClientOrderId) &&
                !subscription.OrderIdentifier.EqualsIgnoreCase(
                    tracked.ExchangeOrderId))
                response = await RestClient.GetOrderByClientIdAsync(
                    subscription.OrderIdentifier, cancellationToken);
            else
            {
                if (subscription.PairId.IsEmpty())
                    throw new InvalidOperationException(
                        "Indodax exchange-order lookup requires the market.");
                response = await RestClient.GetOrderAsync(
                    GetMarket(subscription.PairId).TapiPair,
                    subscription.OrderIdentifier, cancellationToken);
            }
            if (response?.Order is { } order &&
                MatchesOrder(subscription, order))
                await SendLegacyOrderAsync(order, message.TransactionId,
                    cancellationToken);
            return;
        }

        var requestedMarket = subscription.PairId.IsEmpty()
            ? null
            : GetMarket(subscription.PairId);
        var openOrders = await LoadOpenOrdersAsync(requestedMarket,
            cancellationToken);
        var sent = 0;
        foreach (var order in openOrders.Where(order =>
            MatchesOrder(subscription, order)).Take(maximum))
        {
            await SendLegacyOrderAsync(order, message.TransactionId,
                cancellationToken);
            sent++;
        }

        var markets = GetHistoryMarkets(subscription, openOrders);
        foreach (var market in markets)
        {
            if (sent >= maximum)
                break;
            var orders = await LoadOrderHistoryAsync(market, from, to,
                maximum - sent, cancellationToken);
            foreach (var order in orders.Where(order =>
                MatchesOrder(subscription, order)).Take(maximum - sent))
            {
                await SendV2OrderAsync(order, message.TransactionId,
                    cancellationToken);
                sent++;
            }

            if (sent >= maximum)
                break;
            var trades = await LoadTradeHistoryAsync(market, from, to,
                maximum - sent, cancellationToken);
            foreach (var trade in trades.Where(trade =>
                MatchesTrade(subscription, trade)).Take(maximum - sent))
            {
                await SendAccountTradeAsync(trade, message.TransactionId,
                    cancellationToken, false);
                sent++;
            }
        }
    }

    private async ValueTask<IndodaxLegacyOrder[]> LoadOpenOrdersAsync(
        MarketDefinition market, CancellationToken cancellationToken)
    {
        var response = await RestClient.GetOpenOrdersAsync(market?.TapiPair,
            cancellationToken);
        return [.. (response?.Orders ?? []).Where(order => order is not null &&
            (market is null || order.Pair.NormalizePairId()
                .EqualsIgnoreCase(market.PairId)))];
    }

    private async ValueTask<IndodaxV2Order[]> LoadOrderHistoryAsync(
        MarketDefinition market, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, IndodaxV2Order>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var window in GetHistoryWindows(from, to))
        {
            var response = await RestClient.GetOrderHistoryAsync(new()
            {
                Symbol = market.PairId,
                StartTime = window.Start,
                EndTime = window.End,
                Limit = 1000,
            }, cancellationToken);
            foreach (var order in response ?? [])
                if (order?.OrderId.IsEmpty() == false)
                    values[order.OrderId] = order;
            if (values.Count >= maximum)
                break;
        }
        return [.. values.Values.OrderByDescending(static order =>
            order.FinishTime > 0 ? order.FinishTime : order.SubmitTime)
            .Take(maximum)];
    }

    private async ValueTask<IndodaxV2Trade[]> LoadTradeHistoryAsync(
        MarketDefinition market, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, IndodaxV2Trade>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var window in GetHistoryWindows(from, to))
        {
            var response = await RestClient.GetTradeHistoryAsync(new()
            {
                Symbol = market.PairId,
                StartTime = window.Start,
                EndTime = window.End,
                Limit = 1000,
            }, cancellationToken);
            foreach (var trade in response ?? [])
                if (trade?.TradeId.IsEmpty() == false)
                    values[trade.TradeId] = trade;
            if (values.Count >= maximum)
                break;
        }
        return [.. values.Values.OrderByDescending(static trade => trade.Time)
            .Take(maximum)];
    }

    private static IEnumerable<(long Start, long End)> GetHistoryWindows(
        DateTime from, DateTime to)
    {
        var cursor = to;
        while (cursor > from)
        {
            var start = cursor - TimeSpan.FromHours(24) +
                TimeSpan.FromMilliseconds(1);
            if (start < from)
                start = from;
            yield return (new DateTimeOffset(start).ToUnixTimeMilliseconds(),
                new DateTimeOffset(cursor).ToUnixTimeMilliseconds());
            cursor = start - TimeSpan.FromMilliseconds(1);
        }
    }

    private ValueTask SendBalanceAsync(string currency, decimal available,
        decimal blocked, long originalTransactionId, DateTime serverTime,
        CancellationToken cancellationToken)
    {
        if (currency.IsEmpty())
            return default;
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = GetPortfolioName(),
            SecurityId = new SecurityId
            {
                SecurityCode = currency.NormalizeCurrency(),
                BoardCode = BoardCodes.Indodax,
            },
            ServerTime = serverTime,
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, available, true)
        .TryAdd(PositionChangeTypes.BlockedValue, blocked, true),
            cancellationToken);
    }

    private ValueTask SendTrackedOrderAsync(TrackedOrder tracked,
        OrderStates state, decimal balance, long originalTransactionId,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = tracked.PairId.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = tracked.Side,
            OrderVolume = tracked.Volume,
            Balance = balance,
            OrderPrice = tracked.Price,
            OrderType = tracked.OrderType,
            OrderState = state,
            OrderStringId = tracked.ExchangeOrderId,
            UserOrderId = tracked.ClientOrderId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            TimeInForce = TimeInForce.PutInQueue,
            PostOnly = tracked.IsPostOnly,
            Condition = tracked.QuoteAmount is not null
                ? new IndodaxOrderCondition
                {
                    QuoteAmount = tracked.QuoteAmount,
                }
                : null,
        }, cancellationToken);

    private ValueTask SendLegacyOrderAsync(IndodaxLegacyOrder order,
        long originalTransactionId, CancellationToken cancellationToken,
        OrderStates? state = null, decimal? balance = null)
    {
        if (order?.OrderId.IsEmpty() != false)
            return default;
        var tracked = GetTrackedOrder(order.OrderId) ??
            GetTrackedOrder(order.ClientOrderId);
        var market = !order.Pair.IsEmpty()
            ? GetMarket(order.Pair)
            : tracked is null ? null : GetMarket(tracked.PairId);
        if (market is null)
            return default;
        var volume = GetLegacyBaseAmount(order.OriginalAmount,
            order.AmountCurrency, order.Price, market);
        var remaining = GetLegacyBaseAmount(order.RemainingAmount,
            order.RemainingCurrency, order.Price, market);
        var orderState = state ?? order.Status.ToStockSharp();
        if (orderState != OrderStates.Active)
            remaining = 0m;
        tracked ??= new TrackedOrder
        {
            TransactionId = ParseTransactionId(order.ClientOrderId),
            PairId = market.PairId,
            ExchangeOrderId = order.OrderId,
            ClientOrderId = order.ClientOrderId,
            Side = order.Side.ToStockSharp(),
            OrderType = order.OrderType.ToStockSharp(),
            Volume = volume,
            Price = order.Price,
        };
        tracked.ExchangeOrderId = order.OrderId;
        TrackOrder(tracked, order.OrderId, order.ClientOrderId);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = market.PairId.ToStockSharp(),
            ServerTime = (order.FinishTime > 0
                ? order.FinishTime
                : order.SubmitTime).FromIndodaxSeconds(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderVolume = volume,
            Balance = balance ?? remaining,
            OrderPrice = order.Price,
            OrderType = order.OrderType.ToStockSharp(),
            OrderState = orderState,
            OrderStringId = order.OrderId,
            UserOrderId = order.ClientOrderId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
        }, cancellationToken);
    }

    private ValueTask SendV2OrderAsync(IndodaxV2Order order,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        if (order?.OrderId.IsEmpty() != false || order.Symbol.IsEmpty())
            return default;
        var market = GetMarket(order.Symbol);
        var tracked = GetTrackedOrder(order.OrderId) ??
            GetTrackedOrder(order.ClientOrderId) ?? new TrackedOrder
            {
                TransactionId = ParseTransactionId(order.ClientOrderId),
                PairId = market.PairId,
                ExchangeOrderId = order.OrderId,
                ClientOrderId = order.ClientOrderId,
                Side = order.Side.ToStockSharp(),
                OrderType = order.OrderType.ToStockSharp(),
                Volume = order.OriginalQuantity,
                Price = order.Price,
            };
        tracked.ExchangeOrderId = order.OrderId;
        TrackOrder(tracked, order.OrderId, order.ClientOrderId);
        var state = order.Status.ToStockSharp();
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = market.PairId.ToStockSharp(),
            ServerTime = (order.FinishTime > 0
                ? order.FinishTime
                : order.SubmitTime).FromIndodaxMilliseconds(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderVolume = order.OriginalQuantity,
            Balance = state == OrderStates.Active
                ? (order.OriginalQuantity - order.ExecutedQuantity).Max(0m)
                : 0m,
            OrderPrice = order.Price,
            OrderType = order.OrderType.ToStockSharp(),
            OrderState = state,
            OrderStringId = order.OrderId,
            UserOrderId = order.ClientOrderId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(order.CancelReason.IsEmpty()
                    ? "Indodax rejected the order."
                    : order.CancelReason)
                : null,
        }, cancellationToken);
    }

    private ValueTask SendPrivateOrderAsync(IndodaxPrivateOrder order,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        if (order?.OrderId.IsEmpty() != false || order.Symbol.IsEmpty())
            return default;
        var market = GetMarket(order.Symbol);
        var tracked = GetTrackedOrder(order.OrderId) ??
            GetTrackedOrder(order.ClientOrderId) ?? new TrackedOrder
            {
                TransactionId = ParseTransactionId(order.ClientOrderId),
                PairId = market.PairId,
                ExchangeOrderId = order.OrderId,
                ClientOrderId = order.ClientOrderId,
                Side = order.Side.ToStockSharp(),
                OrderType = OrderTypes.Limit,
                Volume = order.OriginalQuantity,
                Price = order.Price,
            };
        tracked.ExchangeOrderId = order.OrderId;
        TrackOrder(tracked, order.OrderId, order.ClientOrderId);
        var state = order.Status.ToStockSharp();
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = market.PairId.ToStockSharp(),
            ServerTime = order.TransactionTime.FromIndodaxMilliseconds(
                CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderVolume = order.OriginalQuantity,
            Balance = state == OrderStates.Active
                ? order.UnfilledQuantity
                : 0m,
            OrderPrice = order.Price,
            OrderType = tracked.OrderType,
            OrderState = state,
            OrderStringId = order.OrderId,
            UserOrderId = order.ClientOrderId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            PostOnly = tracked.IsPostOnly,
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(order.CancelReason.IsEmpty()
                    ? "Indodax rejected the order."
                    : order.CancelReason)
                : null,
        }, cancellationToken);
    }

    private ValueTask SendAccountTradeAsync(IndodaxV2Trade trade,
        long originalTransactionId, CancellationToken cancellationToken,
        bool addToDeduplication = true)
    {
        if (trade?.TradeId.IsEmpty() != false || trade.OrderId.IsEmpty() ||
            trade.Symbol.IsEmpty() || trade.Price <= 0 || trade.Quantity <= 0)
            return default;
        if (addToDeduplication && !AddAccountTrade(trade.TradeId))
            return default;
        var tracked = GetTrackedOrder(trade.OrderId) ??
            GetTrackedOrder(trade.ClientOrderId);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = GetMarket(trade.Symbol).PairId.ToStockSharp(),
            ServerTime = trade.Time.FromIndodaxMilliseconds(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = trade.IsBuyer ? Sides.Buy : Sides.Sell,
            OrderStringId = trade.OrderId,
            TradeStringId = trade.TradeId,
            TradePrice = trade.Price,
            TradeVolume = trade.Quantity,
            Commission = trade.Commission != 0m ? trade.Commission : null,
            CommissionCurrency = trade.CommissionAsset,
            IsMarketMaker = trade.IsMaker,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = originalTransactionId,
        }, cancellationToken);
    }

    private ValueTask SendPrivateTradeAsync(IndodaxPrivateOrder order,
        long originalTransactionId, CancellationToken cancellationToken,
        bool addToDeduplication = true)
    {
        if (order?.TradeId.IsEmpty() != false || order.Fill is null ||
            order.Fill.Quantity <= 0 || order.Price <= 0 ||
            addToDeduplication && !AddAccountTrade(order.TradeId))
            return default;
        var tracked = GetTrackedOrder(order.OrderId) ??
            GetTrackedOrder(order.ClientOrderId);
        var commission = order.Fill.Fee;
        if (order.Fill.TaxAsset.EqualsIgnoreCase(order.Fill.FeeAsset))
            commission += order.Fill.Tax;
        if (order.Fill.ClearingAsset.EqualsIgnoreCase(order.Fill.FeeAsset))
            commission += order.Fill.Clearing;
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = GetMarket(order.Symbol).PairId.ToStockSharp(),
            ServerTime = order.TransactionTime.FromIndodaxMilliseconds(
                CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderStringId = order.OrderId,
            TradeStringId = order.TradeId,
            TradePrice = order.Price,
            TradeVolume = order.Fill.Quantity,
            Commission = commission != 0m ? commission : null,
            CommissionCurrency = order.Fill.FeeAsset,
            IsMarketMaker = order.Fill.Participant ==
                IndodaxParticipants.Maker,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = originalTransactionId,
        }, cancellationToken);
    }

    private async ValueTask OnPrivateEventsAsync(IndodaxPrivateEvent[] events,
        CancellationToken cancellationToken)
    {
        foreach (var value in events ?? [])
        {
            var order = value?.Order;
            if (order?.OrderId.IsEmpty() != false || order.Symbol.IsEmpty())
                continue;
            var tracked = GetTrackedOrder(order.OrderId) ??
                GetTrackedOrder(order.ClientOrderId);
            var targets = GetOrderTargets(GetMarket(order.Symbol).PairId,
                order.OrderId, order.ClientOrderId, order.Side.ToStockSharp(),
                tracked?.TransactionId ?? 0);
            var hasNewTrade = !order.TradeId.IsEmpty() &&
                AddAccountTrade(order.TradeId);
            foreach (var target in targets)
            {
                await SendPrivateOrderAsync(order, target, cancellationToken);
                if (hasNewTrade)
                    await SendPrivateTradeAsync(order, target,
                        cancellationToken, false);
            }
        }
    }

    private long[] GetOrderTargets(string pairId, string orderId,
        string clientOrderId, Sides side, long transactionId)
    {
        var targets = new HashSet<long>();
        if (transactionId > 0)
            targets.Add(transactionId);
        using (_sync.EnterScope())
            foreach (var pair in _orderSubscriptions)
                if (MatchesOrderSubscription(pair.Value, pairId, orderId,
                    clientOrderId, side))
                    targets.Add(pair.Key);
        return [.. targets];
    }

    private bool MatchesOrder(OrderSubscription subscription,
        IndodaxLegacyOrder order)
    {
        if (order is null)
            return false;
        var pairId = order.Pair.IsEmpty()
            ? GetTrackedOrder(order.OrderId)?.PairId
            : GetMarket(order.Pair).PairId;
        return MatchesOrderSubscription(subscription, pairId, order.OrderId,
            order.ClientOrderId, order.Side.ToStockSharp());
    }

    private bool MatchesOrder(OrderSubscription subscription,
        IndodaxV2Order order)
        => order is not null && MatchesOrderSubscription(subscription,
            GetMarket(order.Symbol).PairId, order.OrderId, order.ClientOrderId,
            order.Side.ToStockSharp());

    private bool MatchesTrade(OrderSubscription subscription,
        IndodaxV2Trade trade)
        => trade is not null && MatchesOrderSubscription(subscription,
            GetMarket(trade.Symbol).PairId, trade.OrderId, trade.ClientOrderId,
            trade.IsBuyer ? Sides.Buy : Sides.Sell);

    private static bool MatchesOrderSubscription(
        OrderSubscription subscription, string pairId, string orderId,
        string clientOrderId, Sides side)
        => (subscription.PairId.IsEmpty() ||
            subscription.PairId.EqualsIgnoreCase(pairId)) &&
            (subscription.OrderIdentifier.IsEmpty() ||
            subscription.OrderIdentifier.EqualsIgnoreCase(orderId) ||
            subscription.OrderIdentifier.EqualsIgnoreCase(clientOrderId)) &&
            (subscription.Side is null || subscription.Side == side);

    private MarketDefinition[] GetHistoryMarkets(OrderSubscription subscription,
        IndodaxLegacyOrder[] openOrders)
    {
        if (!subscription.PairId.IsEmpty())
            return [GetMarket(subscription.PairId)];
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in openOrders ?? [])
            if (order?.Pair.IsEmpty() == false)
                ids.Add(GetMarket(order.Pair).PairId);
        using (_sync.EnterScope())
            foreach (var tracked in _trackedOrders.Values)
                if (!tracked.PairId.IsEmpty())
                    ids.Add(tracked.PairId);
        return [.. ids.Select(GetMarket)];
    }

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown Indodax portfolio '{portfolioName}'.");
    }

    private static bool IsUncertainPlacement(Exception error)
        => error is HttpRequestException or TaskCanceledException ||
            error is IndodaxApiException api && (int)api.StatusCode >= 500;

    private static long ParseTransactionId(string clientOrderId)
    {
        if (clientOrderId?.StartsWith("ss-",
            StringComparison.OrdinalIgnoreCase) != true)
            return 0;
        var end = clientOrderId.IndexOf('-', 3);
        var value = end < 0 ? clientOrderId.AsSpan(3) :
            clientOrderId.AsSpan(3, end - 3);
        return long.TryParse(value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var transactionId)
                ? transactionId
                : 0;
    }

    private static decimal GetLegacyBaseAmount(decimal amount,
        string currency, decimal price, MarketDefinition market)
    {
        currency = NormalizeAccountCurrency(currency);
        if (currency.EqualsIgnoreCase(
            NormalizeAccountCurrency(market.BaseCurrency)))
            return amount;
        if (currency.EqualsIgnoreCase(
            NormalizeAccountCurrency(market.QuoteCurrency)) && price > 0)
            return amount / price;
        return 0m;
    }

    private static string NormalizeAccountCurrency(string value)
        => value.EqualsIgnoreCase("rp") ? "idr" : value?.Trim().ToLowerInvariant();

    private async ValueTask RefreshOrderSnapshotsAsync(
        CancellationToken cancellationToken)
    {
        KeyValuePair<long, OrderSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _orderSubscriptions];
        foreach (var pair in subscriptions)
        {
            var market = pair.Value.PairId.IsEmpty()
                ? null
                : GetMarket(pair.Value.PairId);
            var orders = await LoadOpenOrdersAsync(market, cancellationToken);
            foreach (var order in orders.Where(order =>
                MatchesOrder(pair.Value, order)))
                await SendLegacyOrderAsync(order, pair.Key, cancellationToken);
        }
    }

    private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
