namespace StockSharp.Mastertrust;

public partial class MastertrustMessageAdapter
{
    private readonly SynchronizedDictionary<string, long> _orderTransactions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
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
            throw new NotSupportedException("Mastertrust does not expose fill-or-kill orders.");
        ValidatePrice(orderType, regMsg.Price);

        var condition = regMsg.Condition as MastertrustOrderCondition;
        var triggerPrice = condition?.TriggerPrice ?? 0;
        if (orderType == OrderTypes.Conditional && triggerPrice <= 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Mastertrust stop order.");
        }

        var volume = ToQuantity(regMsg.Volume, nameof(regMsg.Volume));
        var disclosedVolume = ToDisclosedQuantity(
            condition?.DisclosedVolume,
            volume);
        var marketProtection = ValidateMarketProtection(
            condition?.MarketProtectionPercentage);
        var instrumentKey = regMsg.SecurityId.ToInstrumentKey();
        var (exchange, token) = instrumentKey.ParseInstrumentKey();
        var instrument = await GetInstrument(instrumentKey, cancellationToken);
        var quantity = exchange.ToNativeQuantity(volume, instrument.LotSize);
        var disclosed = disclosedVolume > 0
            ? exchange.ToNativeQuantity(disclosedVolume, instrument.LotSize)
            : 0;
        var product = condition?.Product ?? DefaultProduct;
        var userOrderId = condition?.UserOrderId.IsEmpty(
            regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));
        if (userOrderId?.Length > 50)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MastertrustOrderCondition.UserOrderId),
                userOrderId.Length,
                "Mastertrust user order identifiers cannot exceed 50 characters.");
        }

        var orderId = await _restClient.PlaceOrder(
            exchange,
            token,
            regMsg.Side,
            product,
            orderType,
            quantity,
            regMsg.Price,
            triggerPrice,
            disclosed,
            marketProtection,
            regMsg.TimeInForce,
            condition?.IsAfterMarket == true,
            userOrderId,
            cancellationToken);
        RememberOrder(orderId, regMsg.TransactionId);

        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = regMsg.TransactionId,
            OrderStringId = orderId,
            SecurityId = regMsg.SecurityId,
            PortfolioName = _resolvedPortfolioName,
            OrderType = orderType,
            Side = regMsg.Side,
            TimeInForce = regMsg.TimeInForce ?? TimeInForce.PutInQueue,
            OrderPrice = regMsg.Price,
            OrderVolume = regMsg.Volume,
            Balance = regMsg.Volume,
            OrderState = OrderStates.Pending,
            ServerTime = CurrentTime,
            Condition = CreateCondition(
                product,
                Positive(triggerPrice),
                condition?.IsAfterMarket == true,
                disclosedVolume,
                Positive(marketProtection),
                userOrderId),
        }, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(replaceMsg.PortfolioName);
        if (replaceMsg.TimeInForce == TimeInForce.MatchOrCancel)
            throw new NotSupportedException("Mastertrust does not expose fill-or-kill orders.");

        var current = await ResolveOrder(
            replaceMsg.OldOrderStringId,
            replaceMsg.OriginalTransactionId,
            cancellationToken);
        var orderType = replaceMsg.OrderType ?? current.OrderType.ToOrderType();
        ValidateOrderType(orderType);
        ValidatePrice(orderType, replaceMsg.Price);

        var condition = replaceMsg.Condition as MastertrustOrderCondition;
        var triggerPrice = condition?.TriggerPrice ?? current.TriggerPrice;
        if (orderType == OrderTypes.Conditional && triggerPrice <= 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Mastertrust stop order.");
        }

        var volume = ToQuantity(replaceMsg.Volume, nameof(replaceMsg.Volume));
        var instrumentKey = current.Exchange.ToInstrumentKey(current.Token);
        var instrument = await GetInstrument(instrumentKey, cancellationToken);
        var currentDisclosed = current.Exchange.FromNativeQuantity(
            current.DisclosedQuantity,
            instrument.LotSize);
        var disclosedVolume = ToDisclosedQuantity(
            condition?.DisclosedVolume ?? Positive(currentDisclosed),
            volume);
        var quantity = current.Exchange.ToNativeQuantity(
            volume,
            instrument.LotSize);
        var disclosed = disclosedVolume > 0
            ? current.Exchange.ToNativeQuantity(
                disclosedVolume,
                instrument.LotSize)
            : 0;
        var product = condition?.Product ?? current.Product.ToProduct();

        var orderId = await _restClient.ModifyOrder(
            current.OrderId,
            current.Exchange,
            current.Token,
            current.Side.ToSide(),
            product,
            orderType,
            quantity,
            replaceMsg.Price,
            triggerPrice,
            disclosed,
            replaceMsg.TimeInForce,
            cancellationToken);
        RememberOrder(orderId, replaceMsg.TransactionId);
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
        await _restClient.CancelOrder(current.OrderId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OrderStatusAsync(
        OrderStatusMessage statusMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

        if (!statusMsg.IsSubscribe)
        {
            if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
                _orderStatusSubscriptionId = 0;
            return;
        }

        EnsurePortfolio(statusMsg.PortfolioName);
        var left = statusMsg.Count ?? long.MaxValue;

        foreach (var order in (await _restClient.GetOrders(cancellationToken))
            .Where(order => order != null)
            .OrderBy(GetOrderTime))
        {
            var time = GetOrderTime(order);
            if (statusMsg.From is DateTime from && time < NormalizeUtc(from))
                continue;
            if (statusMsg.To is DateTime to && time > NormalizeUtc(to))
                continue;

            await ProcessOrder(
                order,
                statusMsg.TransactionId,
                true,
                cancellationToken);
            if (--left <= 0)
                break;
        }

        foreach (var trade in await _restClient.GetTrades(cancellationToken))
        {
            await ProcessTrade(
                trade,
                statusMsg.TransactionId,
                true,
                cancellationToken);
        }

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
            await SendSubscriptionResultAsync(statusMsg, cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(
        PortfolioLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

        if (!lookupMsg.IsSubscribe)
        {
            if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
                _portfolioSubscriptionId = 0;
            return;
        }

        EnsurePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            OriginalTransactionId = lookupMsg.TransactionId,
            PortfolioName = _resolvedPortfolioName,
            BoardCode = "NSE",
        }, cancellationToken);
        await SendPortfolioSnapshot(lookupMsg.TransactionId, cancellationToken);
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
            await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
        }
    }

    private async ValueTask SendOrderSnapshot(
        long originalTransactionId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        foreach (var order in await _restClient.GetOrders(cancellationToken))
        {
            await ProcessOrder(
                order,
                originalTransactionId,
                isLookup,
                cancellationToken);
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

    private async ValueTask SendPortfolioSnapshot(
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var funds = await _restClient.GetFunds(cancellationToken);
        await SendOutMessageAsync(new PositionChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            PortfolioName = _resolvedPortfolioName,
            SecurityId = SecurityId.Money,
            ServerTime = CurrentTime,
        }
        .TryAdd(
            PositionChangeTypes.BeginValue,
            funds.CashMargin + funds.Collateral + funds.PayIn - funds.PayOut,
            true)
        .TryAdd(PositionChangeTypes.CurrentValue, funds.Available, true)
        .TryAdd(PositionChangeTypes.BlockedValue, funds.MarginUsed, true),
            cancellationToken);

        foreach (var position in await _restClient.GetPositions(cancellationToken))
        {
            await ProcessPosition(
                position,
                originalTransactionId,
                cancellationToken);
        }

        foreach (var holding in await _restClient.GetHoldings(cancellationToken))
        {
            if (holding == null ||
                holding.Exchange.IsEmpty() ||
                holding.Token.IsEmpty())
                continue;

            await SendOutMessageAsync(new PositionChangeMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = _resolvedPortfolioName,
                SecurityId = holding.Exchange.ToSecurityId(
                    holding.Token,
                    holding.TradingSymbol),
                ServerTime = CurrentTime,
            }
            .TryAdd(
                PositionChangeTypes.CurrentValue,
                holding.Quantity +
                    holding.T0Quantity +
                    holding.T1Quantity +
                    holding.T2Quantity,
                true)
            .TryAdd(
                PositionChangeTypes.BlockedValue,
                holding.UsedQuantity + holding.CollateralQuantity,
                true)
            .TryAdd(
                PositionChangeTypes.AveragePrice,
                Positive(holding.AveragePrice),
                true), cancellationToken);
        }
    }

    private async ValueTask ProcessOrder(
        MastertrustOrder order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (order == null ||
            order.OrderId.IsEmpty() ||
            order.Exchange.IsEmpty() ||
            order.Token.IsEmpty())
            return;

        var instrument = await _restClient.GetInstrument(
            order.Exchange.ToInstrumentKey(order.Token),
            cancellationToken);
        var lotSize = instrument?.LotSize > 0
            ? instrument.LotSize
            : order.LotSize > 0
                ? order.LotSize
                : 1;
        var quantity = order.Exchange.FromNativeQuantity(
            order.Quantity,
            lotSize);
        var filledQuantity = order.Exchange.FromNativeQuantity(
            order.FilledQuantity,
            lotSize);
        var remainingQuantity = order.RemainingQuantity is decimal remaining
            ? order.Exchange.FromNativeQuantity(remaining, lotSize)
            : Math.Max(0, quantity - filledQuantity);
        var disclosedQuantity = order.Exchange.FromNativeQuantity(
            order.DisclosedQuantity,
            lotSize);
        _orderTransactions.TryGetValue(order.OrderId, out var transactionId);
        if (transactionId == 0 &&
            long.TryParse(
                order.UserOrderId,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var userOrderTransactionId))
            transactionId = userOrderTransactionId;
        RememberOrder(order.OrderId, transactionId);

        var state = order.Status.ToOrderState();
        var balance = state is OrderStates.Done or OrderStates.Failed
            ? 0
            : Math.Max(0, remainingQuantity);
        var error = order.RejectionReason.IsEmpty(order.StatusInfo);

        await SendOutMessageAsync(new ExecutionMessage
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
            SecurityId = order.Exchange.ToSecurityId(
                order.Token,
                order.TradingSymbol),
            PortfolioName = _resolvedPortfolioName,
            OrderType = order.OrderType.ToOrderType(),
            Side = order.Side.ToSide(),
            TimeInForce = order.Validity.ToTimeInForce(),
            OrderPrice = order.Price,
            OrderVolume = quantity,
            Balance = balance,
            AveragePrice = Positive(order.AverageTradePrice),
            OrderState = state,
            ServerTime = GetOrderTime(order),
            Condition = CreateCondition(
                order.Product.ToProduct(),
                Positive(order.TriggerPrice),
                order.IsAfterMarket,
                disclosedQuantity,
                Positive(order.MarketProtectionPercentage),
                order.UserOrderId),
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    error.IsEmpty($"Mastertrust order status: {order.Status}."))
                : null,
        }, cancellationToken);
    }

    private async ValueTask ProcessTrade(
        MastertrustTrade trade,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (trade == null ||
            trade.OrderId.IsEmpty() ||
            trade.Exchange.IsEmpty() ||
            trade.Token.IsEmpty())
            return;

        var fillId = trade.TradeId.IsEmpty(
            $"{trade.OrderId}:{trade.TradeTime}:{trade.Price}:{trade.Quantity}");
        if (!_tradeIds.TryAdd(fillId))
            return;

        var instrument = await _restClient.GetInstrument(
            trade.Exchange.ToInstrumentKey(trade.Token),
            cancellationToken);
        var volume = trade.Exchange.FromNativeQuantity(
            trade.Quantity,
            instrument?.LotSize > 0 ? instrument.LotSize : 1);
        if (volume <= 0)
            return;

        var transactionId =
            _orderTransactions.TryGetValue2(trade.OrderId) ?? 0L;
        if (transactionId == 0)
        {
            long.TryParse(
                trade.UserOrderId,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out transactionId);
        }
        RememberOrder(trade.OrderId, transactionId);

        await SendOutMessageAsync(new ExecutionMessage
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
            SecurityId = trade.Exchange.ToSecurityId(
                trade.Token,
                trade.TradingSymbol),
            PortfolioName = _resolvedPortfolioName,
            Side = trade.Side.ToSide(),
            TradePrice = trade.Price,
            TradeVolume = volume,
            ServerTime = trade.TradeTime.ToMastertrustTime() ?? CurrentTime,
        }, cancellationToken);
    }

    private async ValueTask ProcessPosition(
        MastertrustPosition position,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (position == null ||
            position.Exchange.IsEmpty() ||
            position.Token.IsEmpty())
            return;

        var instrument = await _restClient.GetInstrument(
            position.Exchange.ToInstrumentKey(position.Token),
            cancellationToken);
        var quantity = position.Exchange.FromNativeQuantity(
            position.Quantity,
            instrument?.LotSize > 0
                ? instrument.LotSize
                : position.Multiplier > 0
                    ? position.Multiplier
                    : 1);
        await SendOutMessageAsync(new PositionChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            PortfolioName = _resolvedPortfolioName,
            SecurityId = position.Exchange.ToSecurityId(
                position.Token,
                position.TradingSymbol),
            ServerTime = CurrentTime,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
        .TryAdd(
            PositionChangeTypes.AveragePrice,
            Positive(position.AveragePrice),
            true)
        .TryAdd(
            PositionChangeTypes.RealizedPnL,
            position.RealizedPnL,
            true), cancellationToken);
    }

    private async ValueTask OnSocketUpdate(
        MastertrustSocketUpdate update,
        CancellationToken cancellationToken)
    {
        if (update == null)
            return;
        if (_resolvedPortfolioName.IsEmpty() && !update.ClientId.IsEmpty())
            _resolvedPortfolioName = update.ClientId;

        if (update.Trade != null)
        {
            var transactionId =
                _orderTransactions.TryGetValue2(update.Trade.OrderId) ?? 0;
            if (transactionId != 0 || _orderStatusSubscriptionId != 0)
            {
                await ProcessTrade(
                    update.Trade,
                    _orderStatusSubscriptionId,
                    false,
                    cancellationToken);
            }
        }
        if (update.Order != null)
        {
            var transactionId =
                _orderTransactions.TryGetValue2(update.Order.OrderId) ?? 0;
            if (transactionId != 0 || _orderStatusSubscriptionId != 0)
            {
                await ProcessOrder(
                    update.Order,
                    _orderStatusSubscriptionId,
                    false,
                    cancellationToken);
            }
        }
        if (update.Position != null && _portfolioSubscriptionId != 0)
        {
            await ProcessPosition(
                update.Position,
                _portfolioSubscriptionId,
                cancellationToken);
        }
        _lastOrderRefresh = CurrentTime;
    }

    private async Task<MastertrustOrder> ResolveOrder(
        string orderId,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (orderId.IsEmpty())
            _transactionOrders.TryGetValue(originalTransactionId, out orderId);
        if (orderId.IsEmpty())
        {
            throw new InvalidOperationException(
                LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));
        }

        foreach (var order in await _restClient.GetOrders(cancellationToken))
        {
            if (order == null || order.OrderId.IsEmpty())
                continue;

            _orderTransactions.TryGetValue(order.OrderId, out var transactionId);
            if (transactionId == 0 &&
                long.TryParse(
                    order.UserOrderId,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed))
                transactionId = parsed;
            RememberOrder(order.OrderId, transactionId);
            if (order.OrderId.EqualsIgnoreCase(orderId) ||
                transactionId != 0 &&
                transactionId == originalTransactionId)
                return order;
        }

        throw new InvalidOperationException(
            $"Mastertrust order '{orderId}' was not found in the current order book.");
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
                "Mastertrust supports market, limit, stop-limit, and stop-market orders.");
        }
    }

    private static void ValidatePrice(OrderTypes orderType, decimal price)
    {
        if (orderType == OrderTypes.Limit && price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                price,
                "A positive limit price is required.");
        }
        if (orderType == OrderTypes.Conditional && price < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                price,
                "A stop-order limit price cannot be negative.");
        }
    }

    private static long ToQuantity(decimal value, string parameterName)
    {
        if (value <= 0 ||
            value != decimal.Truncate(value) ||
            value > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Mastertrust quantities must be positive whole numbers within Int64 range.");
        }
        return decimal.ToInt64(value);
    }

    private static long ToDisclosedQuantity(decimal? value, long quantity)
    {
        if (value is null or 0)
            return 0;

        var disclosed = ToQuantity(
            value.Value,
            nameof(MastertrustOrderCondition.DisclosedVolume));
        if (disclosed > quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MastertrustOrderCondition.DisclosedVolume),
                value,
                "Disclosed volume cannot exceed order volume.");
        }
        return disclosed;
    }

    private static decimal ValidateMarketProtection(decimal? value)
    {
        var result = value ?? 0;
        if (result is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MastertrustOrderCondition.MarketProtectionPercentage),
                value,
                "Market protection percentage must be between 0 and 100.");
        }
        return result;
    }

    private static MastertrustOrderCondition CreateCondition(
        MastertrustProducts product,
        decimal? triggerPrice,
        bool isAfterMarket,
        decimal disclosedVolume,
        decimal? marketProtectionPercentage,
        string userOrderId)
        => new()
        {
            Product = product,
            TriggerPrice = triggerPrice,
            IsAfterMarket = isAfterMarket,
            DisclosedVolume = disclosedVolume > 0 ? disclosedVolume : null,
            MarketProtectionPercentage = marketProtectionPercentage,
            UserOrderId = userOrderId,
        };

    private DateTime GetOrderTime(MastertrustOrder order)
        => order.ExchangeTime.ToMastertrustTime() ??
            order.OrderEntryTime.ToMastertrustTime() ??
            CurrentTime;

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
