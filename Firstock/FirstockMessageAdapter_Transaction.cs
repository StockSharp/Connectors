namespace StockSharp.Firstock;

public partial class FirstockMessageAdapter
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
            throw new NotSupportedException("Firstock does not expose fill-or-kill orders.");

        var condition = regMsg.Condition as FirstockOrderCondition;
        var triggerPrice = condition?.TriggerPrice;
        if (orderType == OrderTypes.Conditional && triggerPrice is not > 0)
            throw new InvalidOperationException(
                "A positive trigger price is required for a Firstock stop order.");
        if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(regMsg.Price), regMsg.Price, "A positive limit price is required.");

        var quantity = ToQuantity(regMsg.Volume, nameof(regMsg.Volume));
        var instrument = await GetInstrument(
            regMsg.SecurityId.ToInstrumentKey(), cancellationToken);
        var product = condition?.Product ?? DefaultProduct;
        var priceType = orderType.ToPriceType(regMsg.Price);
        var marketProtection = RequiresMarketProtection(priceType)
            ? condition?.MarketProtection ?? MarketProtection
            : (decimal?)null;
        if (marketProtection is <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(FirstockOrderCondition.MarketProtection), marketProtection,
                "Firstock requires positive market protection for market and stop-market orders.");

        var remarks = (condition?.Remarks).IsEmpty(
            regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));
        var orderId = await _restClient.PlaceOrder(new()
        {
            Side = regMsg.Side.ToNative(),
            Product = product.ToNative(),
            Exchange = instrument.Exchange,
            TradingSymbol = instrument.TradingSymbol,
            Quantity = quantity.ToString(CultureInfo.InvariantCulture),
            PriceType = priceType,
            Price = FormatPrice(orderType == OrderTypes.Market ? 0m : regMsg.Price),
            TriggerPrice = FormatPrice(triggerPrice ?? 0m),
            Retention = regMsg.TimeInForce.ToRetention(),
            MarketProtection = FormatOptional(marketProtection),
            Remarks = remarks,
        }, condition?.IsAfterMarket == true, cancellationToken);

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
                triggerPrice,
                marketProtection,
                condition?.IsAfterMarket == true,
                remarks),
        }, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(replaceMsg.PortfolioName);
        if (replaceMsg.TimeInForce == TimeInForce.MatchOrCancel)
            throw new NotSupportedException("Firstock does not expose fill-or-kill orders.");

        var current = await ResolveOrder(
            replaceMsg.OldOrderStringId,
            replaceMsg.OriginalTransactionId,
            cancellationToken);
        var orderType = replaceMsg.OrderType ?? current.ToOrderType();
        ValidateOrderType(orderType);
        var condition = replaceMsg.Condition as FirstockOrderCondition;
        var triggerPrice = condition?.TriggerPrice ?? Positive(current.TriggerPrice.ToDecimal());
        if (orderType == OrderTypes.Conditional && triggerPrice is not > 0)
            throw new InvalidOperationException(
                "A positive trigger price is required for a Firstock stop order.");
        if (orderType == OrderTypes.Limit && replaceMsg.Price <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(replaceMsg.Price), replaceMsg.Price, "A positive limit price is required.");

        var quantity = ToQuantity(replaceMsg.Volume, nameof(replaceMsg.Volume));
        var instrument = await ResolveInstrument(
            replaceMsg.SecurityId, current, cancellationToken);
        var product = condition?.Product ?? current.Product.ToProduct();
        var priceType = orderType.ToPriceType(replaceMsg.Price);
        var marketProtection = RequiresMarketProtection(priceType)
            ? condition?.MarketProtection ?? MarketProtection
            : (decimal?)null;
        if (marketProtection is <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(FirstockOrderCondition.MarketProtection), marketProtection,
                "Firstock requires positive market protection for market and stop-market orders.");

        await _restClient.ModifyOrder(new()
        {
            OrderId = current.OrderId,
            Exchange = instrument.Exchange,
            TradingSymbol = instrument.TradingSymbol,
            Quantity = quantity.ToString(CultureInfo.InvariantCulture),
            Product = product.ToNative(),
            PriceType = priceType,
            Price = FormatPrice(orderType == OrderTypes.Market ? 0m : replaceMsg.Price),
            TriggerPrice = FormatPrice(triggerPrice ?? 0m),
            Retention = replaceMsg.TimeInForce.ToRetention(),
            MarketProtection = FormatOptional(marketProtection),
        }, condition?.IsAfterMarket == true ||
            current.Status?.StartsWith("AMO", StringComparison.OrdinalIgnoreCase) == true,
            cancellationToken);
        RememberOrder(current.OrderId, replaceMsg.TransactionId);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(cancelMsg.PortfolioName);
        var orderId = cancelMsg.OrderStringId;
        if (orderId.IsEmpty() &&
            _transactionOrders.TryGetValue(cancelMsg.OriginalTransactionId, out var remembered))
            orderId = remembered;
        if (orderId.IsEmpty())
            throw new InvalidOperationException(
                LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));
        await _restClient.CancelOrder(orderId, cancellationToken);
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
            if (statusMsg.From is DateTime from && time < from.ToUniversalTime())
                continue;
            if (statusMsg.To is DateTime to && time > to.ToUniversalTime())
                continue;
            await ProcessOrder(order, statusMsg.TransactionId, true, cancellationToken);
            if (--left <= 0)
                break;
        }

        foreach (var trade in await _restClient.GetTrades(cancellationToken))
            await ProcessTrade(trade, statusMsg.TransactionId, cancellationToken);

        if (statusMsg.IsHistoryOnly())
            await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
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
            await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
        else
        {
            _portfolioSubscriptionId = lookupMsg.TransactionId;
            await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
        }
    }

    private async ValueTask SendPortfolioSnapshot(
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var limits = await _restClient.GetLimits(cancellationToken);
        var cash = limits.Cash.ToDecimal();
        var available = limits.AvailableMargin.ToDecimal();
        await SendOutMessageAsync(new PositionChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            PortfolioName = _resolvedPortfolioName,
            SecurityId = SecurityId.Money,
            ServerTime = CurrentTime,
        }
        .TryAdd(PositionChangeTypes.BeginValue, cash, true)
        .TryAdd(PositionChangeTypes.CurrentValue, available != 0 ? available : cash, true)
        .TryAdd(PositionChangeTypes.BlockedValue, limits.MarginUsed.ToDecimal(), true), cancellationToken);

        foreach (var position in await _restClient.GetPositions(cancellationToken))
        {
            if (position == null ||
                position.Exchange.IsEmpty() ||
                position.Token.IsEmpty())
                continue;
            var realized = position.RealizedPnL.ToDecimal();
            var unrealized = position.UnrealizedPnL.ToDecimal();
            if (unrealized == 0 && !position.TotalMtm.IsEmpty())
                unrealized = position.TotalMtm.ToDecimal() - realized;
            await SendOutMessageAsync(new PositionChangeMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = _resolvedPortfolioName,
                SecurityId = position.Exchange.ToSecurityId(
                    position.Token, position.TradingSymbol),
                ServerTime = CurrentTime,
            }
            .TryAdd(PositionChangeTypes.CurrentValue, position.NetQuantity.ToDecimal(), true)
            .TryAdd(PositionChangeTypes.AveragePrice, Positive(position.NetAveragePrice.ToDecimal()), true)
            .TryAdd(PositionChangeTypes.CurrentPrice, Positive(position.LastPrice.ToDecimal()), true)
            .TryAdd(PositionChangeTypes.RealizedPnL, realized, true)
            .TryAdd(PositionChangeTypes.UnrealizedPnL, unrealized, true), cancellationToken);
        }

        foreach (var holding in await _restClient.GetHoldings(cancellationToken))
        {
            var instrument = holding?.Instruments?.FirstOrDefault(item =>
                item != null &&
                !item.Exchange.IsEmpty() &&
                !item.Token.IsEmpty());
            if (instrument == null)
                continue;
            var current = holding.HoldingQuantity.ToDecimal() +
                holding.BtstQuantity.ToDecimal();
            var blocked = holding.CollateralQuantity.ToDecimal() +
                holding.BrokerCollateralQuantity.ToDecimal() +
                holding.UsedQuantity.ToDecimal();
            await SendOutMessageAsync(new PositionChangeMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = _resolvedPortfolioName,
                SecurityId = instrument.Exchange.ToSecurityId(
                    instrument.Token, instrument.TradingSymbol),
                ServerTime = CurrentTime,
            }
            .TryAdd(PositionChangeTypes.CurrentValue, current, true)
            .TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
            .TryAdd(PositionChangeTypes.AveragePrice, Positive(holding.UploadPrice.ToDecimal()), true),
                cancellationToken);
        }
    }

    private async ValueTask ProcessOrder(
        FirstockOrder order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (order == null || order.OrderId.IsEmpty())
            return;

        _orderTransactions.TryGetValue(order.OrderId, out var transactionId);
        if (transactionId == 0 &&
            long.TryParse(order.Remarks, NumberStyles.Integer, CultureInfo.InvariantCulture, out var remarkId))
            transactionId = remarkId;
        RememberOrder(order.OrderId, transactionId);
        var state = order.Status.ToOrderState(order.ReportType);
        var quantity = order.Quantity.ToDecimal();
        var filled = order.FilledQuantity.ToDecimal();
        if (state == OrderStates.Done &&
            order.Status?.Contains("CANCEL", StringComparison.OrdinalIgnoreCase) != true &&
            filled == 0)
            filled = quantity;
        var balance = Math.Max(0m, quantity - filled);
        if (order.Status?.Contains("CANCEL", StringComparison.OrdinalIgnoreCase) == true)
            balance = 0m;

        var securityId = await GetSecurityId(
            order.Exchange, order.Token, order.TradingSymbol, cancellationToken);
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
            SecurityId = securityId,
            PortfolioName = order.AccountId.IsEmpty(_resolvedPortfolioName),
            OrderType = order.ToOrderType(),
            Side = order.Side.ToSide(),
            TimeInForce = order.Retention.ToTimeInForce(),
            OrderPrice = order.Price.ToDecimal(),
            OrderVolume = quantity,
            Balance = balance,
            AveragePrice = Positive(order.AveragePrice.ToDecimal()),
            OrderState = state,
            ServerTime = GetOrderTime(order),
            Condition = CreateCondition(
                order.Product.ToProduct(),
                Positive(order.TriggerPrice.ToDecimal()),
                null,
                order.Status?.StartsWith("AMO", StringComparison.OrdinalIgnoreCase) == true,
                order.Remarks),
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    order.RejectionReason.IsEmpty($"Firstock order status: {order.Status}."))
                : null,
        }, cancellationToken);
    }

    private async ValueTask ProcessTrade(
        FirstockTrade trade,
        long originId,
        CancellationToken cancellationToken)
    {
        if (trade == null || trade.OrderId.IsEmpty())
            return;
        var fillId = trade.FillId.IsEmpty(
            $"{trade.OrderId}:{trade.FillTime}:{trade.FillPrice}:{trade.FillQuantity}");
        if (!_tradeIds.TryAdd(fillId))
            return;

        var transactionId = _orderTransactions.TryGetValue2(trade.OrderId) ?? 0;
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            OriginalTransactionId = originId != 0
                ? originId
                : transactionId != 0
                    ? transactionId
                    : _orderStatusSubscriptionId,
            TransactionId = originId != 0 ? transactionId : 0,
            OrderStringId = trade.OrderId,
            TradeStringId = fillId,
            SecurityId = await GetSecurityId(
                trade.Exchange, trade.Token, trade.TradingSymbol, cancellationToken),
            PortfolioName = _resolvedPortfolioName,
            Side = trade.Side.ToSide(),
            TradePrice = trade.FillPrice.ToDecimal(),
            TradeVolume = trade.FillQuantity.ToDecimal(),
            ServerTime = trade.FillTime.ToFirstockTime() ?? CurrentTime,
        }, cancellationToken);
    }

    private async ValueTask OnOrderReceived(
        FirstockOrder order,
        CancellationToken cancellationToken)
    {
        var transactionId = _orderTransactions.TryGetValue2(order.OrderId) ?? 0;
        if (transactionId != 0 || _orderStatusSubscriptionId != 0)
            await ProcessOrder(order, _orderStatusSubscriptionId, false, cancellationToken);

        if (order.Status.ToOrderState(order.ReportType) == OrderStates.Done &&
            order.Status?.Contains("CANCEL", StringComparison.OrdinalIgnoreCase) != true)
        {
            foreach (var trade in (await _restClient.GetTrades(cancellationToken))
                .Where(trade => trade.OrderId.EqualsIgnoreCase(order.OrderId)))
                await ProcessTrade(trade, 0, cancellationToken);
        }
    }

    private async Task<FirstockOrder> ResolveOrder(
        string orderId,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (orderId.IsEmpty())
            _transactionOrders.TryGetValue(originalTransactionId, out orderId);
        if (orderId.IsEmpty())
            throw new InvalidOperationException(
                LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));

        foreach (var order in await _restClient.GetOrders(cancellationToken))
        {
            if (order == null || order.OrderId.IsEmpty())
                continue;
            _orderTransactions.TryGetValue(order.OrderId, out var transactionId);
            RememberOrder(order.OrderId, transactionId);
            if (order.OrderId.EqualsIgnoreCase(orderId) ||
                transactionId != 0 && transactionId == originalTransactionId)
                return order;
        }
        throw new InvalidOperationException(
            $"Firstock order '{orderId}' was not found in the current order book.");
    }

    private async Task<FirstockInstrument> ResolveInstrument(
        SecurityId securityId,
        FirstockOrder order,
        CancellationToken cancellationToken)
    {
        if (securityId.Native is string native && !native.IsEmpty())
            return await GetInstrument(native, cancellationToken);
        if (!order.Exchange.IsEmpty() && !order.Token.IsEmpty())
            return await GetInstrument(
                order.Exchange.ToBoardCode().ToInstrumentKey(order.Token), cancellationToken);
        return await _restClient.FindInstrument(
            order.Exchange, order.TradingSymbol, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Firstock instrument '{order.Exchange}|{order.TradingSymbol}' was not found.");
    }

    private async Task<SecurityId> GetSecurityId(
        string exchange,
        string token,
        string tradingSymbol,
        CancellationToken cancellationToken)
    {
        if (!exchange.IsEmpty() && !token.IsEmpty())
            return exchange.ToSecurityId(token, tradingSymbol);
        var instrument = await _restClient.FindInstrument(
            exchange, tradingSymbol, cancellationToken);
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
            throw new ArgumentOutOfRangeException(
                nameof(orderType), orderType,
                "Firstock supports market, limit, stop-limit, and stop-market orders.");
    }

    private static long ToQuantity(decimal value, string parameterName)
    {
        if (value <= 0 ||
            value != decimal.Truncate(value) ||
            value > long.MaxValue)
            throw new ArgumentOutOfRangeException(
                parameterName, value,
                "Firstock quantities must be positive whole numbers within Int64 range.");
        return decimal.ToInt64(value);
    }

    private static bool RequiresMarketProtection(string priceType)
        => priceType is "MKT" or "SL-MKT" or "SL-M";

    private static string FormatPrice(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatOptional(decimal? value)
        => value is > 0 ? FormatPrice(value.Value) : null;

    private static FirstockOrderCondition CreateCondition(
        FirstockProducts product,
        decimal? triggerPrice,
        decimal? marketProtection,
        bool isAfterMarket,
        string remarks)
        => new()
        {
            Product = product,
            TriggerPrice = triggerPrice,
            MarketProtection = marketProtection,
            IsAfterMarket = isAfterMarket,
            Remarks = remarks,
        };

    private DateTime GetOrderTime(FirstockOrder order)
        => order.ExchangeTime.ToFirstockTime() ??
            order.OrderTime.ToFirstockTime() ??
            CurrentTime;
}
