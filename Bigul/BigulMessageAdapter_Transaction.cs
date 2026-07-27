namespace StockSharp.Bigul;

public partial class BigulMessageAdapter
{
    private readonly SynchronizedDictionary<string, long> _orderTransactions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
    private readonly SynchronizedSet<string> _tradeIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<string> _afterMarketOrders =
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
            throw new NotSupportedException("Bigul does not expose fill-or-kill orders.");

        var condition = regMsg.Condition as BigulOrderCondition;
        var triggerPrice = condition?.TriggerPrice;
        if (orderType == OrderTypes.Conditional && triggerPrice is not > 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Bigul stop order.");
        }
        if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regMsg.Price),
                regMsg.Price,
                "A positive limit price is required.");
        }

        var quantity = ToQuantity(regMsg.Volume, nameof(regMsg.Volume));
        var disclosed = ToDisclosedQuantity(
            condition?.DisclosedVolume,
            quantity);
        var instrument = await GetInstrument(
            regMsg.SecurityId.ToInstrumentKey(),
            cancellationToken);
        var product = condition?.Product ?? DefaultProduct;
        var marketProtection = condition?.MarketProtection ?? MarketProtection;
        if (marketProtection < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BigulOrderCondition.MarketProtection),
                marketProtection,
                "Bigul market protection cannot be negative.");
        }

        var remarks = condition?.Remarks.IsEmpty(
            regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));
        var afterMarket = condition?.IsAfterMarket == true;
        var orderId = await _restClient.PlaceOrder(
            new()
            {
                ExchangeSegment = instrument.Segment,
                Product = product.ToNative(),
                Price = FormatPrice(orderType == OrderTypes.Market ? 0m : regMsg.Price),
                MarketProtection = FormatPrice(marketProtection),
                PriceType = orderType.ToPriceType(regMsg.Price),
                Quantity = quantity.ToString(CultureInfo.InvariantCulture),
                Retention = regMsg.TimeInForce.ToRetention(),
                Token = instrument.Token,
                TriggerPrice = FormatPrice(triggerPrice ?? 0m),
                TradingSymbol = instrument.TradingSymbol,
                Side = regMsg.Side.ToNative(),
                AfterMarket = afterMarket ? "YES" : "NO",
                Remarks = remarks,
                UserTag = condition?.UserTag ?? string.Empty,
                DisclosedQuantity = disclosed.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);

        RememberOrder(orderId, regMsg.TransactionId);
        if (afterMarket)
            _afterMarketOrders.Add(orderId);
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
                afterMarket,
                disclosed,
                remarks,
                condition?.UserTag),
        }, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(replaceMsg.PortfolioName);
        if (replaceMsg.TimeInForce == TimeInForce.MatchOrCancel)
            throw new NotSupportedException("Bigul does not expose fill-or-kill orders.");

        var current = await ResolveOrder(
            replaceMsg.OldOrderStringId,
            replaceMsg.OriginalTransactionId,
            cancellationToken);
        var orderType = replaceMsg.OrderType ?? current.ToOrderType();
        ValidateOrderType(orderType);
        var condition = replaceMsg.Condition as BigulOrderCondition;
        var triggerPrice = condition?.TriggerPrice ??
            Positive(current.TriggerPrice.ToDecimal());
        if (orderType == OrderTypes.Conditional && triggerPrice is not > 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Bigul stop order.");
        }
        if (orderType == OrderTypes.Limit && replaceMsg.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replaceMsg.Price),
                replaceMsg.Price,
                "A positive limit price is required.");
        }

        var quantity = ToQuantity(replaceMsg.Volume, nameof(replaceMsg.Volume));
        var disclosed = ToDisclosedQuantity(
            condition?.DisclosedVolume,
            quantity);
        var instrument = await ResolveInstrument(
            replaceMsg.SecurityId,
            current,
            cancellationToken);
        var product = condition?.Product ?? current.Product.ToProduct();
        var marketProtection = condition?.MarketProtection ?? MarketProtection;
        if (marketProtection < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BigulOrderCondition.MarketProtection),
                marketProtection,
                "Bigul market protection cannot be negative.");
        }
        var afterMarket = condition?.IsAfterMarket == true ||
            current.IsAfterMarket() ||
            _afterMarketOrders.Contains(current.OrderId);

        var orderId = await _restClient.ModifyOrder(
            new()
            {
                OrderId = current.OrderId,
                Validity = replaceMsg.TimeInForce.ToRetention(),
                ExchangeSegment = instrument.Segment,
                Product = product.ToNative(),
                Price = FormatPrice(
                    orderType == OrderTypes.Market ? 0m : replaceMsg.Price),
                MarketProtection = FormatPrice(marketProtection),
                PriceType = orderType.ToPriceType(replaceMsg.Price),
                Quantity = quantity.ToString(CultureInfo.InvariantCulture),
                Retention = replaceMsg.TimeInForce.ToRetention(),
                Token = instrument.Token,
                TriggerPrice = FormatPrice(triggerPrice ?? 0m),
                TradingSymbol = instrument.TradingSymbol,
                Side = replaceMsg.Side.ToNative(),
                AfterMarket = afterMarket ? "YES" : "NO",
                Remarks = condition?.Remarks.IsEmpty(current.Remarks),
                UserTag = condition?.UserTag ?? string.Empty,
                DisclosedQuantity = disclosed.ToString(CultureInfo.InvariantCulture),
                ScripName = instrument.Description.IsEmpty(instrument.Symbol),
                Action = current.Status.IsEmpty("PENDING"),
            },
            cancellationToken);
        RememberOrder(orderId, replaceMsg.TransactionId);
        if (afterMarket)
            _afterMarketOrders.Add(orderId);
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
        var afterMarket = current.IsAfterMarket() ||
            _afterMarketOrders.Contains(current.OrderId);
        await _restClient.CancelOrder(
            current.OrderId,
            afterMarket,
            current.TradingSymbol,
            cancellationToken);
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

        foreach (var trade in await _restClient.GetTrades(cancellationToken))
        {
            await ProcessTrade(
                trade,
                statusMsg.TransactionId,
                cancellationToken);
        }

        _lastOrderRefresh = CurrentTime;
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
                cancellationToken);
        }
    }

    private async ValueTask SendPortfolioSnapshot(
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var limits = await _restClient.GetLimits(cancellationToken);
        var cash = limits.NotionalCash.ToDecimal();
        if (cash == 0)
            cash = limits.LiquidCashCollateral.ToDecimal();
        var available = limits.Net.ToDecimal();
        await SendOutMessageAsync(new PositionChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            PortfolioName = _resolvedPortfolioName,
            SecurityId = SecurityId.Money,
            ServerTime = CurrentTime,
        }
        .TryAdd(PositionChangeTypes.BeginValue, cash, true)
        .TryAdd(
            PositionChangeTypes.CurrentValue,
            available != 0 ? available : cash,
            true)
        .TryAdd(
            PositionChangeTypes.BlockedValue,
            limits.MarginUsed.ToDecimal(),
            true), cancellationToken);

        foreach (var position in await _restClient.GetPositions(cancellationToken))
            await ProcessPosition(position, originalTransactionId, cancellationToken);

        foreach (var holding in await _restClient.GetHoldings(cancellationToken))
        {
            if (holding == null)
                continue;
            var segment = holding.PrimarySegment;
            var token = holding.PrimaryToken;
            var tradingSymbol = holding.NseTradingSymbol;
            if (segment.IsEmpty() || token.IsEmpty())
            {
                segment = holding.SecondarySegment;
                token = holding.SecondaryToken;
                tradingSymbol = holding.BseTradingSymbol;
            }
            if (segment.IsEmpty() || token.IsEmpty())
                continue;

            var current = holding.HoldingQuantity.ToDecimal() +
                holding.BtstQuantity.ToDecimal() +
                holding.T1Quantity.ToDecimal();
            var blocked = holding.CollateralQuantity.ToDecimal() +
                holding.WithheldHoldingQuantity.ToDecimal() +
                holding.WithheldCollateralQuantity.ToDecimal();
            var averagePrice = holding.BuyPrice.ToDecimal();
            if (averagePrice == 0)
                averagePrice = holding.Price.ToDecimal();
            await SendOutMessageAsync(new PositionChangeMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = _resolvedPortfolioName,
                SecurityId = segment.ToSecurityId(token, tradingSymbol),
                ServerTime = CurrentTime,
            }
            .TryAdd(PositionChangeTypes.CurrentValue, current, true)
            .TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
            .TryAdd(
                PositionChangeTypes.AveragePrice,
                Positive(averagePrice),
                true), cancellationToken);
        }
    }

    private async ValueTask ProcessPosition(
        BigulPosition position,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (position == null ||
            position.Segment.IsEmpty() ||
            position.Token.IsEmpty())
            return;

        var buyQuantity = position.CarryBuyQuantity.ToDecimal() +
            position.BuyQuantity.ToDecimal();
        var sellQuantity = position.CarrySellQuantity.ToDecimal() +
            position.SellQuantity.ToDecimal();
        var current = buyQuantity - sellQuantity;
        var buyAmount = position.CarryBuyAmount.ToDecimal() +
            position.BuyAmount.ToDecimal();
        var sellAmount = position.CarrySellAmount.ToDecimal() +
            position.SellAmount.ToDecimal();
        var averagePrice = current > 0 && buyQuantity > 0
            ? buyAmount / buyQuantity
            : current < 0 && sellQuantity > 0
                ? sellAmount / sellQuantity
                : position.UploadPrice.ToDecimal();

        await SendOutMessageAsync(new PositionChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            PortfolioName = _resolvedPortfolioName,
            SecurityId = position.Segment.ToSecurityId(
                position.Token,
                position.TradingSymbol),
            ServerTime = ToUpdateTime(position.UpdateReceivedTime),
        }
        .TryAdd(PositionChangeTypes.CurrentValue, current, true)
        .TryAdd(
            PositionChangeTypes.AveragePrice,
            Positive(averagePrice),
            true), cancellationToken);
    }

    private async ValueTask ProcessOrder(
        BigulOrder order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (order == null || order.OrderId.IsEmpty())
            return;

        _orderTransactions.TryGetValue(order.OrderId, out var transactionId);
        if (transactionId == 0 &&
            long.TryParse(
                order.Remarks,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var remarkId))
            transactionId = remarkId;
        RememberOrder(order.OrderId, transactionId);
        if (order.IsAfterMarket())
            _afterMarketOrders.Add(order.OrderId);

        var state = order.Status.ToOrderState();
        var quantity = order.Quantity.ToDecimal();
        var filled = order.FilledQuantity.ToDecimal();
        if (state == OrderStates.Done && !order.IsCancelled() && filled == 0)
            filled = quantity;
        var balance = order.UnfilledQuantity.ToDecimal();
        if (balance == 0 && state is not OrderStates.Done and not OrderStates.Failed)
            balance = Math.Max(0m, quantity - filled);
        if (state == OrderStates.Done || order.IsCancelled())
            balance = 0m;

        var securityId = await GetSecurityId(
            order.Segment,
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
            TransactionId = isLookup ? transactionId : 0,
            OrderStringId = order.OrderId,
            SecurityId = securityId,
            PortfolioName = order.AccountId.IsEmpty(_resolvedPortfolioName),
            OrderType = order.ToOrderType(),
            Side = order.Side.ToSide(),
            TimeInForce = order.Validity.ToTimeInForce(),
            OrderPrice = order.Price.ToDecimal(),
            OrderVolume = quantity,
            Balance = balance,
            AveragePrice = Positive(order.AveragePrice.ToDecimal()),
            OrderState = state,
            ServerTime = GetOrderTime(order),
            Condition = CreateCondition(
                order.Product.ToProduct(),
                Positive(order.TriggerPrice.ToDecimal()),
                Positive(MarketProtection),
                order.IsAfterMarket() ||
                    _afterMarketOrders.Contains(order.OrderId),
                0,
                order.Remarks,
                null),
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    order.RejectionReason.IsEmpty(
                        $"Bigul order status: {order.Status}."))
                : null,
        }, cancellationToken);
    }

    private async ValueTask ProcessTrade(
        BigulTrade trade,
        long originId,
        CancellationToken cancellationToken)
    {
        if (trade == null || trade.OrderId.IsEmpty())
            return;
        var fillId = trade.FillId.IsEmpty(
            $"{trade.OrderId}:{trade.FillTime}:{trade.Price}:{trade.Quantity}");
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
                trade.Segment,
                trade.Token,
                trade.TradingSymbol,
                cancellationToken),
            PortfolioName = _resolvedPortfolioName,
            Side = trade.Side.ToSide(),
            TradePrice = trade.Price.ToDecimal(),
            TradeVolume = trade.Quantity.ToDecimal(),
            ServerTime = trade.FillTime.ToBigulTime() ??
                trade.ExchangeTime.ToBigulTime() ??
                ToUpdateTime(trade.UpdateReceivedTime),
        }, cancellationToken);
    }

    private async ValueTask OnOrderReceived(
        BigulOrder order,
        CancellationToken cancellationToken)
    {
        var transactionId = _orderTransactions.TryGetValue2(order.OrderId) ?? 0;
        if (transactionId != 0 || _orderStatusSubscriptionId != 0)
        {
            await ProcessOrder(
                order,
                _orderStatusSubscriptionId,
                false,
                cancellationToken);
        }
        _lastOrderRefresh = CurrentTime;
    }

    private async ValueTask OnTradeReceived(
        BigulTrade trade,
        CancellationToken cancellationToken)
    {
        var transactionId = _orderTransactions.TryGetValue2(trade.OrderId) ?? 0;
        if (transactionId != 0 || _orderStatusSubscriptionId != 0)
            await ProcessTrade(trade, 0, cancellationToken);
        _lastOrderRefresh = CurrentTime;
    }

    private async Task<BigulOrder> ResolveOrder(
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
            RememberOrder(order.OrderId, transactionId);
            if (order.OrderId.EqualsIgnoreCase(orderId) ||
                transactionId != 0 &&
                transactionId == originalTransactionId)
                return order;
        }
        throw new InvalidOperationException(
            $"Bigul order '{orderId}' was not found in the current order book.");
    }

    private async Task<BigulInstrument> ResolveInstrument(
        SecurityId securityId,
        BigulOrder order,
        CancellationToken cancellationToken)
    {
        if (securityId.Native is string native && !native.IsEmpty())
            return await GetInstrument(native, cancellationToken);
        if (!order.Segment.IsEmpty() && !order.Token.IsEmpty())
        {
            return await GetInstrument(
                order.Segment.ToInstrumentKey(order.Token),
                cancellationToken);
        }
        return await _restClient.FindInstrument(
            order.Segment,
            order.TradingSymbol,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Bigul instrument '{order.Segment}|{order.TradingSymbol}' was not found.");
    }

    private async Task<SecurityId> GetSecurityId(
        string segment,
        string token,
        string tradingSymbol,
        CancellationToken cancellationToken)
    {
        if (!segment.IsEmpty() && !token.IsEmpty())
            return segment.ToSecurityId(token, tradingSymbol);
        var instrument = await _restClient.FindInstrument(
            segment,
            tradingSymbol,
            cancellationToken);
        if (instrument != null)
            return instrument.ToSecurityId();
        return new()
        {
            SecurityCode = tradingSymbol,
            BoardCode = segment.ToBoardCode(),
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
                "Bigul supports market, limit, stop-limit, and stop-market orders.");
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
                "Bigul quantities must be positive whole numbers within Int64 range.");
        }
        return decimal.ToInt64(value);
    }

    private static long ToDisclosedQuantity(decimal? value, long quantity)
    {
        if (value is null or 0)
            return 0;
        var disclosed = ToQuantity(value.Value, nameof(BigulOrderCondition.DisclosedVolume));
        if (disclosed > quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BigulOrderCondition.DisclosedVolume),
                value,
                "Disclosed volume cannot exceed order volume.");
        }
        return disclosed;
    }

    private static string FormatPrice(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static BigulOrderCondition CreateCondition(
        BigulProducts product,
        decimal? triggerPrice,
        decimal? marketProtection,
        bool isAfterMarket,
        decimal disclosedVolume,
        string remarks,
        string userTag)
        => new()
        {
            Product = product,
            TriggerPrice = triggerPrice,
            MarketProtection = marketProtection,
            IsAfterMarket = isAfterMarket,
            DisclosedVolume = disclosedVolume > 0 ? disclosedVolume : null,
            Remarks = remarks,
            UserTag = userTag,
        };

    private DateTime GetOrderTime(BigulOrder order)
        => order.ExchangeTime.ToBigulTime() ??
            order.OrderTime.ToBigulTime() ??
            order.UpdateTime.ToBigulTime() ??
            ToUpdateTime(order.UpdateReceivedTime);

    private DateTime ToUpdateTime(long value)
    {
        if (value <= 0)
            return CurrentTime;
        if (value > 10_000_000_000_000_000)
            value /= 1_000_000;
        else if (value > 10_000_000_000_000)
            value /= 1_000;
        return value > 10_000_000_000
            ? value.FromUnixMilliseconds()
            : value.FromUnixSeconds();
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
