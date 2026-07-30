namespace StockSharp.WisdomCapital;

public partial class WisdomCapitalMessageAdapter
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
        var instrument = await ResolveInstrument(
            regMsg.SecurityId,
            cancellationToken);
        if (instrument.ToSecurityType() == SecurityTypes.Index)
        {
            throw new NotSupportedException(
                "Wisdom Capital XTS index instruments cannot be traded.");
        }
        var condition = regMsg.Condition as WisdomCapitalOrderCondition;
        var product = condition?.Product ?? DefaultProduct;
        var orderType = regMsg.OrderType ?? OrderTypes.Limit;
        var uniqueIdentifier = condition?.UniqueIdentifier
            .IsEmpty(
                $"StockSharp-{regMsg.TransactionId.ToString(CultureInfo.InvariantCulture)}");
        var payload = CreateOrderPayload(
            instrument,
            regMsg.Volume,
            regMsg.Side,
            product,
            orderType,
            regMsg.Price,
            regMsg.TimeInForce,
            condition?.TriggerPrice,
            condition?.DisclosedVolume,
            uniqueIdentifier);
        var orderId = await _restClient.PlaceOrder(
            payload,
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
                    condition?.TriggerPrice,
                    condition?.DisclosedVolume,
                    uniqueIdentifier),
            },
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(replaceMsg.PortfolioName);
        var current = await ResolveOrder(
            replaceMsg.OldOrderStringId,
            replaceMsg.OriginalTransactionId,
            cancellationToken);
        var condition =
            replaceMsg.Condition as WisdomCapitalOrderCondition;
        var product = condition?.Product ??
            current.ProductType.ToProduct();
        var orderType = replaceMsg.OrderType ??
            current.OrderType.ToOrderType();
        var uniqueIdentifier = condition?.UniqueIdentifier
            .IsEmpty(current.UniqueIdentifier)
            .IsEmpty(
                $"StockSharp-{replaceMsg.TransactionId.ToString(CultureInfo.InvariantCulture)}");
        var payload = CreateModifyPayload(
            current.OrderId,
            replaceMsg.Volume,
            product,
            orderType,
            replaceMsg.Price,
            replaceMsg.TimeInForce ??
                current.TimeInForce.ToTimeInForce(),
            condition?.TriggerPrice ??
                Positive(current.StopPrice),
            condition?.DisclosedVolume ??
                Positive(current.DisclosedQuantity),
            uniqueIdentifier);
        var orderId = await _restClient.ModifyOrder(
            payload,
            cancellationToken);
        RememberOrder(
            orderId.IsEmpty(current.OrderId),
            replaceMsg.TransactionId);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(cancelMsg.PortfolioName);
        var order = await ResolveOrder(
            cancelMsg.OrderStringId,
            cancelMsg.OriginalTransactionId,
            cancellationToken);
        await _restClient.CancelOrder(
            order.OrderId,
            order.UniqueIdentifier,
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

        foreach (var order in (await _restClient.GetOrders(
            cancellationToken))
            .Where(order => order != null && !order.OrderId.IsEmpty())
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

        foreach (var trade in await _restClient.GetTrades(
            cancellationToken))
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
        var funds = await _restClient.GetFunds(cancellationToken);
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
                funds.Available + funds.Utilized,
                true)
            .TryAdd(
                PositionChangeTypes.CurrentValue,
                funds.Available,
                true)
            .TryAdd(
                PositionChangeTypes.BlockedValue,
                funds.Utilized,
                true)
            .TryAdd(
                PositionChangeTypes.UnrealizedPnL,
                funds.UnrealizedPnl,
                true)
            .TryAdd(
                PositionChangeTypes.RealizedPnL,
                funds.RealizedPnl,
                true),
            cancellationToken);

        foreach (var position in await _restClient.GetPositions(
            cancellationToken))
        {
            if (position == null ||
                position.ExchangeInstrumentId <= 0 ||
                position.ExchangeSegment.IsEmpty())
                continue;
            var average = position.Quantity switch
            {
                > 0 => position.BuyAveragePrice,
                < 0 => position.SellAveragePrice,
                _ => position.BreakEvenPrice,
            };
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originalTransactionId,
                    PortfolioName = _resolvedPortfolioName,
                    SecurityId = await GetSecurityId(
                        position.ExchangeSegment,
                        position.ExchangeInstrumentId,
                        position.TradingSymbol,
                        null,
                        cancellationToken),
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    position.Quantity,
                    true)
                .TryAdd(
                    PositionChangeTypes.AveragePrice,
                    Positive(average),
                    true)
                .TryAdd(
                    PositionChangeTypes.UnrealizedPnL,
                    position.UnrealizedPnl,
                    true)
                .TryAdd(
                    PositionChangeTypes.RealizedPnL,
                    position.RealizedPnl,
                    true),
                cancellationToken);
        }

        foreach (var holding in await _restClient.GetHoldings(
            cancellationToken))
        {
            if (holding == null ||
                holding.ExchangeInstrumentId <= 0 &&
                holding.Isin.IsEmpty())
                continue;
            var securityId = await GetHoldingSecurityId(
                holding,
                cancellationToken);
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originalTransactionId,
                    PortfolioName = _resolvedPortfolioName,
                    SecurityId = securityId,
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    holding.Quantity,
                    true)
                .TryAdd(
                    PositionChangeTypes.AveragePrice,
                    Positive(holding.AveragePrice),
                    true),
                cancellationToken);
        }
    }

    private async ValueTask ProcessOrder(
        WisdomOrder order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (order?.OrderId.IsEmpty() != false)
            return;

        _orderTransactions.TryGetValue(
            order.OrderId,
            out var transactionId);
        RememberOrder(order.OrderId, transactionId);
        var state = order.OrderStatus.ToOrderState();
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
                OrderStringId = order.OrderId,
                SecurityId = await GetSecurityId(
                    order.ExchangeSegment,
                    order.ExchangeInstrumentId,
                    null,
                    null,
                    cancellationToken),
                PortfolioName = _resolvedPortfolioName,
                OrderType = order.OrderType.ToOrderType(),
                Side = order.OrderSide.ToSide(),
                TimeInForce = order.TimeInForce.ToTimeInForce(),
                OrderPrice = order.OrderPrice,
                OrderVolume = order.OrderQuantity,
                Balance = Math.Max(0, order.LeavesQuantity),
                AveragePrice = Positive(order.AveragePrice),
                OrderState = state,
                ServerTime = GetOrderTime(order),
                Condition = CreateCondition(
                    order.ProductType.ToProduct(),
                    Positive(order.StopPrice),
                    Positive(order.DisclosedQuantity),
                    order.UniqueIdentifier),
                Error = state == OrderStates.Failed
                    ? new InvalidOperationException(
                        order.RejectReason.IsEmpty(
                            $"Wisdom Capital XTS order status: {order.OrderStatus}."))
                    : null,
            },
            cancellationToken);
    }

    private async ValueTask ProcessTrade(
        WisdomTrade trade,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (trade == null ||
            trade.OrderId.IsEmpty() ||
            trade.Quantity <= 0)
            return;
        var tradeId = trade.ExecutionId.IsEmpty(
            $"{trade.OrderId}:{trade.ExecutionTime}:{trade.Price}:{trade.Quantity}");
        if (!_tradeIds.TryAdd(tradeId))
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
                TradeStringId = tradeId,
                SecurityId = await GetSecurityId(
                    trade.ExchangeSegment,
                    trade.ExchangeInstrumentId,
                    null,
                    null,
                    cancellationToken),
                PortfolioName = _resolvedPortfolioName,
                Side = trade.OrderSide.ToSide(),
                TradePrice = trade.Price,
                TradeVolume = trade.Quantity,
                ServerTime = trade.ExecutionTime
                    .IsEmpty(trade.OrderTime)
                    .ToWisdomTime(CurrentTime),
            },
            cancellationToken);
    }

    private async Task<WisdomOrder> ResolveOrder(
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

        foreach (var order in await _restClient.GetOrders(
            cancellationToken))
        {
            if (order?.OrderId.IsEmpty() != false)
                continue;
            _orderTransactions.TryGetValue(
                order.OrderId,
                out var transactionId);
            RememberOrder(order.OrderId, transactionId);
            if (order.OrderId.EqualsIgnoreCase(orderId) ||
                transactionId != 0 &&
                    transactionId == originalTransactionId)
                return order;
        }

        throw new InvalidOperationException(
            $"Wisdom Capital XTS order '{orderId}' was not found in the current order book.");
    }

    private async Task<SecurityId> GetSecurityId(
        string segment,
        long instrumentId,
        string symbol,
        string isin,
        CancellationToken cancellationToken)
    {
        var instrument = instrumentId > 0 && !segment.IsEmpty()
            ? await _restClient.GetInstrument(
                segment,
                instrumentId,
                cancellationToken)
            : null;
        if (instrument == null &&
            !segment.IsEmpty() &&
            !symbol.IsEmpty())
        {
            instrument = await _restClient.FindInstrument(
                segment,
                symbol,
                cancellationToken);
        }
        if (instrument != null)
        {
            var result = instrument.ToSecurityId();
            if (!isin.IsEmpty())
                result = result with { Isin = isin };
            RememberInstrument(instrument, result);
            return result;
        }
        return new()
        {
            SecurityCode = symbol
                .IsEmpty(
                    instrumentId > 0
                        ? instrumentId.ToString(CultureInfo.InvariantCulture)
                        : isin),
            BoardCode = segment.IsEmpty()
                ? "NSE"
                : segment.ToBoardCode(),
            Native = !segment.IsEmpty() && instrumentId > 0
                ? WisdomCapitalExtensions.CreateInstrumentKey(
                    segment,
                    instrumentId)
                : null,
            Isin = isin,
        };
    }

    private async Task<SecurityId> GetHoldingSecurityId(
        WisdomHolding holding,
        CancellationToken cancellationToken)
    {
        var instrument =
            holding.ExchangeInstrumentId > 0 &&
            !holding.ExchangeSegment.IsEmpty()
                ? await _restClient.GetInstrument(
                    holding.ExchangeSegment,
                    holding.ExchangeInstrumentId,
                    cancellationToken)
                : null;
        if (instrument != null)
        {
            var securityId = instrument.ToSecurityId() with
            {
                Isin = holding.Isin,
            };
            RememberInstrument(instrument, securityId);
            return securityId;
        }
        return new()
        {
            SecurityCode = holding.Isin.IsEmpty(
                holding.ExchangeInstrumentId.ToString(
                    CultureInfo.InvariantCulture)),
            BoardCode = holding.ExchangeSegment.IsEmpty()
                ? "NSE"
                : holding.ExchangeSegment.ToBoardCode(),
            Isin = holding.Isin,
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
        {
            throw new InvalidOperationException(
                LocalizedStrings.AccountNotFound);
        }
    }

    internal static JObject CreateOrderPayload(
        WisdomInstrument instrument,
        decimal volume,
        Sides side,
        WisdomCapitalProducts product,
        OrderTypes orderType,
        decimal price,
        TimeInForce? timeInForce,
        decimal? triggerPrice,
        decimal? disclosedVolume,
        string uniqueIdentifier)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        var payload = CreateMutableOrderFields(
            volume,
            product,
            orderType,
            price,
            timeInForce,
            triggerPrice,
            disclosedVolume,
            uniqueIdentifier,
            false);
        payload["exchangeSegment"] = instrument.ExchangeSegment;
        payload["exchangeInstrumentID"] =
            instrument.ExchangeInstrumentId;
        payload["orderSide"] = side.ToNative();
        payload["apiOrderSource"] = "WebAPI";
        return payload;
    }

    internal static JObject CreateModifyPayload(
        string orderId,
        decimal volume,
        WisdomCapitalProducts product,
        OrderTypes orderType,
        decimal price,
        TimeInForce? timeInForce,
        decimal? triggerPrice,
        decimal? disclosedVolume,
        string uniqueIdentifier)
    {
        var payload = CreateMutableOrderFields(
            volume,
            product,
            orderType,
            price,
            timeInForce,
            triggerPrice,
            disclosedVolume,
            uniqueIdentifier,
            true);
        payload["appOrderID"] = ParseOrderId(orderId);
        return payload;
    }

    private static JObject CreateMutableOrderFields(
        decimal volume,
        WisdomCapitalProducts product,
        OrderTypes orderType,
        decimal price,
        TimeInForce? timeInForce,
        decimal? triggerPrice,
        decimal? disclosedVolume,
        string uniqueIdentifier,
        bool modify)
    {
        var quantity = ToWholeQuantity(volume, nameof(volume));
        var disclosed = disclosedVolume ?? 0;
        if (disclosed < 0 ||
            disclosed != decimal.Truncate(disclosed) ||
            disclosed > quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(disclosedVolume),
                disclosed,
                "Disclosed quantity must be a whole number no greater than order quantity.");
        }
        if (timeInForce == TimeInForce.MatchOrCancel)
        {
            throw new NotSupportedException(
                "Wisdom Capital XTS does not expose fill-or-kill orders.");
        }
        if (orderType is not OrderTypes.Market and
            not OrderTypes.Limit and
            not OrderTypes.Conditional)
        {
            throw new ArgumentOutOfRangeException(
                nameof(orderType),
                orderType,
                "Wisdom Capital XTS supports market, limit, stop-market, and stop-limit orders.");
        }
        if (orderType == OrderTypes.Limit && price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                price,
                "A positive price is required for a limit order.");
        }
        if (orderType == OrderTypes.Conditional &&
            triggerPrice is not > 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a stop order.");
        }
        var nativeType = orderType switch
        {
            OrderTypes.Market => "MARKET",
            OrderTypes.Limit => "LIMIT",
            OrderTypes.Conditional when price > 0 => "STOPLIMIT",
            OrderTypes.Conditional => "STOPMARKET",
            _ => throw new ArgumentOutOfRangeException(
                nameof(orderType)),
        };
        return new()
        {
            [modify ? "modifiedProductType" : "productType"] =
                product.ToNative(),
            [modify ? "modifiedOrderType" : "orderType"] = nativeType,
            [modify ? "modifiedOrderQuantity" : "orderQuantity"] =
                quantity,
            [modify
                ? "modifiedDisclosedQuantity"
                : "disclosedQuantity"] = decimal.ToInt64(disclosed),
            [modify ? "modifiedLimitPrice" : "limitPrice"] =
                orderType == OrderTypes.Market ? 0 : price,
            [modify ? "modifiedStopPrice" : "stopPrice"] =
                triggerPrice ?? 0,
            [modify ? "modifiedTimeInForce" : "timeInForce"] =
                timeInForce == TimeInForce.CancelBalance
                    ? "IOC"
                    : "DAY",
            ["orderUniqueIdentifier"] =
                uniqueIdentifier.IsEmpty("StockSharp"),
        };
    }

    private static long ToWholeQuantity(
        decimal value,
        string parameterName)
    {
        if (value <= 0 ||
            value != decimal.Truncate(value) ||
            value > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Wisdom Capital XTS quantities must be positive whole numbers within Int64 range.");
        }
        return decimal.ToInt64(value);
    }

    private static long ParseOrderId(string value)
    {
        if (!long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result) ||
            result <= 0)
        {
            throw new InvalidOperationException(
                $"Wisdom Capital XTS AppOrderID '{value}' is invalid.");
        }
        return result;
    }

    private static WisdomCapitalOrderCondition CreateCondition(
        WisdomCapitalProducts product,
        decimal? triggerPrice,
        decimal? disclosedVolume,
        string uniqueIdentifier)
        => new()
        {
            Product = product,
            TriggerPrice = triggerPrice,
            DisclosedVolume = disclosedVolume,
            UniqueIdentifier = uniqueIdentifier,
        };

    private DateTime GetOrderTime(WisdomOrder order)
        => order.UpdateTime
            .IsEmpty(order.ExchangeTime)
            .IsEmpty(order.GeneratedTime)
            .ToWisdomTime(CurrentTime);
}
