namespace StockSharp.Definedge;

public partial class DefinedgeMessageAdapter
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
        var instrument = await GetInstrument(
            regMsg.SecurityId.ToInstrumentKey(),
            cancellationToken);
        var condition =
            regMsg.Condition as DefinedgeOrderCondition;
        var request = CreateOrderRequest(
            regMsg,
            instrument,
            condition,
            DefaultProduct,
            AlgoId);
        var orderId = await _restClient.PlaceOrder(
            request, cancellationToken);
        RememberOrder(orderId, regMsg.TransactionId);

        var orderType =
            regMsg.OrderType ?? OrderTypes.Limit;
        var product =
            condition?.Product ?? DefaultProduct;
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = regMsg.TransactionId,
            OrderStringId = orderId,
            SecurityId = regMsg.SecurityId,
            PortfolioName = AccountId,
            OrderType = orderType,
            Side = regMsg.Side,
            TimeInForce =
                regMsg.TimeInForce ??
                TimeInForce.PutInQueue,
            OrderPrice = regMsg.Price,
            OrderVolume = regMsg.Volume,
            Balance = regMsg.Volume,
            OrderState = OrderStates.Pending,
            ServerTime = CurrentTime,
            Condition = CreateCondition(
                product,
                condition?.TriggerPrice,
                condition?.DisclosedVolume,
                condition?.IsAfterMarket == true,
                condition?.BookLossPrice,
                condition?.BookProfitPrice,
                condition?.TrailingPrice,
                condition?.MarketProtection,
                condition?.Remarks),
        }, cancellationToken);
    }

    internal static DefinedgeOrderRequest CreateOrderRequest(
        OrderRegisterMessage message,
        DefinedgeInstrument instrument,
        DefinedgeOrderCondition condition,
        DefinedgeProducts defaultProduct,
        string algoId)
    {
        var orderType =
            message.OrderType ?? OrderTypes.Limit;
        ValidateOrderType(orderType);
        if (message.TimeInForce == TimeInForce.MatchOrCancel)
        {
            throw new NotSupportedException(
                "Definedge does not expose fill-or-kill orders.");
        }

        var triggerPrice = condition?.TriggerPrice;
        if (orderType == OrderTypes.Conditional &&
            triggerPrice is not > 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Definedge stop order.");
        }
        if (orderType is
            OrderTypes.Limit or OrderTypes.Conditional &&
            message.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message.Price),
                message.Price,
                "A positive limit price is required.");
        }
        if (triggerPrice is > 0 &&
            orderType is
                OrderTypes.Limit or OrderTypes.Conditional)
        {
            if (message.Side == Sides.Buy &&
                triggerPrice > message.Price)
            {
                throw new InvalidOperationException(
                    "A Definedge buy stop-limit trigger cannot exceed the limit price.");
            }
            if (message.Side == Sides.Sell &&
                triggerPrice < message.Price)
            {
                throw new InvalidOperationException(
                    "A Definedge sell stop-limit trigger cannot be below the limit price.");
            }
        }

        var quantity = ToQuantity(
            message.Volume, nameof(message.Volume), false);
        var disclosed = ToQuantity(
            condition?.DisclosedVolume ?? 0,
            nameof(condition.DisclosedVolume),
            true);
        if (disclosed > quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(condition.DisclosedVolume),
                disclosed,
                "Disclosed quantity cannot exceed order quantity.");
        }

        return new()
        {
            Exchange = instrument.Exchange,
            Side = message.Side.ToNative(),
            Price = orderType == OrderTypes.Market
                ? 0
                : message.Price,
            PriceType =
                orderType.ToPriceType(triggerPrice),
            Product =
                (condition?.Product ?? defaultProduct).ToNative(),
            Quantity = quantity,
            TradingSymbol = instrument.TradingSymbol,
            AlgoId = algoId.ThrowIfEmpty(nameof(algoId)),
            AfterMarket =
                condition?.IsAfterMarket == true
                    ? "Yes"
                    : null,
            BookLossPrice =
                Positive(condition?.BookLossPrice),
            BookProfitPrice =
                Positive(condition?.BookProfitPrice),
            DisclosedQuantity =
                disclosed > 0 ? disclosed : null,
            MarketProtection =
                Positive(condition?.MarketProtection),
            Remarks = condition?.Remarks.IsEmpty(
                message.TransactionId.ToString(
                    CultureInfo.InvariantCulture)),
            TrailingPrice =
                Positive(condition?.TrailingPrice),
            TriggerPrice = Positive(triggerPrice),
            Validity = message.TimeInForce.ToValidity(),
        };
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(replaceMsg.PortfolioName);
        if (replaceMsg.TimeInForce ==
            TimeInForce.MatchOrCancel)
        {
            throw new NotSupportedException(
                "Definedge does not expose fill-or-kill orders.");
        }

        var current = await ResolveOrder(
            replaceMsg.OldOrderStringId,
            replaceMsg.OriginalTransactionId,
            cancellationToken);
        var orderType =
            replaceMsg.OrderType ?? current.ToOrderType();
        ValidateOrderType(orderType);
        var condition =
            replaceMsg.Condition as DefinedgeOrderCondition;
        var triggerPrice =
            condition?.TriggerPrice ??
            Positive(current.TriggerPrice.ToDecimal());
        if (orderType == OrderTypes.Conditional &&
            triggerPrice is not > 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Definedge stop order.");
        }
        if (orderType is
            OrderTypes.Limit or OrderTypes.Conditional &&
            replaceMsg.Price <= 0)
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
        var disclosed = ToQuantity(
            condition?.DisclosedVolume ??
                current.DisclosedQuantity.ToDecimal(),
            nameof(condition.DisclosedVolume),
            true);
        if (disclosed > quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(condition.DisclosedVolume),
                disclosed,
                "Disclosed quantity cannot exceed order quantity.");
        }
        var instrument = await ResolveInstrument(
            replaceMsg.SecurityId,
            current,
            cancellationToken);

        await _restClient.ModifyOrder(
            new DefinedgeOrderRequest
            {
                OrderId = current.OrderId,
                Exchange = instrument.Exchange,
                Side = current.Side.ToSide().ToNative(),
                Price = orderType == OrderTypes.Market
                    ? 0
                    : replaceMsg.Price,
                PriceType =
                    orderType.ToPriceType(triggerPrice),
                Product =
                    current.Product.ToProduct().ToNative(),
                Quantity = quantity,
                TradingSymbol =
                    instrument.TradingSymbol,
                AfterMarket =
                    condition?.IsAfterMarket == true
                        ? "Yes"
                        : null,
                BookLossPrice = Positive(
                    condition?.BookLossPrice ??
                    current.BookLossPrice.ToDecimal()),
                BookProfitPrice = Positive(
                    condition?.BookProfitPrice ??
                    current.BookProfitPrice.ToDecimal()),
                DisclosedQuantity =
                    disclosed > 0 ? disclosed : null,
                MarketProtection = Positive(
                    condition?.MarketProtection ??
                    current.MarketProtection.ToDecimal()),
                Remarks =
                    condition?.Remarks.IsEmpty(
                        current.Remarks),
                TrailingPrice = Positive(
                    condition?.TrailingPrice ??
                    current.TrailingPrice.ToDecimal()),
                TriggerPrice = Positive(triggerPrice),
                Validity =
                    ((TimeInForce?)(
                        replaceMsg.TimeInForce ??
                        current.Validity.ToTimeInForce()))
                    .ToValidity(),
            },
            cancellationToken);
        RememberOrder(
            current.OrderId, replaceMsg.TransactionId);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(cancelMsg.PortfolioName);
        var orderId = cancelMsg.OrderStringId;
        if (orderId.IsEmpty() &&
            _transactionOrders.TryGetValue(
                cancelMsg.OriginalTransactionId,
                out var remembered))
        {
            orderId = remembered;
        }
        if (orderId.IsEmpty())
        {
            throw new InvalidOperationException(
                LocalizedStrings.OrderNoExchangeId.Put(
                    cancelMsg.OriginalTransactionId));
        }
        await _restClient.CancelOrder(
            orderId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OrderStatusAsync(
        OrderStatusMessage statusMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            statusMsg.TransactionId, cancellationToken);

        if (!statusMsg.IsSubscribe)
        {
            if (_orderStatusSubscriptionId ==
                statusMsg.OriginalTransactionId)
            {
                _orderStatusSubscriptionId = 0;
            }
            return;
        }

        EnsurePortfolio(statusMsg.PortfolioName);
        await SendOrderSnapshot(
            statusMsg.TransactionId,
            true,
            cancellationToken,
            statusMsg);

        if (statusMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                statusMsg.TransactionId, cancellationToken);
        }
        else
        {
            _orderStatusSubscriptionId =
                statusMsg.TransactionId;
            await SendSubscriptionResultAsync(
                statusMsg, cancellationToken);
        }
    }

    private async ValueTask SendOrderSnapshot(
        long originalTransactionId,
        bool isLookup,
        CancellationToken cancellationToken,
        OrderStatusMessage filter = null)
    {
        var left = filter?.Count ?? long.MaxValue;

        foreach (var order in
            (await _restClient.GetOrders(cancellationToken))
                .Where(order => order != null)
                .OrderBy(GetOrderTime))
        {
            var time = GetOrderTime(order);
            if (filter?.From is DateTime from &&
                time < from.ToUniversalTime())
            {
                continue;
            }
            if (filter?.To is DateTime to &&
                time > to.ToUniversalTime())
            {
                continue;
            }
            await ProcessOrder(
                order,
                originalTransactionId,
                isLookup,
                cancellationToken);
            if (--left <= 0)
                break;
        }

        foreach (var trade in
            await _restClient.GetTrades(cancellationToken))
        {
            await ProcessTrade(
                trade,
                originalTransactionId,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(
        PortfolioLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        if (!lookupMsg.IsSubscribe)
        {
            if (_portfolioSubscriptionId ==
                lookupMsg.OriginalTransactionId)
            {
                _portfolioSubscriptionId = 0;
            }
            return;
        }

        EnsurePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            OriginalTransactionId =
                lookupMsg.TransactionId,
            PortfolioName = AccountId,
            BoardCode = "NSE",
        }, cancellationToken);
        await SendPortfolioSnapshot(
            lookupMsg.TransactionId, cancellationToken);

        if (lookupMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                lookupMsg.TransactionId, cancellationToken);
        }
        else
        {
            _portfolioSubscriptionId =
                lookupMsg.TransactionId;
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
        }
    }

    private async ValueTask SendPortfolioSnapshot(
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var limits = await _restClient.GetLimits(
            cancellationToken);
        var cash = limits.Cash.ToDecimal() +
            limits.PayIn.ToDecimal() -
            limits.PayOut.ToDecimal();
        await SendOutMessageAsync(
            new PositionChangeMessage
            {
                OriginalTransactionId =
                    originalTransactionId,
                PortfolioName = AccountId,
                SecurityId = SecurityId.Money,
                ServerTime = CurrentTime,
            }
            .TryAdd(
                PositionChangeTypes.BeginValue,
                cash,
                true)
            .TryAdd(
                PositionChangeTypes.CurrentValue,
                cash,
                true)
            .TryAdd(
                PositionChangeTypes.BlockedValue,
                limits.PendingOrderValue.ToDecimal(),
                true),
            cancellationToken);

        foreach (var position in
            await _restClient.GetPositions(cancellationToken))
        {
            if (position == null ||
                position.Exchange.IsEmpty() ||
                position.Token.IsEmpty())
            {
                continue;
            }
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId =
                        originalTransactionId,
                    PortfolioName = AccountId,
                    SecurityId =
                        position.Exchange.ToSecurityId(
                            position.Token,
                            position.TradingSymbol),
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    position.NetQuantity.ToDecimal(),
                    true)
                .TryAdd(
                    PositionChangeTypes.AveragePrice,
                    Positive(
                        position.NetAveragePrice.ToDecimal()),
                    true)
                .TryAdd(
                    PositionChangeTypes.CurrentPrice,
                    Positive(
                        position.LastPrice.ToDecimal()),
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

        foreach (var holding in
            await _restClient.GetHoldings(cancellationToken))
        {
            var instrument =
                holding?.Instruments?.FirstOrDefault(
                    item =>
                        item != null &&
                        !item.Exchange.IsEmpty() &&
                        !item.Token.IsEmpty());
            if (instrument == null)
                continue;
            var current =
                holding.DepositoryQuantity.ToDecimal() +
                holding.T1Quantity.ToDecimal() +
                holding.TradeQuantity.ToDecimal();
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId =
                        originalTransactionId,
                    PortfolioName = AccountId,
                    SecurityId =
                        instrument.Exchange.ToSecurityId(
                            instrument.Token,
                            instrument.TradingSymbol),
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    current,
                    true)
                .TryAdd(
                    PositionChangeTypes.BlockedValue,
                    holding.UsedQuantity.ToDecimal(),
                    true)
                .TryAdd(
                    PositionChangeTypes.AveragePrice,
                    Positive(
                        holding.AverageBuyPrice.ToDecimal()),
                    true),
                cancellationToken);
        }
    }

    private async ValueTask ProcessOrder(
        DefinedgeOrder order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (order == null || order.OrderId.IsEmpty())
            return;

        if (!_orderTransactions.TryGetValue(
            order.OrderId, out var transactionId) &&
            long.TryParse(
                order.Remarks,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var remarksTransactionId))
        {
            transactionId = remarksTransactionId;
        }
        RememberOrder(order.OrderId, transactionId);
        var state = order.OrderStatus.ToOrderState(
            order.ReportType);
        var quantity = order.Quantity.ToDecimal();
        var filled = order.FilledQuantity.ToDecimal();
        var balance =
            order.PendingQuantity.ToDecimal();
        if (balance <= 0 && state is not
            OrderStates.Done and not OrderStates.Failed)
        {
            balance = Math.Max(0, quantity - filled);
        }
        var securityId = await GetSecurityId(
            order.Exchange,
            order.Token,
            order.TradingSymbol,
            cancellationToken);
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = isLookup
                ? originId
                : transactionId != 0
                    ? transactionId
                    : _orderStatusSubscriptionId,
            TransactionId =
                isLookup ? transactionId : 0,
            OrderStringId = order.OrderId,
            SecurityId = securityId,
            PortfolioName =
                order.AccountId.IsEmpty(AccountId),
            OrderType = order.ToOrderType(),
            Side = order.Side.ToSide(),
            TimeInForce =
                order.Validity.ToTimeInForce(),
            OrderPrice = order.Price.ToDecimal(),
            OrderVolume = quantity,
            Balance = balance,
            AveragePrice =
                Positive(order.AveragePrice.ToDecimal()),
            OrderState = state,
            ServerTime = GetOrderTime(order),
            Condition = CreateCondition(
                order.Product.ToProduct(),
                Positive(order.TriggerPrice.ToDecimal()),
                Positive(
                    order.DisclosedQuantity.ToDecimal()),
                order.AfterMarket.EqualsIgnoreCase("true") ||
                    order.AfterMarket.EqualsIgnoreCase("yes"),
                Positive(
                    order.BookLossPrice.ToDecimal()),
                Positive(
                    order.BookProfitPrice.ToDecimal()),
                Positive(
                    order.TrailingPrice.ToDecimal()),
                Positive(
                    order.MarketProtection.ToDecimal()),
                order.Remarks),
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    order.RejectionReason.IsEmpty(
                        $"Definedge order status: {order.OrderStatus}."))
                : null,
        }, cancellationToken);
    }

    private async ValueTask ProcessTrade(
        DefinedgeOrder trade,
        long originId,
        CancellationToken cancellationToken)
    {
        if (trade == null || trade.OrderId.IsEmpty())
            return;
        var fillId = trade.FillId.IsEmpty(
            $"{trade.OrderId}:{trade.FillTime}:{trade.FillPrice}:{trade.FillQuantity}");
        if (!_tradeIds.TryAdd(fillId))
            return;

        var transactionId =
            _orderTransactions.TryGetValue2(
                trade.OrderId) ?? 0;
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            OriginalTransactionId =
                originId != 0
                    ? originId
                    : transactionId != 0
                        ? transactionId
                        : _orderStatusSubscriptionId,
            TransactionId =
                originId != 0 ? transactionId : 0,
            OrderStringId = trade.OrderId,
            TradeStringId = fillId,
            SecurityId = await GetSecurityId(
                trade.Exchange,
                trade.Token,
                trade.TradingSymbol,
                cancellationToken),
            PortfolioName =
                trade.AccountId.IsEmpty(AccountId),
            Side = trade.Side.ToSide(),
            TradePrice = trade.FillPrice.ToDecimal(),
            TradeVolume = trade.FillQuantity.ToDecimal(),
            ServerTime =
                trade.FillTime.ToDefinedgeTime() ??
                GetOrderTime(trade),
        }, cancellationToken);
    }

    private async ValueTask OnOrderReceived(
        DefinedgeOrder order,
        CancellationToken cancellationToken)
    {
        var transactionId =
            _orderTransactions.TryGetValue2(
                order.OrderId) ?? 0;
        if (transactionId != 0 ||
            _orderStatusSubscriptionId != 0)
        {
            await ProcessOrder(
                order,
                _orderStatusSubscriptionId,
                false,
                cancellationToken);
        }
        if (!order.FillId.IsEmpty() ||
            order.ReportType.EqualsIgnoreCase("Fill"))
        {
            await ProcessTrade(
                order, 0, cancellationToken);
        }
    }

    private async Task<DefinedgeOrder> ResolveOrder(
        string orderId,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (orderId.IsEmpty())
        {
            _transactionOrders.TryGetValue(
                originalTransactionId, out orderId);
        }
        if (orderId.IsEmpty())
        {
            throw new InvalidOperationException(
                LocalizedStrings.OrderNoExchangeId.Put(
                    originalTransactionId));
        }

        var order = await _restClient.GetOrder(
            orderId, cancellationToken);
        if (order != null && !order.OrderId.IsEmpty())
            return order;

        throw new InvalidOperationException(
            $"Definedge order '{orderId}' was not found in the current order book.");
    }

    private async Task<DefinedgeInstrument> ResolveInstrument(
        SecurityId securityId,
        DefinedgeOrder order,
        CancellationToken cancellationToken)
    {
        if (securityId.Native is string native &&
            !native.IsEmpty())
        {
            return await GetInstrument(
                native, cancellationToken);
        }
        if (!order.Exchange.IsEmpty() &&
            !order.Token.IsEmpty())
        {
            return await GetInstrument(
                order.Exchange.ToInstrumentKey(
                    order.Token),
                cancellationToken);
        }
        return await _restClient.FindInstrument(
            order.Exchange,
            order.TradingSymbol,
            cancellationToken) ??
            throw new InvalidOperationException(
                $"Definedge instrument '{order.Exchange}|{order.TradingSymbol}' was not found.");
    }

    private async Task<SecurityId> GetSecurityId(
        string exchange,
        string token,
        string tradingSymbol,
        CancellationToken cancellationToken)
    {
        if (!exchange.IsEmpty() && !token.IsEmpty())
        {
            return exchange.ToSecurityId(
                token, tradingSymbol);
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

    private void RememberOrder(
        string orderId, long transactionId)
    {
        if (orderId.IsEmpty() || transactionId == 0)
            return;
        _orderTransactions[orderId] = transactionId;
        _transactionOrders[transactionId] = orderId;
    }

    private void EnsurePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(AccountId))
        {
            throw new InvalidOperationException(
                LocalizedStrings.AccountNotFound);
        }
    }

    private static void ValidateOrderType(
        OrderTypes orderType)
    {
        if (orderType is not
            OrderTypes.Limit and not
            OrderTypes.Market and not
            OrderTypes.Conditional)
        {
            throw new ArgumentOutOfRangeException(
                nameof(orderType),
                orderType,
                "Definedge supports market, limit, stop-limit, and stop-market orders.");
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
                "Definedge quantities must be non-negative whole numbers within Int64 range.");
        }
        return decimal.ToInt64(value);
    }

    private static DefinedgeOrderCondition CreateCondition(
        DefinedgeProducts product,
        decimal? triggerPrice,
        decimal? disclosedVolume,
        bool isAfterMarket,
        decimal? bookLossPrice,
        decimal? bookProfitPrice,
        decimal? trailingPrice,
        decimal? marketProtection,
        string remarks)
        => new()
        {
            Product = product,
            TriggerPrice = triggerPrice,
            DisclosedVolume = disclosedVolume,
            IsAfterMarket = isAfterMarket,
            BookLossPrice = bookLossPrice,
            BookProfitPrice = bookProfitPrice,
            TrailingPrice = trailingPrice,
            MarketProtection = marketProtection,
            Remarks = remarks,
        };

    private DateTime GetOrderTime(DefinedgeOrder order)
        => order.ExchangeTime.ToDefinedgeTime() ??
            order.OrderEntryTime.ToDefinedgeTime() ??
            CurrentTime;
}
