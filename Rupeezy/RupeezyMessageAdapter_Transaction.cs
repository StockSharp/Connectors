namespace StockSharp.Rupeezy;

public partial class RupeezyMessageAdapter
{
    private readonly SynchronizedDictionary<string, long> _orderTransactions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
    private readonly SynchronizedDictionary<string, decimal> _orderFills =
        new(StringComparer.OrdinalIgnoreCase);
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
            throw new NotSupportedException("Rupeezy does not expose fill-or-kill orders.");
        if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regMsg.Price),
                regMsg.Price,
                "A positive limit price is required.");
        }
        if (orderType == OrderTypes.Conditional && regMsg.Price < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regMsg.Price),
                regMsg.Price,
                "A stop-order limit price cannot be negative.");
        }

        var condition = regMsg.Condition as RupeezyOrderCondition;
        var triggerPrice = condition?.TriggerPrice ?? 0;
        if (orderType == OrderTypes.Conditional && triggerPrice <= 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Rupeezy stop order.");
        }

        var volume = ToQuantity(regMsg.Volume, nameof(regMsg.Volume));
        var disclosedVolume = ToDisclosedQuantity(
            condition?.DisclosedVolume,
            volume);
        var instrumentKey = regMsg.SecurityId.ToInstrumentKey();
        var (exchange, token) = instrumentKey.ParseInstrumentKey();
        var instrument = await GetInstrument(instrumentKey, cancellationToken);
        var quantity = exchange.ToNativeQuantity(volume, instrument.LotSize);
        var disclosed = disclosedVolume > 0
            ? exchange.ToNativeQuantity(disclosedVolume, instrument.LotSize)
            : 0;
        var product = condition?.Product ?? DefaultProduct;
        var identifier = condition?.OrderIdentifier.IsEmpty(
            regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));
        if (identifier?.Length > 50)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RupeezyOrderCondition.OrderIdentifier),
                identifier.Length,
                "Rupeezy order identifiers cannot exceed 50 characters.");
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
            regMsg.TimeInForce,
            condition?.IsAfterMarket == true,
            identifier,
            cancellationToken);
        RememberOrder(orderId, regMsg.TransactionId);
        _orderFills[orderId] = 0;

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
                identifier),
        }, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(replaceMsg.PortfolioName);
        if (replaceMsg.TimeInForce == TimeInForce.MatchOrCancel)
            throw new NotSupportedException("Rupeezy does not expose fill-or-kill orders.");

        var current = await ResolveOrder(
            replaceMsg.OldOrderStringId,
            replaceMsg.OriginalTransactionId,
            cancellationToken);
        var orderType = replaceMsg.OrderType ?? current.Variety.ToOrderType();
        ValidateOrderType(orderType);
        if (orderType == OrderTypes.Limit && replaceMsg.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replaceMsg.Price),
                replaceMsg.Price,
                "A positive limit price is required.");
        }
        if (orderType == OrderTypes.Conditional && replaceMsg.Price < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replaceMsg.Price),
                replaceMsg.Price,
                "A stop-order limit price cannot be negative.");
        }

        var condition = replaceMsg.Condition as RupeezyOrderCondition;
        var triggerPrice = condition?.TriggerPrice ?? current.TriggerPrice;
        if (orderType == OrderTypes.Conditional && triggerPrice <= 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Rupeezy stop order.");
        }

        var volume = ToQuantity(replaceMsg.Volume, nameof(replaceMsg.Volume));
        var instrumentKey = current.Exchange.ToInstrumentKey(current.Token);
        var instrument = await GetInstrument(instrumentKey, cancellationToken);
        var currentDisclosedVolume = current.Exchange.FromNativeQuantity(
            current.DisclosedQuantity,
            instrument.LotSize);
        var disclosedVolume = ToDisclosedQuantity(
            condition?.DisclosedVolume ?? Positive(currentDisclosedVolume),
            volume);
        var quantity = current.Exchange.ToNativeQuantity(
            volume,
            instrument.LotSize);
        var disclosed = disclosedVolume > 0
            ? current.Exchange.ToNativeQuantity(
                disclosedVolume,
                instrument.LotSize)
            : 0;
        var emittedFills = _orderFills.TryGetValue2(current.OrderId) ?? 0m;
        var nativeEmittedFills = emittedFills > 0
            ? current.Exchange.ToNativeQuantity(
                emittedFills,
                instrument.LotSize)
            : 0;
        var traded = Math.Max(current.TradedQuantity, nativeEmittedFills);
        var orderId = await _restClient.ModifyOrder(
            current.OrderId,
            orderType,
            quantity,
            traded,
            replaceMsg.Price,
            triggerPrice,
            disclosed,
            replaceMsg.TimeInForce,
            cancellationToken);
        RememberOrder(orderId, replaceMsg.TransactionId);
        _orderFills[orderId] = emittedFills;
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
        if (funds.Length > 0)
        {
            var begin = funds.Sum(fund => fund.Deposit + fund.Collateral);
            var available = funds.Sum(fund => fund.Available);
            var blocked = Math.Abs(funds.Sum(fund => fund.Utilization));
            await SendOutMessageAsync(new PositionChangeMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = _resolvedPortfolioName,
                SecurityId = SecurityId.Money,
                ServerTime = CurrentTime,
            }
            .TryAdd(PositionChangeTypes.BeginValue, begin, true)
            .TryAdd(PositionChangeTypes.CurrentValue, available, true)
            .TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
            .TryAdd(
                PositionChangeTypes.RealizedPnL,
                funds.Sum(fund => fund.RealizedPnL),
                true)
            .TryAdd(
                PositionChangeTypes.UnrealizedPnL,
                funds.Sum(fund => fund.UnrealizedPnL),
                true), cancellationToken);
        }

        foreach (var position in await _restClient.GetPositions(cancellationToken))
        {
            if (position == null ||
                position.Exchange.IsEmpty() ||
                position.Token.IsEmpty())
                continue;

            var quantity = position.Exchange.FromPositionQuantity(
                position.Quantity,
                position.LotSize,
                position.Multiplier);
            await SendOutMessageAsync(new PositionChangeMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = _resolvedPortfolioName,
                SecurityId = position.Exchange.ToSecurityId(
                    position.Token,
                    position.Symbol),
                ServerTime = CurrentTime,
            }
            .TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
            .TryAdd(
                PositionChangeTypes.AveragePrice,
                Positive(position.AveragePrice),
                true)
            .TryAdd(
                PositionChangeTypes.RealizedPnL,
                quantity == 0
                    ? position.SellValue - position.BuyValue
                    : null,
                true), cancellationToken);
        }

        foreach (var holding in await _restClient.GetHoldings(cancellationToken))
        {
            if (holding == null)
                continue;
            var security = holding.Nse != null && !holding.Nse.Token.IsEmpty()
                ? holding.Nse
                : holding.Bse;
            if (security == null ||
                security.Exchange.IsEmpty() ||
                security.Token.IsEmpty())
                continue;

            await SendOutMessageAsync(new PositionChangeMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = _resolvedPortfolioName,
                SecurityId = security.Exchange.ToSecurityId(
                    security.Token,
                    security.Symbol),
                ServerTime = CurrentTime,
            }
            .TryAdd(
                PositionChangeTypes.CurrentValue,
                holding.Quantity + holding.T1Quantity,
                true)
            .TryAdd(
                PositionChangeTypes.BlockedValue,
                holding.CollateralQuantity,
                true)
            .TryAdd(
                PositionChangeTypes.AveragePrice,
                Positive(holding.AveragePrice),
                true), cancellationToken);
        }
    }

    private async ValueTask ProcessOrder(
        RupeezyOrder order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (order == null ||
            order.OrderId.IsEmpty() ||
            order.Exchange.IsEmpty() ||
            order.Token.IsEmpty())
            return;

        var instrument = await GetInstrument(
            order.Exchange.ToInstrumentKey(order.Token),
            cancellationToken);
        var quantity = order.Exchange.FromNativeQuantity(
            order.Quantity,
            instrument.LotSize);
        var pendingQuantity = order.Exchange.FromNativeQuantity(
            order.PendingQuantity,
            instrument.LotSize);
        var tradedQuantity = order.Exchange.FromNativeQuantity(
            order.TradedQuantity,
            instrument.LotSize);
        var disclosedQuantity = order.Exchange.FromNativeQuantity(
            order.DisclosedQuantity,
            instrument.LotSize);
        _orderTransactions.TryGetValue(order.OrderId, out var transactionId);
        if (transactionId == 0 &&
            long.TryParse(
                order.OrderIdentifier,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var identifierId))
            transactionId = identifierId;
        RememberOrder(order.OrderId, transactionId);

        var state = order.Status.ToOrderState();
        var balance = pendingQuantity;
        if (balance <= 0 && state is not OrderStates.Done and not OrderStates.Failed)
            balance = Math.Max(0, quantity - tradedQuantity);
        if (state is OrderStates.Done or OrderStates.Failed)
            balance = 0;
        var error = order.ErrorReason.IsEmpty(order.StatusMessage);

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
            SecurityId = order.Exchange.ToSecurityId(order.Token, order.Symbol),
            PortfolioName = _resolvedPortfolioName,
            OrderType = order.Variety.ToOrderType(),
            Side = order.TransactionType.ToSide(),
            TimeInForce = order.Validity.ToTimeInForce(),
            OrderPrice = order.Price,
            OrderVolume = quantity,
            Balance = balance,
            AveragePrice = Positive(order.TradedPrice),
            OrderState = state,
            ServerTime = GetOrderTime(order),
            Condition = CreateCondition(
                order.Product.ToProduct(),
                Positive(order.TriggerPrice),
                order.IsAfterMarket,
                disclosedQuantity,
                order.OrderIdentifier),
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    error.IsEmpty($"Rupeezy order status: {order.Status}."))
                : null,
        }, cancellationToken);
    }

    private async ValueTask ProcessTrade(
        RupeezyTrade trade,
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
            $"{trade.OrderId}:{trade.TradedAt}:{trade.Price}:{trade.Quantity}:{trade.CumulativeQuantity}");
        if (!_tradeIds.TryAdd(fillId))
            return;

        var instrument = await GetInstrument(
            trade.Exchange.ToInstrumentKey(trade.Token),
            cancellationToken);
        var previousFills = _orderFills.TryGetValue2(trade.OrderId) ?? 0m;
        var cumulative = trade.CumulativeQuantity > 0
            ? trade.Exchange.FromNativeQuantity(
                trade.CumulativeQuantity,
                instrument.LotSize)
            : 0;
        var volume = trade.Quantity > 0
            ? trade.Exchange.FromNativeQuantity(
                trade.Quantity,
                instrument.LotSize)
            : Math.Max(0, cumulative - previousFills);
        if (volume <= 0)
            return;
        _orderFills[trade.OrderId] = cumulative > 0
            ? Math.Max(previousFills, cumulative)
            : previousFills + volume;
        var transactionId = _orderTransactions.TryGetValue2(trade.OrderId) ?? 0L;
        if (transactionId == 0)
        {
            long.TryParse(
                trade.OrderIdentifier,
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
            SecurityId = trade.Exchange.ToSecurityId(trade.Token, trade.Symbol),
            PortfolioName = _resolvedPortfolioName,
            Side = trade.TransactionType.ToSide(),
            TradePrice = trade.Price,
            TradeVolume = volume,
            ServerTime = trade.TradedAt.ToRupeezyTime() ?? CurrentTime,
        }, cancellationToken);
    }

    private async ValueTask OnSocketUpdate(
        RupeezySocketUpdate update,
        CancellationToken cancellationToken)
    {
        if (update == null)
            return;
        if (_resolvedPortfolioName.IsEmpty() && !update.ClientCode.IsEmpty())
            _resolvedPortfolioName = update.ClientCode;
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
        _lastOrderRefresh = CurrentTime;
    }

    private async Task<RupeezyOrder> ResolveOrder(
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
                    order.OrderIdentifier,
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
            $"Rupeezy order '{orderId}' was not found in the current order book.");
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
                "Rupeezy supports market, limit, stop-limit, and stop-market orders.");
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
                "Rupeezy quantities must be positive whole numbers within Int64 range.");
        }
        return decimal.ToInt64(value);
    }

    private static long ToDisclosedQuantity(decimal? value, long quantity)
    {
        if (value is null or 0)
            return 0;

        var disclosed = ToQuantity(
            value.Value,
            nameof(RupeezyOrderCondition.DisclosedVolume));
        if (disclosed > quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RupeezyOrderCondition.DisclosedVolume),
                value,
                "Disclosed volume cannot exceed order volume.");
        }
        return disclosed;
    }

    private static RupeezyOrderCondition CreateCondition(
        RupeezyProducts product,
        decimal? triggerPrice,
        bool isAfterMarket,
        decimal disclosedVolume,
        string orderIdentifier)
        => new()
        {
            Product = product,
            TriggerPrice = triggerPrice,
            IsAfterMarket = isAfterMarket,
            DisclosedVolume = disclosedVolume > 0 ? disclosedVolume : null,
            OrderIdentifier = orderIdentifier,
        };

    private DateTime GetOrderTime(RupeezyOrder order)
        => order.UpdatedAt.ToRupeezyTime() ??
            order.ExchangeCreatedAt.ToRupeezyTime() ??
            order.CreatedAt.ToRupeezyTime() ??
            CurrentTime;

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
