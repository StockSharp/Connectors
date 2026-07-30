namespace StockSharp.Tradejini;

public partial class TradejiniMessageAdapter
{
    private readonly SynchronizedDictionary<string, long>
        _orderTransactions =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<long, string>
        _transactionOrders = [];
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
        ValidateOrderPrices(
            orderType,
            regMsg.Price,
            (regMsg.Condition as TradejiniOrderCondition)
                ?.TriggerPrice ?? 0);

        var volume = ValidateQuantity(
            regMsg.Volume,
            nameof(regMsg.Volume));
        var condition = regMsg.Condition as TradejiniOrderCondition;
        var disclosed = ValidateDisclosedQuantity(
            condition?.DisclosedVolume,
            volume);
        var triggerPrice = condition?.TriggerPrice ?? 0;
        var validity = regMsg.TimeInForce.ToValidity(
            condition?.Validity);
        var marketProtection = ValidateMarketProtection(
            condition?.MarketProtection);
        ValidateMarketProtectionOrderType(
            orderType,
            marketProtection);
        var remarks = ValidateRemarks(condition?.Remarks);
        var product = condition?.Product ?? DefaultProduct;

        var orderId = await _restClient.PlaceOrder(
            regMsg.SecurityId.ToSymbolId(),
            volume,
            regMsg.Side,
            orderType,
            product,
            regMsg.Price,
            triggerPrice,
            validity,
            disclosed,
            condition?.IsAfterMarket == true,
            marketProtection,
            remarks,
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
            TimeInForce = validity.ToTimeInForce(),
            OrderPrice = regMsg.Price,
            OrderVolume = volume,
            Balance = volume,
            OrderState = OrderStates.Pending,
            ServerTime = CurrentTime,
            Condition = CreateCondition(
                product,
                Positive(triggerPrice),
                validity,
                condition?.IsAfterMarket == true,
                disclosed,
                Positive(marketProtection),
                remarks),
        }, cancellationToken);
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
        var orderType =
            replaceMsg.OrderType ?? current.Type.ToOrderType();
        var condition =
            replaceMsg.Condition as TradejiniOrderCondition;
        var triggerPrice =
            condition?.TriggerPrice ?? current.TriggerPrice;
        ValidateOrderType(orderType);
        ValidateOrderPrices(
            orderType,
            replaceMsg.Price,
            triggerPrice);

        var volume = ValidateQuantity(
            replaceMsg.Volume,
            nameof(replaceMsg.Volume));
        if (volume < current.FilledQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replaceMsg.Volume),
                replaceMsg.Volume,
                "Modified Tradejini quantity cannot be below the already filled quantity.");
        }
        var disclosed = ValidateDisclosedQuantity(
            condition?.DisclosedVolume ??
                Positive(current.DisclosedQuantity),
            volume);
        var validity = replaceMsg.TimeInForce.ToValidity(
            condition?.Validity ?? current.Validity.ToValidity());
        var marketProtection = ValidateMarketProtection(
            condition?.MarketProtection ??
                Positive(current.MarketProtection));
        ValidateMarketProtectionOrderType(
            orderType,
            marketProtection);

        var orderId = await _restClient.ModifyOrder(
            current.SymbolId,
            current.OrderId,
            volume,
            replaceMsg.Side,
            orderType,
            replaceMsg.Price,
            triggerPrice,
            validity,
            disclosed,
            marketProtection,
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
        await _restClient.CancelOrder(
            current.OrderId,
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
        var left = statusMsg.Count ?? long.MaxValue;

        foreach (var order in
            (await _restClient.GetOrders(cancellationToken))
                .Where(order => order != null)
                .OrderBy(GetOrderTime))
        {
            var time = GetOrderTime(order);
            if (statusMsg.From is DateTime from &&
                time < NormalizeUtc(from))
                continue;
            if (statusMsg.To is DateTime to &&
                time > NormalizeUtc(to))
                continue;

            await ProcessOrder(
                order,
                statusMsg.TransactionId,
                true,
                cancellationToken);
            if (--left <= 0)
                break;
        }

        foreach (var trade in
            await _restClient.GetTrades(cancellationToken))
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
            await SendSubscriptionResultAsync(
                statusMsg,
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
        await SendOutMessageAsync(new PortfolioMessage
        {
            OriginalTransactionId = lookupMsg.TransactionId,
            PortfolioName = _resolvedPortfolioName,
            BoardCode = "NSE",
        }, cancellationToken);
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

    private async ValueTask SendOrderSnapshot(
        long originalTransactionId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        foreach (var order in
            await _restClient.GetOrders(cancellationToken))
        {
            await ProcessOrder(
                order,
                originalTransactionId,
                isLookup,
                cancellationToken);
        }

        foreach (var trade in
            await _restClient.GetTrades(cancellationToken))
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
        if (funds.Length > 0)
        {
            await SendOutMessageAsync(new PositionChangeMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = _resolvedPortfolioName,
                SecurityId = SecurityId.Money,
                ServerTime = CurrentTime,
            }
            .TryAdd(
                PositionChangeTypes.BeginValue,
                funds.Sum(fund => fund.TotalCredits),
                true)
            .TryAdd(
                PositionChangeTypes.CurrentValue,
                funds.Sum(fund => fund.AvailableMargin),
                true)
            .TryAdd(
                PositionChangeTypes.BlockedValue,
                funds.Sum(fund => fund.MarginUsed),
                true)
            .TryAdd(
                PositionChangeTypes.RealizedPnL,
                funds.Sum(fund => fund.RealizedPnL),
                true)
            .TryAdd(
                PositionChangeTypes.UnrealizedPnL,
                funds.Sum(fund => fund.UnrealizedPnL),
                true), cancellationToken);
        }

        foreach (var position in
            await _restClient.GetPositions(cancellationToken))
        {
            if (position?.SymbolId.IsEmpty() != false)
                continue;
            SecurityId securityId;
            try
            {
                securityId = position.SymbolId
                    .ToTradejiniSecurityId();
            }
            catch (FormatException)
            {
                continue;
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            await SendOutMessageAsync(new PositionChangeMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = _resolvedPortfolioName,
                SecurityId = securityId,
                ServerTime = CurrentTime,
            }
            .TryAdd(
                PositionChangeTypes.CurrentValue,
                position.NetQuantity,
                true)
            .TryAdd(
                PositionChangeTypes.AveragePrice,
                Positive(position.NetAveragePrice),
                true)
            .TryAdd(
                PositionChangeTypes.RealizedPnL,
                position.RealizedPnL,
                true)
            .TryAdd(
                PositionChangeTypes.UnrealizedPnL,
                position.UnrealizedPnL,
                true), cancellationToken);
        }

        foreach (var holding in
            await _restClient.GetHoldings(cancellationToken))
        {
            if (holding?.SymbolId.IsEmpty() != false)
                continue;
            SecurityId securityId;
            try
            {
                securityId = holding.SymbolId
                    .ToTradejiniSecurityId();
            }
            catch (FormatException)
            {
                continue;
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            await SendOutMessageAsync(new PositionChangeMessage
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
                PositionChangeTypes.BlockedValue,
                Math.Max(
                    0,
                    holding.Quantity - holding.SaleableQuantity),
                true)
            .TryAdd(
                PositionChangeTypes.AveragePrice,
                Positive(holding.AveragePrice),
                true), cancellationToken);
        }
    }

    private async ValueTask ProcessOrder(
        TradejiniOrder order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (order == null ||
            order.OrderId.IsEmpty() ||
            order.SymbolId.IsEmpty())
            return;

        _orderTransactions.TryGetValue(
            order.OrderId,
            out var transactionId);
        if (transactionId == 0 &&
            long.TryParse(
                order.Remarks,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var remarksTransactionId))
            transactionId = remarksTransactionId;
        RememberOrder(order.OrderId, transactionId);

        var state = order.Status.ToOrderState();
        var balance = order.PendingQuantity;
        if (balance <= 0 &&
            state is not OrderStates.Done and
                not OrderStates.Failed)
        {
            balance = Math.Max(
                0,
                order.Quantity - order.FilledQuantity);
        }
        if (state is OrderStates.Done or OrderStates.Failed)
            balance = 0;
        var validity = order.Validity.ToValidity();
        var error = order.Reason.IsEmpty(order.Message);

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
            SecurityId = order.SymbolId.ToTradejiniSecurityId(),
            PortfolioName = _resolvedPortfolioName,
            OrderType = order.Type.ToOrderType(),
            Side = order.Side.ToSide(),
            TimeInForce = validity.ToTimeInForce(),
            OrderPrice = order.LimitPrice,
            OrderVolume = order.Quantity,
            Balance = balance,
            AveragePrice = Positive(order.AveragePrice),
            OrderState = state,
            ServerTime = GetOrderTime(order),
            Condition = CreateCondition(
                order.Product.ToProduct(),
                Positive(order.TriggerPrice),
                validity,
                order.IsAfterMarket,
                order.DisclosedQuantity,
                Positive(order.MarketProtection),
                order.Remarks),
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    error.IsEmpty(
                        $"Tradejini order status: {order.Status}."))
                : null,
        }, cancellationToken);
    }

    private async ValueTask ProcessTrade(
        TradejiniTrade trade,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (trade == null ||
            trade.OrderId.IsEmpty() ||
            trade.SymbolId.IsEmpty() ||
            trade.Quantity <= 0)
            return;

        var fillId = trade.FillId.IsEmpty(
            $"{trade.OrderId}:{trade.Time}:{trade.Price}:{trade.Quantity}");
        if (!_tradeIds.TryAdd(fillId))
            return;

        var transactionId =
            _orderTransactions.TryGetValue2(trade.OrderId) ?? 0;
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
            SecurityId = trade.SymbolId.ToTradejiniSecurityId(),
            PortfolioName = _resolvedPortfolioName,
            Side = trade.Side.ToSide(),
            TradePrice = trade.Price,
            TradeVolume = trade.Quantity,
            ServerTime = trade.Time.ToTradejiniTime(CurrentTime) ??
                CurrentTime,
        }, cancellationToken);
    }

    private async Task<TradejiniOrder> ResolveOrder(
        string orderId,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (orderId.IsEmpty())
        {
            _transactionOrders.TryGetValue(
                originalTransactionId,
                out orderId);
        }
        if (orderId.IsEmpty())
        {
            throw new InvalidOperationException(
                LocalizedStrings.OrderNoExchangeId.Put(
                    originalTransactionId));
        }

        foreach (var order in
            await _restClient.GetOrders(cancellationToken))
        {
            if (order?.OrderId.IsEmpty() != false)
                continue;

            _orderTransactions.TryGetValue(
                order.OrderId,
                out var transactionId);
            if (transactionId == 0)
            {
                long.TryParse(
                    order.Remarks,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out transactionId);
            }
            RememberOrder(order.OrderId, transactionId);
            if (order.OrderId.EqualsIgnoreCase(orderId) ||
                transactionId != 0 &&
                transactionId == originalTransactionId)
                return order;
        }

        throw new InvalidOperationException(
            $"Tradejini order '{orderId}' was not found in the current order book.");
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
            throw new InvalidOperationException(
                LocalizedStrings.AccountNotFound);
    }

    internal static decimal ValidateQuantity(
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
                "Tradejini quantities must be positive whole numbers within Int64 range.");
        }
        return value;
    }

    private static decimal ValidateDisclosedQuantity(
        decimal? value,
        decimal quantity)
    {
        if (value is null or 0)
            return 0;
        var disclosed = ValidateQuantity(
            value.Value,
            nameof(TradejiniOrderCondition.DisclosedVolume));
        if (disclosed > quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TradejiniOrderCondition.DisclosedVolume),
                value,
                "Disclosed volume cannot exceed order volume.");
        }
        return disclosed;
    }

    private static decimal ValidateMarketProtection(decimal? value)
    {
        if (value is null or 0)
            return 0;
        if (value < 0 || value > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TradejiniOrderCondition.MarketProtection),
                value,
                "Market protection must be between zero and 100 percent.");
        }
        return value.Value;
    }

    private static void ValidateMarketProtectionOrderType(
        OrderTypes orderType,
        decimal marketProtection)
    {
        if (marketProtection > 0 && orderType != OrderTypes.Market)
        {
            throw new InvalidOperationException(
                "Tradejini market protection is available only for market orders.");
        }
    }

    private static string ValidateRemarks(string remarks)
    {
        if (remarks?.Length > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TradejiniOrderCondition.Remarks),
                remarks.Length,
                "Tradejini remarks cannot exceed ten characters.");
        }
        return remarks;
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
                "Tradejini supports market, limit, stop-limit, and stop-market orders.");
        }
    }

    private static void ValidateOrderPrices(
        OrderTypes orderType,
        decimal limitPrice,
        decimal triggerPrice)
    {
        if (orderType == OrderTypes.Limit && limitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limitPrice),
                limitPrice,
                "A positive Tradejini limit price is required.");
        }
        if (orderType == OrderTypes.Conditional &&
            limitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limitPrice),
                limitPrice,
                "A stop-order limit price cannot be negative.");
        }
        if (orderType == OrderTypes.Conditional &&
            triggerPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(triggerPrice),
                triggerPrice,
                "A positive Tradejini trigger price is required.");
        }
    }

    private static TradejiniOrderCondition CreateCondition(
        TradejiniProducts product,
        decimal? triggerPrice,
        TradejiniValidities validity,
        bool isAfterMarket,
        decimal disclosedVolume,
        decimal? marketProtection,
        string remarks)
        => new()
        {
            Product = product,
            TriggerPrice = triggerPrice,
            Validity = validity,
            IsAfterMarket = isAfterMarket,
            DisclosedVolume = disclosedVolume > 0
                ? disclosedVolume
                : null,
            MarketProtection = marketProtection,
            Remarks = remarks,
        };

    private DateTime GetOrderTime(TradejiniOrder order)
        => order.UpdateTime.ToTradejiniTime(CurrentTime) ??
            order.OrderTime.ToTradejiniTime(CurrentTime) ??
            CurrentTime;

    private static decimal? Positive(decimal value)
        => value > 0 ? value : null;

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
