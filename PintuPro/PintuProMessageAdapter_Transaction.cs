namespace StockSharp.PintuPro;

public partial class PintuProMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var market = GetMarket(regMsg.SecurityId);
        var orderType = (regMsg.OrderType ?? OrderTypes.Limit).ToPintuPro();
        var volume = regMsg.Volume.Abs();
        var condition = regMsg.Condition as PintuProOrderCondition ?? new();
        var quoteAmount = condition.QuoteAmount;

        if (orderType == PintuProOrderTypes.Limit)
        {
            if (regMsg.Price <= 0 || volume <= 0)
                throw new InvalidOperationException(
                    "A Pintu Pro limit order requires positive price and volume.");
            if (quoteAmount is not null)
                throw new InvalidOperationException(
                    "Quote amount is supported for market buy orders only.");
        }
        else if (regMsg.Side == Sides.Buy)
        {
            if (quoteAmount is null or <= 0)
                throw new InvalidOperationException(
                    "A Pintu Pro market buy order requires QuoteAmount.");
        }
        else if (volume <= 0)
            throw new InvalidOperationException(
                "A Pintu Pro market sell order requires positive volume.");

        if (regMsg.PostOnly == true && orderType != PintuProOrderTypes.Limit)
            throw new InvalidOperationException(
                "Pintu Pro post-only is supported for limit orders only.");
        if (regMsg.PostOnly == true && regMsg.TimeInForce is not null and
            not TimeInForce.PutInQueue)
            throw new InvalidOperationException(
                "A Pintu Pro post-only order must use GTC.");
        if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
            throw new NotSupportedException(
                "Pintu Pro does not document iceberg orders.");
        if (regMsg.TillDate is not null)
            throw new NotSupportedException(
                "Pintu Pro does not support GTD orders.");

        market.Reference.ValidateOrderValue(
            orderType == PintuProOrderTypes.Limit ? regMsg.Price : 0m,
            regMsg.Side == Sides.Buy && orderType == PintuProOrderTypes.Market
                ? 0m
                : volume,
            quoteAmount);

        var clientOrderId = GetClientOrderId(regMsg.TransactionId,
            regMsg.UserOrderId);
        var timeInForce = regMsg.TimeInForce.ToPintuPro(orderType);
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            Symbol = market.Symbol,
            ClientOrderId = clientOrderId,
            Side = regMsg.Side,
            OrderType = orderType.ToStockSharp(),
            Volume = volume,
            Price = regMsg.Price,
            TimeInForce = timeInForce.ToStockSharp(),
            IsPostOnly = regMsg.PostOnly == true,
            QuoteAmount = quoteAmount,
        };
        TrackOrder(tracked, clientOrderId,
            regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));

        var parameters = new PintuProPlaceOrderParams
        {
            Symbol = market.Symbol,
            Side = regMsg.Side.ToPintuPro(),
            OrderType = orderType,
            Price = orderType == PintuProOrderTypes.Limit
                ? regMsg.Price.ToWire()
                : null,
            Size = orderType == PintuProOrderTypes.Market &&
                regMsg.Side == Sides.Buy
                    ? null
                    : volume.ToWire(),
            Notional = quoteAmount?.ToWire(),
            ClientOrderId = clientOrderId,
            TimeInForce = orderType == PintuProOrderTypes.Limit
                ? timeInForce
                : null,
            ExecutionInstruction = regMsg.PostOnly == true
                ? PintuProExecutionInstructions.PostOnly
                : null,
        };
        var result = await PlaceWithReconciliationAsync(parameters,
            cancellationToken);
        if (result?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                "Pintu Pro accepted an order without returning its order ID.");
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
        var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? tracked?.Symbol
            : GetMarket(cancelMsg.SecurityId).Symbol;
        if (symbol.IsEmpty())
            throw new InvalidOperationException(
                "Pintu Pro cancellation requires the order symbol.");
        await RestClient.CancelOrderAsync(new()
        {
            Symbol = symbol,
            OrderId = identifier,
        }, cancellationToken);
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = symbol.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            OrderStringId = identifier,
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
                "Pintu Pro spot cancellation cannot close positions.");
        if (cancelMsg.IsStop == true)
            return;

        var markets = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? GetSelectedMarkets(default)
            : [GetMarket(cancelMsg.SecurityId)];
        foreach (var market in markets)
        {
            if (cancelMsg.Side is null)
            {
                await RestClient.CancelAllOrdersAsync(new()
                {
                    Symbol = market.Symbol,
                }, cancellationToken);
                continue;
            }

            var openOrders = await LoadOpenOrdersAsync(market.Symbol,
                cancelMsg.Side, 5000, cancellationToken);
            foreach (var order in openOrders)
            {
                await RestClient.CancelOrderAsync(new()
                {
                    Symbol = market.Symbol,
                    OrderId = order.OrderId,
                }, cancellationToken);
                await SendOrderAsync(order, cancelMsg.TransactionId,
                    cancellationToken, OrderStates.Done, 0m);
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
                _portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = GetPortfolioName(),
            BoardCode = BoardCodes.PintuPro,
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
        var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(statusMsg.SecurityId).Symbol;
        string identifier = null;
        if (statusMsg.HasOrderId())
            identifier = ResolveOrderIdentifier(statusMsg.OrderId,
                statusMsg.OrderStringId, "lookup");
        symbol ??= GetTrackedOrder(identifier)?.Symbol;
        var subscription = new OrderSubscription
        {
            Symbol = symbol,
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

    private async ValueTask<PintuProPlaceOrderData>
        PlaceWithReconciliationAsync(PintuProPlaceOrderParams parameters,
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
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt),
                    cancellationToken);
                try
                {
                    var details = await RestClient.GetOrderDetailsAsync(new()
                    {
                        Symbol = parameters.Symbol,
                        ClientOrderId = parameters.ClientOrderId,
                        StartTime = now - (long)TimeSpan.FromHours(24)
                            .TotalMilliseconds + 1000,
                        EndTime = now,
                    }, cancellationToken);
                    if (details?.Order?.OrderId.IsEmpty() == false)
                        return new()
                        {
                            OrderId = details.Order.OrderId,
                            ClientOrderId = details.Order.ClientOrderId,
                        };
                }
                catch (PintuProApiException lookupError) when (
                    lookupError.Code == "7")
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
        foreach (var asset in account?.Assets ?? [])
            await SendBalanceAsync(asset, originalTransactionId,
                account.ServerTimestamp.FromPintuProTimestamp(CurrentTime),
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

        if (!subscription.OrderIdentifier.IsEmpty() &&
            !subscription.Symbol.IsEmpty())
        {
            var tracked = GetTrackedOrder(subscription.OrderIdentifier);
            var exchangeOrderId = tracked?.ExchangeOrderId;
            var clientOrderId = exchangeOrderId.IsEmpty()
                ? tracked?.ClientOrderId
                : null;
            if (exchangeOrderId.IsEmpty() && clientOrderId.IsEmpty())
                exchangeOrderId = subscription.OrderIdentifier;
            PintuProOrderDetailsData details = null;
            foreach (var (start, end) in GetHistoryWindows(from, to))
            {
                try
                {
                    details = await RestClient.GetOrderDetailsAsync(new()
                    {
                        Symbol = subscription.Symbol,
                        OrderId = exchangeOrderId,
                        ClientOrderId = clientOrderId,
                        StartTime = start,
                        EndTime = end,
                    }, cancellationToken);
                    if (details?.Order is not null)
                        break;
                }
                catch (PintuProApiException error) when (error.Code == "7")
                {
                }
            }
            if (details?.Order is null)
                return;
            if (details?.Order is not null && MatchesOrder(subscription,
                details.Order))
                await SendOrderAsync(details.Order, message.TransactionId,
                    cancellationToken);
            foreach (var trade in details?.Trades ?? [])
            {
                if (trade?.OrderId.IsEmpty() != false)
                    trade.OrderId = details.Order.OrderId;
                if (MatchesTrade(subscription, trade))
                    await SendAccountTradeAsync(trade, message.TransactionId,
                        cancellationToken, false);
            }
            return;
        }

        var orders = await LoadOrdersAsync(subscription.Symbol,
            subscription.Side, from, to, maximum, cancellationToken);
        foreach (var order in orders.Where(order =>
            MatchesOrder(subscription, order)))
            await SendOrderAsync(order, message.TransactionId,
                cancellationToken);

        var remaining = (maximum - orders.Length).Max(0);
        if (remaining <= 0)
            return;
        var trades = await LoadTradesAsync(subscription.Symbol,
            subscription.Side, from, to, remaining, cancellationToken);
        foreach (var trade in trades.Where(trade =>
            MatchesTrade(subscription, trade)))
            await SendAccountTradeAsync(trade, message.TransactionId,
                cancellationToken, false);
    }

    private async ValueTask<PintuProOrder[]> LoadOrdersAsync(string symbol,
        Sides? side, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, PintuProOrder>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var order in await LoadOpenOrdersAsync(symbol, side, maximum,
            cancellationToken))
            if (!order.OrderId.IsEmpty())
                values[order.OrderId] = order;

        foreach (var (start, end) in GetHistoryWindows(from, to))
        {
            for (var page = 0; values.Count < maximum; page++)
            {
                var response = await RestClient.GetOrderHistoryAsync(new()
                {
                    Symbol = symbol,
                    Side = side?.ToPintuPro(),
                    Page = page,
                    PageSize = 200,
                    StartTime = start,
                    EndTime = end,
                }, cancellationToken);
                var pageValues = response?.Orders ?? [];
                foreach (var order in pageValues)
                    if (order?.OrderId.IsEmpty() == false)
                        values[order.OrderId] = order;
                if (pageValues.Length < 200)
                    break;
            }
            if (values.Count >= maximum)
                break;
        }
        return [.. values.Values.OrderByDescending(static order =>
            order.UpdatedAt).Take(maximum)];
    }

    private async ValueTask<PintuProOrder[]> LoadOpenOrdersAsync(string symbol,
        Sides? side, int maximum, CancellationToken cancellationToken)
    {
        var values = new List<PintuProOrder>();
        for (var page = 0; values.Count < maximum; page++)
        {
            var response = await RestClient.GetOpenOrdersAsync(new()
            {
                Symbol = symbol,
                Side = side?.ToPintuPro(),
                Page = page,
                PageSize = 200,
            }, cancellationToken);
            var pageValues = response?.Orders ?? [];
            values.AddRange(pageValues.Where(static order => order is not null));
            if (pageValues.Length < 200 || response is null ||
                response.Count > 0 && values.Count >= response.Count)
                break;
        }
        return [.. values.Take(maximum)];
    }

    private async ValueTask<PintuProAccountTrade[]> LoadTradesAsync(
        string symbol, Sides? side, DateTime from, DateTime to, int maximum,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, PintuProAccountTrade>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (start, end) in GetHistoryWindows(from, to))
        {
            for (var page = 0; values.Count < maximum; page++)
            {
                var response = await RestClient.GetTradeHistoryAsync(new()
                {
                    Symbol = symbol,
                    Side = side?.ToPintuPro(),
                    Page = page,
                    PageSize = 200,
                    StartTime = start,
                    EndTime = end,
                }, cancellationToken);
                var pageValues = response?.Trades ?? [];
                foreach (var trade in pageValues)
                    if (trade?.TradeId.IsEmpty() == false)
                        values[trade.TradeId] = trade;
                if (pageValues.Length < 200)
                    break;
            }
            if (values.Count >= maximum)
                break;
        }
        return [.. values.Values.OrderByDescending(static trade =>
            trade.TradedAt).Take(maximum)];
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

    private ValueTask SendBalanceAsync(PintuProAsset asset,
        long originalTransactionId, DateTime serverTime,
        CancellationToken cancellationToken)
    {
        if (asset?.Currency.IsEmpty() != false)
            return default;
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = GetPortfolioName(),
            SecurityId = new SecurityId
            {
                SecurityCode = asset.Currency.NormalizeCurrency(),
                BoardCode = BoardCodes.PintuPro,
            },
            ServerTime = serverTime,
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, asset.Available, true)
        .TryAdd(PositionChangeTypes.BlockedValue, asset.InOrders, true),
            cancellationToken);
    }

    private ValueTask SendTrackedOrderAsync(TrackedOrder tracked,
        OrderStates state, decimal balance, long originalTransactionId,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = tracked.Symbol.ToStockSharp(),
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
            TimeInForce = tracked.TimeInForce,
            PostOnly = tracked.IsPostOnly,
            Condition = tracked.QuoteAmount is not null
                ? new PintuProOrderCondition
                {
                    QuoteAmount = tracked.QuoteAmount,
                }
                : null,
        }, cancellationToken);

    private ValueTask SendOrderAsync(PintuProOrder order,
        long originalTransactionId, CancellationToken cancellationToken,
        OrderStates? state = null, decimal? balance = null)
    {
        if (order?.OrderId.IsEmpty() != false || order.Symbol.IsEmpty())
            return default;
        var symbol = GetMarket(order.Symbol).Symbol;
        var tracked = GetTrackedOrder(order.OrderId) ??
            GetTrackedOrder(order.ClientOrderId) ?? new TrackedOrder
            {
                TransactionId = ParseTransactionId(order.ClientOrderId),
                Symbol = symbol,
                ExchangeOrderId = order.OrderId,
                ClientOrderId = order.ClientOrderId,
                Side = order.Side.ToStockSharp(),
                OrderType = order.OrderType.ToStockSharp(),
                Volume = order.Size,
                Price = order.Price ?? 0m,
                TimeInForce = order.TimeInForce.ToStockSharp(),
                IsPostOnly = order.ExecutionInstruction ==
                    PintuProExecutionInstructions.PostOnly,
            };
        tracked.ExchangeOrderId = order.OrderId;
        TrackOrder(tracked, order.OrderId, order.ClientOrderId);
        var orderState = state ?? order.Status.ToStockSharp();
        var remaining = balance ?? (orderState == OrderStates.Active
            ? (order.Size - order.FilledSize).Max(0m)
            : 0m);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = symbol.ToStockSharp(),
            ServerTime = (order.UpdatedAt > 0 ? order.UpdatedAt : order.CreatedAt)
                .FromPintuProTimestamp(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderVolume = order.Size,
            Balance = remaining,
            OrderPrice = order.Price ?? 0m,
            AveragePrice = order.AveragePrice is > 0
                ? order.AveragePrice
                : order.FilledSize > 0 && order.FilledValue > 0
                    ? order.FilledValue / order.FilledSize
                    : null,
            OrderType = order.OrderType.ToStockSharp(),
            OrderState = orderState,
            OrderStringId = order.OrderId,
            UserOrderId = order.ClientOrderId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            TimeInForce = order.TimeInForce.ToStockSharp(),
            PostOnly = order.ExecutionInstruction ==
                PintuProExecutionInstructions.PostOnly,
            Error = orderState == OrderStates.Failed
                ? new InvalidOperationException(order.Reason.IsEmpty()
                    ? "Pintu Pro rejected the order."
                    : order.Reason)
                : null,
        }, cancellationToken);
    }

    private ValueTask SendAccountTradeAsync(PintuProAccountTrade trade,
        long originalTransactionId, CancellationToken cancellationToken,
        bool addToDeduplication = true)
    {
        if (trade?.TradeId.IsEmpty() != false || trade.OrderId.IsEmpty() ||
            trade.Symbol.IsEmpty() || trade.Price <= 0 || trade.Size <= 0)
            return default;
        if (addToDeduplication && !AddAccountTrade(trade.TradeId))
            return default;
        var tracked = GetTrackedOrder(trade.OrderId) ??
            GetTrackedOrder(trade.ClientOrderId);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = GetMarket(trade.Symbol).Symbol.ToStockSharp(),
            ServerTime = trade.TradedAt.FromPintuProTimestamp(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = trade.Side.ToStockSharp(),
            OrderStringId = trade.OrderId,
            TradeStringId = trade.TradeId,
            TradePrice = trade.Price,
            TradeVolume = trade.Size,
            Commission = trade.Fee is not null and not 0 ? trade.Fee : null,
            CommissionCurrency = trade.FeeAsset,
            IsMarketMaker = trade.FeeType is PintuProFeeTypes.Maker,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = originalTransactionId,
        }, cancellationToken);
    }

    private async ValueTask OnSocketOrderAsync(
        PintuProOrderStreamMessage message,
        CancellationToken cancellationToken)
    {
        foreach (var order in message?.Data?.Orders ?? [])
        {
            if (order?.OrderId.IsEmpty() != false || order.Symbol.IsEmpty())
                continue;
            var tracked = GetTrackedOrder(order.OrderId) ??
                GetTrackedOrder(order.ClientOrderId);
            var targets = GetOrderTargets(order.Symbol.NormalizeSymbol(),
                order.OrderId, order.ClientOrderId, order.Side.ToStockSharp(),
                tracked?.TransactionId ?? 0);
            foreach (var target in targets)
                await SendOrderAsync(order, target, cancellationToken);
        }
    }

    private async ValueTask OnSocketAccountTradeAsync(
        PintuProAccountTradeStreamMessage message,
        CancellationToken cancellationToken)
    {
        foreach (var trade in message?.Data?.Trades ?? [])
        {
            if (trade?.TradeId.IsEmpty() != false ||
                !AddAccountTrade(trade.TradeId))
                continue;
            var tracked = GetTrackedOrder(trade.OrderId) ??
                GetTrackedOrder(trade.ClientOrderId);
            var targets = GetOrderTargets(trade.Symbol.NormalizeSymbol(),
                trade.OrderId, trade.ClientOrderId, trade.Side.ToStockSharp(),
                tracked?.TransactionId ?? 0);
            foreach (var target in targets)
                await SendAccountTradeAsync(trade, target, cancellationToken,
                    false);
        }
    }

    private async ValueTask OnSocketBalanceAsync(
        PintuProBalanceStreamMessage message,
        CancellationToken cancellationToken)
    {
        long[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _portfolioSubscriptions];
        var serverTime = message?.Timestamp.FromPintuProTimestamp(CurrentTime) ??
            CurrentTime;
        foreach (var subscriptionId in subscriptions)
            foreach (var asset in message?.Data?.Assets ?? [])
                await SendBalanceAsync(asset, subscriptionId, serverTime,
                    cancellationToken);
    }

    private long[] GetOrderTargets(string symbol, string orderId,
        string clientOrderId, Sides side, long transactionId)
    {
        var targets = new HashSet<long>();
        if (transactionId > 0)
            targets.Add(transactionId);
        using (_sync.EnterScope())
            foreach (var pair in _orderSubscriptions)
                if (MatchesOrderSubscription(pair.Value, symbol, orderId,
                    clientOrderId, side))
                    targets.Add(pair.Key);
        return [.. targets];
    }

    private static bool MatchesOrder(OrderSubscription subscription,
        PintuProOrder order)
        => order is not null && MatchesOrderSubscription(subscription,
            order.Symbol, order.OrderId, order.ClientOrderId,
            order.Side.ToStockSharp());

    private static bool MatchesTrade(OrderSubscription subscription,
        PintuProAccountTrade trade)
        => trade is not null && MatchesOrderSubscription(subscription,
            trade.Symbol, trade.OrderId, trade.ClientOrderId,
            trade.Side.ToStockSharp());

    private static bool MatchesOrderSubscription(
        OrderSubscription subscription, string symbol, string orderId,
        string clientOrderId, Sides side)
        => (subscription.Symbol.IsEmpty() ||
            subscription.Symbol.EqualsIgnoreCase(symbol)) &&
            (subscription.OrderIdentifier.IsEmpty() ||
            subscription.OrderIdentifier.EqualsIgnoreCase(orderId) ||
            subscription.OrderIdentifier.EqualsIgnoreCase(clientOrderId)) &&
            (subscription.Side is null || subscription.Side == side);

    private MarketDefinition[] GetSelectedMarkets(SecurityId securityId)
    {
        if (!securityId.SecurityCode.IsEmpty())
            return [GetMarket(securityId)];
        using (_sync.EnterScope())
            return [.. _markets.Values.OrderBy(static market => market.Symbol,
                StringComparer.OrdinalIgnoreCase)];
    }

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown Pintu Pro portfolio '{portfolioName}'.");
    }

    private static bool IsUncertainPlacement(Exception error)
        => error is HttpRequestException or TaskCanceledException ||
            error is PintuProApiException api && (int)api.StatusCode >= 500;

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

    private async ValueTask RefreshOrderSnapshotsAsync(
        CancellationToken cancellationToken)
    {
        KeyValuePair<long, OrderSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _orderSubscriptions];
        foreach (var pair in subscriptions)
        {
            var orders = await LoadOpenOrdersAsync(pair.Value.Symbol,
                pair.Value.Side, 1000, cancellationToken);
            foreach (var order in orders.Where(order =>
                MatchesOrder(pair.Value, order)))
                await SendOrderAsync(order, pair.Key, cancellationToken);
        }
    }

    private async ValueTask CompleteOrderStatusAsync(
        OrderStatusMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
