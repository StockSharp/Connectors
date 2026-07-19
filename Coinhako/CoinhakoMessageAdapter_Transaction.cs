namespace StockSharp.Coinhako;

public partial class CoinhakoMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var market = GetMarket(regMsg.SecurityId);
        var orderType = regMsg.OrderType ?? OrderTypes.Limit;
        if (orderType is not OrderTypes.Limit and not OrderTypes.Market)
            throw new NotSupportedException(
                "Coinhako supports limit and RFQ market orders only.");
        var volume = regMsg.Volume.Abs();
        if (volume <= 0)
            throw new InvalidOperationException(
                "Coinhako order volume must be positive.");
        if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
            throw new InvalidOperationException(
                "A Coinhako limit order requires a positive price.");
        if (regMsg.PostOnly == true)
            throw new NotSupportedException(
                "Coinhako Public API does not document maker-only orders.");
        if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
            throw new NotSupportedException(
                "Coinhako Public API does not document iceberg orders.");
        if (regMsg.TimeInForce is not null and not TimeInForce.PutInQueue)
            throw new NotSupportedException(
                "Coinhako Public API documents GTC and GTD limit orders only.");
        if (orderType == OrderTypes.Market && regMsg.TillDate is not null)
            throw new NotSupportedException(
                "Coinhako RFQ orders use the quote expiry and cannot specify GTD.");

        DateTime? expiryDate = null;
        long? expiresAt = null;
        if (regMsg.TillDate is { } tillDate)
        {
            expiryDate = tillDate.ToUniversalTime();
            if (expiryDate <= DateTime.UtcNow)
                throw new InvalidOperationException(
                    "Coinhako GTD expiry must be in the future.");
            expiresAt = new DateTimeOffset(expiryDate.Value)
                .ToUnixTimeMilliseconds();
        }

        var clientOrderId = GetClientOrderId(regMsg.TransactionId,
            regMsg.UserOrderId);
        CoinhakoOrderQuote quote = null;
        if (orderType == OrderTypes.Market)
        {
            quote = await RestClient.CreateQuoteAsync(new()
            {
                Symbol = market.Symbol,
                Side = regMsg.Side.ToCoinhako(),
                Quantity = ToWire(volume),
                Currency = market.BaseCurrency,
                PaymentMethod = "user_balance",
            }, cancellationToken);
            if (quote?.QuoteId.IsEmpty() != false || quote.LockedPrice <= 0)
                throw new InvalidDataException(
                    "Coinhako returned an incomplete order quote.");
            if (quote.ExpiresAt > 0 && quote.ExpiresAt <=
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                throw new InvalidDataException(
                    "Coinhako returned an already expired order quote.");
        }

        var request = new CoinhakoOrderRequest
        {
            Symbol = market.Symbol,
            Side = regMsg.Side.ToCoinhako(),
            Quantity = ToWire(volume),
            Currency = market.BaseCurrency,
            ClientOrderId = clientOrderId,
            OrderQuoteId = quote?.QuoteId,
            ExecutionType = orderType == OrderTypes.Market
                ? CoinhakoExecutionTypes.Rfq
                : CoinhakoExecutionTypes.Limit,
            Price = orderType == OrderTypes.Limit
                ? ToWire(regMsg.Price)
                : null,
            ExpiresAt = expiresAt,
        };
        var order = await PlaceWithReconciliationAsync(request, market,
            cancellationToken);
        if (order?.Id <= 0)
            throw new InvalidDataException(
                "Coinhako accepted an order without returning its order ID.");
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            OrderId = order.Id,
            ClientOrderId = clientOrderId,
            Symbol = market.Symbol,
            Side = regMsg.Side,
            OrderType = orderType,
            Volume = volume,
            Price = orderType == OrderTypes.Market
                ? quote.LockedPrice
                : regMsg.Price,
            ExpiryDate = expiryDate,
        };
        TrackOrder(tracked);
        await SendOrderAsync(order, regMsg.TransactionId, true,
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
        var order = await CancelWithReconciliationAsync(orderId,
            cancellationToken);
        await SendOrderAsync(order, cancelMsg.TransactionId, true,
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
                "Coinhako spot cancellation cannot close positions.");
        if (cancelMsg.IsStop == true)
            throw new NotSupportedException(
                "Coinhako Public API does not expose stop orders.");

        var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(cancelMsg.SecurityId).Symbol;
        var subscription = new OrderSubscription
        {
            Symbol = symbol,
            Side = cancelMsg.Side,
            Maximum = 5000,
        };
        var orders = await LoadOrdersAsync(subscription, false,
            cancellationToken);
        foreach (var order in orders.Where(static order =>
            order.Status.ToStockSharp() == OrderStates.Active))
        {
            var cancelled = await CancelWithReconciliationAsync(order.Id,
                cancellationToken);
            await SendOrderAsync(cancelled, cancelMsg.TransactionId, true,
                cancellationToken);
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
                _portfolioSubscriptions.Remove(
                    lookupMsg.OriginalTransactionId);
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = GetPortfolioName(),
            BoardCode = BoardCodes.Coinhako,
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
            RemoveOrderSubscription(statusMsg.OriginalTransactionId);
            return;
        }
        if (statusMsg.Count is <= 0)
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        ValidatePortfolio(statusMsg.PortfolioName);

        long? orderId = null;
        string clientOrderId = null;
        if (statusMsg.OrderId is > 0)
            orderId = statusMsg.OrderId;
        else if (!statusMsg.OrderStringId.IsEmpty())
        {
            if (long.TryParse(statusMsg.OrderStringId, NumberStyles.None,
                CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                orderId = parsed;
            else
                clientOrderId = statusMsg.OrderStringId.Trim();
        }
        var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(statusMsg.SecurityId).Symbol;
        var subscription = new OrderSubscription
        {
            Symbol = symbol,
            OrderId = orderId,
            ClientOrderId = clientOrderId,
            Side = statusMsg.Side,
            From = statusMsg.From?.ToUniversalTime(),
            To = statusMsg.To?.ToUniversalTime(),
            Maximum = (statusMsg.Count ?? 1000).Min(5000).Max(1).To<int>(),
        };
        var orders = await LoadOrdersAsync(subscription, false,
            cancellationToken);
        foreach (var order in orders)
            await SendOrderAsync(order, statusMsg.TransactionId, true,
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

    private async ValueTask<CoinhakoOrder> PlaceWithReconciliationAsync(
        CoinhakoOrderRequest request, MarketDefinition market,
        CancellationToken cancellationToken)
    {
        Exception placementError = null;
        try
        {
            var order = await RestClient.CreateOrderAsync(request,
                cancellationToken);
            if (order?.Id > 0)
                return order;
            placementError = new InvalidDataException(
                "Coinhako order response contains no order ID.");
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            IsUncertainOperation(error))
        {
            placementError = error;
        }
        if (placementError is null)
            throw new InvalidOperationException(
                "Coinhako order placement failed.");

        var started = DateTime.UtcNow - TimeSpan.FromMinutes(2);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt),
                cancellationToken);
            var orders = await RestClient.GetOrdersAsync(new()
            {
                PageNumber = 1,
                PageSize = 100,
                BaseCurrency = market.BaseCurrency,
                CounterCurrency = market.CounterCurrency,
                FromTime = ToMilliseconds(started),
            }, cancellationToken);
            var match = (orders ?? []).FirstOrDefault(order =>
                order?.ClientOrderId.EqualsIgnoreCase(
                    request.ClientOrderId) == true);
            if (match is not null)
                return match;
        }
        throw placementError;
    }

    private async ValueTask<CoinhakoOrder> CancelWithReconciliationAsync(
        long orderId, CancellationToken cancellationToken)
    {
        Exception cancellationError;
        try
        {
            var order = await RestClient.CancelOrderAsync(orderId,
                cancellationToken);
            if (order?.Id > 0)
                return order;
            cancellationError = new InvalidDataException(
                "Coinhako cancellation response contains no order ID.");
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            IsUncertainOperation(error))
        {
            cancellationError = error;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt),
                cancellationToken);
            try
            {
                var order = await RestClient.GetOrderAsync(orderId,
                    cancellationToken);
                if (order is not null && order.Status is
                    CoinhakoOrderStatuses.Cancelling or
                    CoinhakoOrderStatuses.Cancelled or
                    CoinhakoOrderStatuses.Completed)
                    return order;
            }
            catch (CoinhakoApiException error) when (
                error.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }
        throw cancellationError;
    }

    private async ValueTask<CoinhakoOrder[]> LoadOrdersAsync(
        OrderSubscription subscription, bool isRefresh,
        CancellationToken cancellationToken)
    {
        if (subscription.OrderId is { } orderId)
        {
            var order = await RestClient.GetOrderAsync(orderId,
                cancellationToken);
            return order is not null && MatchesOrder(subscription, order)
                ? [order]
                : [];
        }
        if (!subscription.ClientOrderId.IsEmpty())
        {
            var tracked = GetTrackedOrder(0, subscription.ClientOrderId);
            if (tracked?.OrderId > 0)
            {
                var order = await RestClient.GetOrderAsync(tracked.OrderId,
                    cancellationToken);
                return order is not null && MatchesOrder(subscription, order)
                    ? [order]
                    : [];
            }
        }

        MarketDefinition market = null;
        if (!subscription.Symbol.IsEmpty())
            market = GetMarket(subscription.Symbol);
        var maximum = isRefresh
            ? subscription.Maximum.Min(200)
            : subscription.Maximum;
        var from = subscription.From;
        if (isRefresh)
        {
            var recent = DateTime.UtcNow - TimeSpan.FromDays(1);
            if (from is null || from < recent)
                from = recent;
        }

        var values = new List<CoinhakoOrder>();
        for (var page = 1; values.Count < maximum; page++)
        {
            var pageSize = (maximum - values.Count).Min(100);
            var response = await RestClient.GetOrdersAsync(new()
            {
                PageNumber = page,
                PageSize = pageSize,
                FromTime = from is null ? null : ToMilliseconds(from.Value),
                ToTime = subscription.To is null
                    ? null
                    : ToMilliseconds(subscription.To.Value),
                BaseCurrency = market?.BaseCurrency,
                CounterCurrency = market?.CounterCurrency,
                Side = subscription.Side?.ToCoinhako(),
            }, cancellationToken) ?? [];
            values.AddRange(response.Where(order =>
                MatchesOrder(subscription, order)));
            if (response.Length < pageSize)
                break;
        }
        return [.. values.OrderBy(static order => order.CreatedAt)
            .Take(maximum)];
    }

    private static bool MatchesOrder(OrderSubscription subscription,
        CoinhakoOrder order)
        => order is not null && order.Id > 0 &&
            (subscription.Symbol.IsEmpty() ||
                order.Symbol.ToCoinhakoSymbolKey().EqualsIgnoreCase(
                    subscription.Symbol.ToCoinhakoSymbolKey())) &&
            (subscription.ClientOrderId.IsEmpty() ||
                order.ClientOrderId.EqualsIgnoreCase(
                    subscription.ClientOrderId)) &&
            (subscription.Side is null ||
                order.Side.ToStockSharp() == subscription.Side);

    private async ValueTask SendPortfolioSnapshotAsync(
        long originalTransactionId, CancellationToken cancellationToken)
    {
        var balances = await RestClient.GetBalancesAsync(null,
            cancellationToken);
        foreach (var balance in balances ?? [])
            await SendBalanceAsync(balance, originalTransactionId,
                cancellationToken);
    }

    private ValueTask SendBalanceAsync(CoinhakoBalance balance,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        if (balance?.Currency.IsEmpty() != false)
            return default;
        var detailedLocked = (balance.LockedBalance?.Orders ?? 0m) +
            (balance.LockedBalance?.AlternativeProducts ?? 0m);
        var blocked = detailedLocked != 0m ? detailedLocked : balance.Locked;
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = GetPortfolioName(),
            SecurityId = new SecurityId
            {
                SecurityCode = balance.Currency.ToUpperInvariant(),
                BoardCode = BoardCodes.Coinhako,
            },
            ServerTime = CurrentTime,
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, balance.Available, true)
        .TryAdd(PositionChangeTypes.BlockedValue, blocked, true),
            cancellationToken);
    }

    private async ValueTask SendOrderAsync(CoinhakoOrder order,
        long originalTransactionId, bool isForced,
        CancellationToken cancellationToken)
    {
        if (order?.Id <= 0 || order.Symbol.IsEmpty())
            return;
        var tracked = GetTrackedOrder(order.Id, order.ClientOrderId);
        var volume = GetBaseVolume(order, tracked);
        tracked ??= new TrackedOrder
        {
            TransactionId = ParseTransactionId(order.ClientOrderId),
            OrderId = order.Id,
            ClientOrderId = order.ClientOrderId,
            Symbol = order.Symbol.NormalizeCoinhakoSymbol(),
            Side = order.Side.ToStockSharp(),
            OrderType = order.ExecutionType == CoinhakoExecutionTypes.Rfq
                ? OrderTypes.Market
                : OrderTypes.Limit,
            Volume = volume,
            Price = order.Price,
            ExpiryDate = order.ExpiresAt is > 0
                ? order.ExpiresAt.Value.FromCoinhakoTime(CurrentTime)
                : null,
        };
        tracked.OrderId = order.Id;
        TrackOrder(tracked);

        var delivery = new OrderDeliveryKey(originalTransactionId, order.Id);
        var signature = GetOrderSignature(order, volume);
        using (_sync.EnterScope())
        {
            if (!isForced && _orderSignatures.TryGetValue(delivery,
                out var previous) && previous == signature)
                return;
            _orderSignatures[delivery] = signature;
        }

        var state = order.Status.ToStockSharp();
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.Symbol.ToStockSharp(),
            ServerTime = order.CreatedAt.FromCoinhakoTime(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderVolume = volume,
            Balance = state == OrderStates.Active ? volume : 0m,
            OrderPrice = order.Price,
            AveragePrice = order.Status == CoinhakoOrderStatuses.Completed &&
                order.Price > 0
                ? order.Price
                : null,
            OrderType = tracked.OrderType,
            OrderState = state,
            OrderId = order.Id,
            OrderStringId = order.Id.ToString(CultureInfo.InvariantCulture),
            UserOrderId = order.ClientOrderId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            TimeInForce = tracked.OrderType == OrderTypes.Limit
                ? TimeInForce.PutInQueue
                : null,
            ExpiryDate = tracked.ExpiryDate,
        }, cancellationToken);

        if (order.Status == CoinhakoOrderStatuses.Completed &&
            volume > 0 && order.Price > 0)
            await SendCompletedFillAsync(order, tracked, volume, delivery,
                cancellationToken);
    }

    private async ValueTask SendCompletedFillAsync(CoinhakoOrder order,
        TrackedOrder tracked, decimal volume, OrderDeliveryKey delivery,
        CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
            if (!_reportedFills.Add(delivery))
                return;
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = order.Symbol.ToStockSharp(),
            ServerTime = order.CreatedAt.FromCoinhakoTime(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderId = order.Id,
            OrderStringId = order.Id.ToString(CultureInfo.InvariantCulture),
            TradeStringId = $"{order.Id.ToString(CultureInfo.InvariantCulture)}:completed",
            TradePrice = order.Price,
            TradeVolume = volume,
            Commission = order.Fee is { } fee && fee != 0m ? fee : null,
            CommissionCurrency = order.FeeCurrency,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = delivery.TargetId,
        }, cancellationToken);
    }

    private static decimal GetBaseVolume(CoinhakoOrder order,
        TrackedOrder tracked)
    {
        if (tracked?.Volume > 0)
            return tracked.Volume;
        var baseCurrency = order.BaseCurrency;
        var counterCurrency = order.CounterCurrency;
        if ((baseCurrency.IsEmpty() || counterCurrency.IsEmpty()) &&
            !order.Symbol.IsEmpty())
        {
            var symbol = order.Symbol.NormalizeCoinhakoSymbol();
            var separator = symbol.LastIndexOf('-');
            if (separator > 0 && separator < symbol.Length - 1)
            {
                baseCurrency ??= symbol[..separator];
                counterCurrency ??= symbol[(separator + 1)..];
            }
        }
        if (order.Currency.EqualsIgnoreCase(baseCurrency))
            return order.Quantity;
        if (order.Currency.EqualsIgnoreCase(counterCurrency) && order.Price > 0)
            return order.Quantity / order.Price;
        if (order.ReceivedCurrency.EqualsIgnoreCase(baseCurrency) &&
            order.NetAmount is > 0)
            return order.NetAmount.Value;
        return 0m;
    }

    private static string GetOrderSignature(CoinhakoOrder order,
        decimal volume)
        => string.Join('|',
            order.Status.ToString(),
            order.Price.ToString(CultureInfo.InvariantCulture),
            volume.ToString(CultureInfo.InvariantCulture),
            order.Fee?.ToString(CultureInfo.InvariantCulture),
            order.NetAmount?.ToString(CultureInfo.InvariantCulture),
            order.CancelReason,
            order.ExpiresAt?.ToString(CultureInfo.InvariantCulture));

    private async ValueTask RefreshOrderSubscriptionsAsync(
        CancellationToken cancellationToken)
    {
        KeyValuePair<long, OrderSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _orderSubscriptions];
        foreach (var pair in subscriptions)
        {
            var orders = await LoadOrdersAsync(pair.Value, true,
                cancellationToken);
            foreach (var order in orders)
                await SendOrderAsync(order, pair.Key, false,
                    cancellationToken);
        }
    }

    private async ValueTask RefreshPortfolioSubscriptionsAsync(
        CancellationToken cancellationToken)
    {
        long[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _portfolioSubscriptions];
        if (subscriptions.Length == 0)
            return;
        var balances = await RestClient.GetBalancesAsync(null,
            cancellationToken) ?? [];
        foreach (var subscription in subscriptions)
            foreach (var balance in balances)
                await SendBalanceAsync(balance, subscription,
                    cancellationToken);
    }

    private void RemoveOrderSubscription(long transactionId)
    {
        using (_sync.EnterScope())
        {
            _orderSubscriptions.Remove(transactionId);
            foreach (var key in _orderSignatures.Keys.Where(key =>
                key.TargetId == transactionId).ToArray())
                _orderSignatures.Remove(key);
            _reportedFills.RemoveWhere(key => key.TargetId == transactionId);
        }
    }

    private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }

    private static bool IsUncertainOperation(Exception error)
        => error is HttpRequestException or TaskCanceledException ||
            error is CoinhakoApiException api &&
                (int)api.StatusCode >= 500;

    private static long ToMilliseconds(DateTime value)
        => new DateTimeOffset(value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime()).ToUnixTimeMilliseconds();

    private static string ToWire(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);
}
