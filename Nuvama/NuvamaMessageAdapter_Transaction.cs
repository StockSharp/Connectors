namespace StockSharp.Nuvama;

public partial class NuvamaMessageAdapter
{
    private readonly SynchronizedDictionary<string, long> _orderTransactions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<long, string> _transactionOrders =
        [];
    private readonly SynchronizedSet<string> _tradeIds =
        new(StringComparer.OrdinalIgnoreCase);
    private long _orderStatusSubscriptionId;
    private long _portfolioSubscriptionId;

    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(regMsg.PortfolioName);
        var orderType = regMsg.OrderType ?? OrderTypes.Limit;
        ValidateOrderType(orderType);
        if (regMsg.TimeInForce == TimeInForce.MatchOrCancel)
        {
            throw new NotSupportedException(
                "Nuvama does not expose fill-or-kill orders.");
        }

        var condition = regMsg.Condition as NuvamaOrderCondition;
        var triggerPrice = condition?.TriggerPrice;
        if (orderType == OrderTypes.Conditional &&
            triggerPrice is not > 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Nuvama stop order.");
        }
        if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regMsg.Price),
                regMsg.Price,
                "A positive limit price is required.");
        }

        var quantity = ToQuantity(
            regMsg.Volume,
            nameof(regMsg.Volume),
            false);
        var disclosedQuantity = ToQuantity(
            condition?.DisclosedVolume ?? 0,
            nameof(condition.DisclosedVolume),
            true);
        if (disclosedQuantity > quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(condition.DisclosedVolume),
                disclosedQuantity,
                "Disclosed quantity cannot exceed order quantity.");
        }

        var instrument = await GetInstrument(
            regMsg.SecurityId.ToInstrumentKey(),
            cancellationToken);
        var product = condition?.Product ?? DefaultProduct;
        var orderId = await _restClient.PlaceOrder(
            new NuvamaOrderRequest
            {
                TradingSymbol = instrument.TradingSymbol,
                Exchange = instrument.Exchange,
                Action = regMsg.Side.ToNative(),
                Duration = regMsg.TimeInForce.ToValidity(),
                OrderType = orderType.ToNative(regMsg.Price),
                Quantity = quantity.ToString(CultureInfo.InvariantCulture),
                DisclosedQuantity = disclosedQuantity.ToString(
                    CultureInfo.InvariantCulture),
                StreamingSymbol = instrument.ExchangeToken,
                LimitPrice = FormatPrice(
                    orderType == OrderTypes.Market ? 0 : regMsg.Price),
                TriggerPrice = FormatPrice(triggerPrice ?? 0),
                ProductCode = product.ToNative(),
                Remark = condition?.Remark.IsEmpty(string.Empty),
                EmployeeOrDependent = EmployeeOrDependent,
            },
            cancellationToken);

        RememberOrder(orderId, regMsg.TransactionId);
        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                OriginalTransactionId = regMsg.TransactionId,
                OrderStringId = orderId,
                SecurityId = regMsg.SecurityId,
                PortfolioName = _resolvedPortfolioName,
                OrderType = orderType,
                Side = regMsg.Side,
                TimeInForce =
                    regMsg.TimeInForce ?? TimeInForce.PutInQueue,
                OrderPrice = regMsg.Price,
                OrderVolume = regMsg.Volume,
                Balance = regMsg.Volume,
                OrderState = OrderStates.Pending,
                ServerTime = CurrentTime,
                Condition = CreateCondition(
                    product,
                    triggerPrice,
                    condition?.DisclosedVolume,
                    condition?.Remark),
            },
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(replaceMsg.PortfolioName);
        if (replaceMsg.TimeInForce == TimeInForce.MatchOrCancel)
        {
            throw new NotSupportedException(
                "Nuvama does not expose fill-or-kill orders.");
        }

        var current = await ResolveOrder(
            replaceMsg.OldOrderStringId,
            replaceMsg.OriginalTransactionId,
            cancellationToken);
        var orderType = replaceMsg.OrderType ??
            current.EffectiveOrderType().ToOrderType();
        ValidateOrderType(orderType);
        var condition = replaceMsg.Condition as NuvamaOrderCondition;
        var triggerPrice = condition?.TriggerPrice ??
            Positive(current.TriggerPrice);
        if (orderType == OrderTypes.Conditional &&
            triggerPrice is not > 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Nuvama stop order.");
        }
        if (orderType == OrderTypes.Limit && replaceMsg.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replaceMsg.Price),
                replaceMsg.Price,
                "A positive limit price is required.");
        }

        var quantity = ToQuantity(
            replaceMsg.Volume,
            nameof(replaceMsg.Volume),
            false);
        var disclosedQuantity = ToQuantity(
            condition?.DisclosedVolume ??
                current.DisclosedQuantity.ToDecimal(),
            nameof(condition.DisclosedVolume),
            true);
        if (disclosedQuantity > quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(condition.DisclosedVolume),
                disclosedQuantity,
                "Disclosed quantity cannot exceed order quantity.");
        }

        var instrument = await ResolveInstrument(
            current.Exchange,
            current.StreamingSymbol,
            current.TradingSymbol,
            cancellationToken);
        await _restClient.ModifyOrder(
            new NuvamaModifyOrderRequest
            {
                TradingSymbol =
                    current.TradingSymbol.IsEmpty(instrument.TradingSymbol),
                Exchange = current.Exchange.IsEmpty(instrument.Exchange),
                Action = current.EffectiveSide(),
                Duration = ((TimeInForce?)(
                    replaceMsg.TimeInForce ??
                    current.Duration.ToTimeInForce())).ToValidity(),
                FilledQuantity = current.FilledQuantity.IsEmpty("0"),
                OrderType = orderType.ToNative(replaceMsg.Price),
                Quantity = quantity.ToString(CultureInfo.InvariantCulture),
                DisclosedQuantity = disclosedQuantity.ToString(
                    CultureInfo.InvariantCulture),
                StreamingSymbol = current.StreamingSymbol
                    .IsEmpty(instrument.ExchangeToken),
                LimitPrice = FormatPrice(
                    orderType == OrderTypes.Market
                        ? 0
                        : replaceMsg.Price),
                TriggerPrice = FormatPrice(triggerPrice ?? 0),
                ProductCode = current.ProductCode
                    .IsEmpty(DefaultProduct.ToNative()),
                OrderId = current.EffectiveOrderId(),
                CurrentQuantity = current.EffectiveQuantity().ToString(
                    CultureInfo.InvariantCulture),
                EmployeeOrDependent = EmployeeOrDependent,
            },
            cancellationToken);
        RememberOrder(current.EffectiveOrderId(), replaceMsg.TransactionId);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(cancelMsg.PortfolioName);
        var current = await ResolveOrder(
            cancelMsg.OrderStringId,
            cancelMsg.OriginalTransactionId,
            cancellationToken);
        var instrument = await ResolveInstrument(
            current.Exchange,
            current.StreamingSymbol,
            current.TradingSymbol,
            cancellationToken);
        await _restClient.CancelOrder(
            new NuvamaCancelOrderRequest
            {
                OrderId = current.EffectiveOrderId(),
                Exchange = current.Exchange.IsEmpty(instrument.Exchange),
                ProductCode = current.ProductCode
                    .IsEmpty(DefaultProduct.ToNative()),
                OrderType = current.EffectiveOrderType().IsEmpty("LIMIT"),
                CurrentQuantity = current.EffectiveQuantity().ToString(
                    CultureInfo.InvariantCulture),
                FilledQuantity =
                    current.FilledQuantity.IsEmpty("0"),
                TradingSymbol = current.TradingSymbol
                    .IsEmpty(instrument.TradingSymbol),
                Action = current.EffectiveSide(),
                StreamingSymbol = current.StreamingSymbol
                    .IsEmpty(instrument.ExchangeToken),
                EmployeeOrDependent = EmployeeOrDependent,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OrderStatusAsync(
        OrderStatusMessage statusMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            statusMsg.TransactionId,
            cancellationToken);
        if (!statusMsg.IsSubscribe)
        {
            if (_orderStatusSubscriptionId ==
                statusMsg.OriginalTransactionId)
                _orderStatusSubscriptionId = 0;
            return;
        }

        EnsurePortfolio(statusMsg.PortfolioName);
        await SendOrderSnapshot(
            statusMsg.TransactionId,
            true,
            cancellationToken,
            statusMsg.From,
            statusMsg.To,
            statusMsg.Count);
        _lastOrderRefresh = CurrentTime;

        if (statusMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                statusMsg.TransactionId,
                cancellationToken);
        }
        else
        {
            _orderStatusSubscriptionId = statusMsg.TransactionId;
            await SendSubscriptionResultAsync(
                statusMsg,
                cancellationToken);
        }
    }

    private async ValueTask SendOrderSnapshot(
        long originalTransactionId,
        bool isLookup,
        CancellationToken cancellationToken,
        DateTime? from = null,
        DateTime? to = null,
        long? count = null)
    {
        var left = count ?? long.MaxValue;
        foreach (var order in (await _restClient.GetOrders(cancellationToken))
            .Where(order => order != null)
            .OrderBy(GetOrderTime))
        {
            var time = GetOrderTime(order);
            if (from is DateTime fromTime &&
                time < fromTime.ToUniversalTime())
                continue;
            if (to is DateTime toTime &&
                time > toTime.ToUniversalTime())
                continue;
            await ProcessOrder(
                order,
                originalTransactionId,
                isLookup,
                cancellationToken);
            if (--left <= 0)
                break;
        }

        foreach (var trade in await _restClient.GetTrades(cancellationToken))
        {
            await ProcessTrade(
                trade,
                originalTransactionId,
                isLookup,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(
        PortfolioLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId,
            cancellationToken);
        if (!lookupMsg.IsSubscribe)
        {
            if (_portfolioSubscriptionId ==
                lookupMsg.OriginalTransactionId)
                _portfolioSubscriptionId = 0;
            return;
        }

        EnsurePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(
            new PortfolioMessage
            {
                OriginalTransactionId = lookupMsg.TransactionId,
                PortfolioName = _resolvedPortfolioName,
                BoardCode = "NSE",
            },
            cancellationToken);
        await SendPortfolioSnapshot(
            lookupMsg.TransactionId,
            cancellationToken);
        _lastPortfolioRefresh = CurrentTime;

        if (lookupMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                lookupMsg.TransactionId,
                cancellationToken);
        }
        else
        {
            _portfolioSubscriptionId = lookupMsg.TransactionId;
            await SendSubscriptionResultAsync(
                lookupMsg,
                cancellationToken);
        }
    }

    private async ValueTask SendPortfolioSnapshot(
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var limits = await _restClient.GetLimits(cancellationToken);
        var currentCash = limits.CashAvailable.ToDecimal();
        var availableMargin =
            limits.MarginAvailable?.Value.ToDecimal() ?? 0m;
        var openingBalance =
            limits.MarginAvailable?.DayOpeningBalance.ToDecimal() ?? 0m;
        var usedMargin = limits.MarginUsed?.Value.ToDecimal() ?? 0m;
        await SendOutMessageAsync(
            new PositionChangeMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = _resolvedPortfolioName,
                SecurityId = SecurityId.Money,
                ServerTime = CurrentTime,
            }
            .TryAdd(
                PositionChangeTypes.BeginValue,
                openingBalance,
                true)
            .TryAdd(
                PositionChangeTypes.CurrentValue,
                currentCash != 0 ? currentCash : availableMargin,
                true)
            .TryAdd(
                PositionChangeTypes.BlockedValue,
                usedMargin,
                true),
            cancellationToken);

        foreach (var position in await _restClient.GetPositions(
            cancellationToken))
        {
            if (position == null ||
                position.Exchange.IsEmpty() ||
                position.StreamingSymbol.IsEmpty())
                continue;
            var netQuantity = position.NetQuantity.ToDecimal();
            var averagePrice = netQuantity < 0
                ? position.AverageSellPrice.ToDecimal()
                : position.AverageBuyPrice.ToDecimal();
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originalTransactionId,
                    PortfolioName = _resolvedPortfolioName,
                    SecurityId = position.Exchange.ToSecurityId(
                        position.StreamingSymbol,
                        position.TradingSymbol),
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    netQuantity,
                    true)
                .TryAdd(
                    PositionChangeTypes.AveragePrice,
                    Positive(averagePrice),
                    true)
                .TryAdd(
                    PositionChangeTypes.CurrentPrice,
                    Positive(position.LastPrice),
                    true)
                .TryAdd(
                    PositionChangeTypes.RealizedPnL,
                    position.RealizedPnL.ToDecimal(),
                    true)
                .TryAdd(
                    PositionChangeTypes.UnrealizedPnL,
                    position.UnrealizedPnL.ToDecimal(),
                    true),
                cancellationToken);
        }

        foreach (var holding in await _restClient.GetHoldings(
            cancellationToken))
        {
            if (holding == null ||
                holding.Exchange.IsEmpty() ||
                holding.StreamingSymbol.IsEmpty())
                continue;
            var blocked =
                (holding.Cnc?.HoldingUsedQuantity.ToDecimal() ?? 0m) +
                (holding.Cnc?.PledgedQuantity.ToDecimal() ?? 0m) +
                (holding.Mtf?.HoldingUsedQuantity.ToDecimal() ?? 0m) +
                (holding.Mtf?.PledgedQuantity.ToDecimal() ?? 0m);
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originalTransactionId,
                    PortfolioName = _resolvedPortfolioName,
                    SecurityId = holding.Exchange.ToSecurityId(
                        holding.StreamingSymbol,
                        holding.TradingSymbol,
                        holding.Isin),
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    holding.EffectiveHoldingQuantity(),
                    true)
                .TryAdd(
                    PositionChangeTypes.BlockedValue,
                    blocked,
                    true)
                .TryAdd(
                    PositionChangeTypes.CurrentPrice,
                    Positive(holding.LastPrice),
                    true),
                cancellationToken);
        }
    }

    private async ValueTask OnOrderStreamReceived(
        JToken update,
        CancellationToken cancellationToken)
    {
        if (update == null)
            return;

        var orders = NuvamaExtensions.FindToken(
            update,
            "ord",
            "orders") as JArray;
        if (orders != null)
        {
            foreach (var item in orders)
                await ProcessOrderStreamItem(item, cancellationToken);
        }
        var trades = NuvamaExtensions.FindToken(
            update,
            "trade",
            "trades") as JArray;
        if (trades != null)
        {
            foreach (var item in trades)
                await ProcessTradeStreamItem(item, cancellationToken);
        }
        if (orders != null || trades != null)
            return;

        var responseType = NuvamaExtensions.FindString(
            update,
            "responseType",
            "type",
            "msgType");
        var isTrade =
            responseType?.Contains(
                "TRADE",
                StringComparison.OrdinalIgnoreCase) == true ||
            NuvamaExtensions.FindToken(
                update,
                "trdID",
                "fldQty",
                "flPrc") != null;
        if (isTrade)
            await ProcessTradeStreamItem(update, cancellationToken);
        else
            await ProcessOrderStreamItem(update, cancellationToken);
    }

    private async ValueTask ProcessOrderStreamItem(
        JToken item,
        CancellationToken cancellationToken)
    {
        var order = item.ToObject<NuvamaOrder>();
        var orderId = order?.EffectiveOrderId();
        if (orderId.IsEmpty())
            return;
        var transactionId = _orderTransactions.TryGetValue2(orderId) ?? 0;
        if (transactionId == 0 && _orderStatusSubscriptionId == 0)
            return;
        await ProcessOrder(
            order,
            _orderStatusSubscriptionId,
            false,
            cancellationToken);
    }

    private async ValueTask ProcessTradeStreamItem(
        JToken item,
        CancellationToken cancellationToken)
    {
        var trade = item.ToObject<NuvamaTrade>();
        if (trade?.OrderId.IsEmpty() != false)
            return;
        var transactionId =
            _orderTransactions.TryGetValue2(trade.OrderId) ?? 0;
        if (transactionId == 0 && _orderStatusSubscriptionId == 0)
            return;
        await ProcessTrade(
            trade,
            _orderStatusSubscriptionId,
            false,
            cancellationToken);
    }

    private async ValueTask ProcessOrder(
        NuvamaOrder order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        var orderId = order?.EffectiveOrderId();
        if (orderId.IsEmpty())
            return;

        _orderTransactions.TryGetValue(orderId, out var transactionId);
        RememberOrder(orderId, transactionId);
        var state = order.Status.ToOrderState();
        var quantity = order.EffectiveQuantity();
        var filled = order.FilledQuantity.ToDecimal();
        var balance = !order.PendingQuantity.IsEmpty()
            ? order.PendingQuantity.ToDecimal()
            : Math.Max(0, quantity - filled);
        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                OriginalTransactionId = isLookup
                    ? originId
                    : transactionId != 0
                        ? transactionId
                        : _orderStatusSubscriptionId,
                TransactionId = isLookup ? transactionId : 0,
                OrderStringId = orderId,
                SecurityId = await GetSecurityId(
                    order.Exchange,
                    order.StreamingSymbol,
                    order.TradingSymbol,
                    cancellationToken),
                PortfolioName = _resolvedPortfolioName,
                OrderType = order.EffectiveOrderType().ToOrderType(),
                Side = order.EffectiveSide().ToSide(),
                TimeInForce = order.Duration.ToTimeInForce(),
                OrderPrice = order.Price.ToDecimal(),
                OrderVolume = quantity,
                Balance = balance,
                AveragePrice = Positive(order.AveragePrice),
                OrderState = state,
                ServerTime = GetOrderTime(order),
                Condition = CreateCondition(
                    order.ProductCode.ToProduct(),
                    Positive(order.TriggerPrice),
                    Positive(order.DisclosedQuantity),
                    null),
                Error = state == OrderStates.Failed
                    ? new InvalidOperationException(
                        order.RejectionReason.IsEmpty(
                            $"Nuvama order status: {order.Status}."))
                    : null,
            },
            cancellationToken);
    }

    private async ValueTask ProcessTrade(
        NuvamaTrade trade,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (trade == null || trade.OrderId.IsEmpty())
            return;
        var price = trade.EffectiveFilledPrice();
        var volume = trade.EffectiveFilledQuantity();
        var fillId = trade.EffectiveTradeId().IsEmpty(
            $"{trade.OrderId}:{trade.FillDate}:{trade.FillTime}:{price}:{volume}");
        if (!_tradeIds.TryAdd(fillId))
            return;

        var transactionId =
            _orderTransactions.TryGetValue2(trade.OrderId) ?? 0;
        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                OriginalTransactionId = isLookup
                    ? originId
                    : transactionId != 0
                        ? transactionId
                        : _orderStatusSubscriptionId,
                TransactionId = isLookup ? transactionId : 0,
                OrderStringId = trade.OrderId,
                TradeStringId = fillId,
                SecurityId = await GetSecurityId(
                    trade.Exchange,
                    trade.StreamingSymbol,
                    trade.TradingSymbol,
                    cancellationToken),
                PortfolioName = _resolvedPortfolioName,
                Side = trade.TransactionType.ToSide(),
                TradePrice = price,
                TradeVolume = volume,
                ServerTime = GetTradeTime(trade),
            },
            cancellationToken);
    }

    private async Task<NuvamaOrder> ResolveOrder(
        string orderId,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (orderId.IsEmpty())
            _transactionOrders.TryGetValue(originalTransactionId, out orderId);
        if (orderId.IsEmpty())
        {
            throw new InvalidOperationException(
                LocalizedStrings.OrderNoExchangeId.Put(
                    originalTransactionId));
        }

        foreach (var order in await _restClient.GetOrders(cancellationToken))
        {
            var currentId = order?.EffectiveOrderId();
            if (currentId.IsEmpty())
                continue;
            _orderTransactions.TryGetValue(
                currentId,
                out var transactionId);
            RememberOrder(currentId, transactionId);
            if (currentId.EqualsIgnoreCase(orderId) ||
                transactionId != 0 &&
                transactionId == originalTransactionId)
                return order;
        }

        throw new InvalidOperationException(
            $"Nuvama order '{orderId}' was not found in the current order book.");
    }

    private async Task<NuvamaInstrument> ResolveInstrument(
        string exchange,
        string streamingSymbol,
        string tradingSymbol,
        CancellationToken cancellationToken)
    {
        if (!exchange.IsEmpty() && !streamingSymbol.IsEmpty())
        {
            var instrument = await _restClient.GetInstrument(
                exchange.ToInstrumentKey(streamingSymbol),
                cancellationToken);
            if (instrument != null)
                return instrument;
        }

        return await _restClient.FindInstrument(
            exchange,
            tradingSymbol,
            cancellationToken) ??
            throw new InvalidOperationException(
                $"Nuvama instrument '{exchange}|{streamingSymbol}|{tradingSymbol}' was not found.");
    }

    private async Task<SecurityId> GetSecurityId(
        string exchange,
        string streamingSymbol,
        string tradingSymbol,
        CancellationToken cancellationToken)
    {
        if (!exchange.IsEmpty() && !streamingSymbol.IsEmpty())
        {
            return exchange.ToSecurityId(
                streamingSymbol,
                tradingSymbol);
        }
        var instrument = await _restClient.FindInstrument(
            exchange,
            tradingSymbol,
            cancellationToken);
        if (instrument != null)
            return instrument.ToSecurityId();
        return new()
        {
            SecurityCode = tradingSymbol,
            BoardCode = exchange.ToBoardCode(),
        };
    }

    private void RememberOrder(string orderId, long transactionId)
    {
        if (orderId.IsEmpty() || transactionId == 0)
            return;
        _orderTransactions[orderId] = transactionId;
        _transactionOrders[transactionId] = orderId;
    }

    private void EnsurePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(_resolvedPortfolioName))
            throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
    }

    private static void ValidateOrderType(OrderTypes orderType)
    {
        if (orderType is not OrderTypes.Limit and
            not OrderTypes.Market and
            not OrderTypes.Conditional)
        {
            throw new ArgumentOutOfRangeException(
                nameof(orderType),
                orderType,
                "Nuvama supports market, limit, stop-limit, and stop-market orders.");
        }
    }

    private static long ToQuantity(
        decimal value,
        string parameterName,
        bool allowZero)
    {
        if (value < 0 ||
            !allowZero && value == 0 ||
            value != decimal.Truncate(value) ||
            value > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Nuvama quantities must be non-negative whole numbers within Int64 range.");
        }
        return decimal.ToInt64(value);
    }

    private static string FormatPrice(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static decimal? Positive(decimal value)
        => value > 0 ? value : null;

    private static NuvamaOrderCondition CreateCondition(
        NuvamaProducts product,
        decimal? triggerPrice,
        decimal? disclosedVolume,
        string remark)
        => new()
        {
            Product = product,
            TriggerPrice = triggerPrice,
            DisclosedVolume = disclosedVolume,
            Remark = remark,
        };

    private DateTime GetOrderTime(NuvamaOrder order)
        => order.EpochTime.ToNuvamaTime() ??
            order.ReceivedEpochTime.ToNuvamaTime() ??
            order.OrderTime.ToNuvamaTime() ??
            order.ReceivedTime.ToNuvamaTime() ??
            CurrentTime;

    private DateTime GetTradeTime(NuvamaTrade trade)
    {
        var combined = trade.FillDate.IsEmpty()
            ? trade.FillTime
            : $"{trade.FillDate} {trade.FillTime}";
        return combined.ToNuvamaTime() ??
            trade.FillTime.ToNuvamaTime() ??
            trade.OrderTime.ToNuvamaTime() ??
            CurrentTime;
    }
}
