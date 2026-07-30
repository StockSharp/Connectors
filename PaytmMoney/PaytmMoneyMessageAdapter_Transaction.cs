namespace StockSharp.PaytmMoney;

public partial class PaytmMoneyMessageAdapter
{
    private readonly SynchronizedDictionary<
        string, long> _orderTransactions =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, decimal> _orderFills =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, PaytmMoneyOrder> _orderCache =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, string> _orderFingerprints =
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
        var condition =
            regMsg.Condition as PaytmMoneyOrderCondition;
        var product =
            condition?.Product ?? DefaultProduct;
        var request = CreateOrderRequest(
            regMsg, condition, product);
        var result = await _restClient.PlaceOrder(
            request, product, cancellationToken);
        var orderId = result?.OrderNumber;
        if (orderId.IsEmpty())
        {
            throw new InvalidOperationException(
                "Paytm Money did not return an order number.");
        }

        _orderTransactions[orderId] =
            regMsg.TransactionId;
        _orderFills[orderId] = 0;
        _orderCache[orderId] = new()
        {
            OrderNumber = orderId,
            Exchange = request.Exchange,
            Segment = request.Segment,
            SecurityId = request.SecurityId,
            TransactionType = request.TransactionType,
            Product = request.Product,
            Quantity = request.Quantity,
            RemainingQuantity = request.Quantity,
            OrderType = request.OrderType,
            Price = request.Price,
            TriggerPrice = request.TriggerPrice ?? 0,
            Validity = request.Validity,
            MarketType = request.MarketType,
            LegNumber = request.LegNumber,
            AlgoOrderNumber = request.AlgoOrderNumber,
            Status = "PENDING",
            Remarks = request.Remarks,
        };

        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = regMsg.TransactionId,
            OrderStringId = orderId,
            SecurityId = regMsg.SecurityId,
            PortfolioName = _portfolioName,
            OrderType = regMsg.OrderType,
            Side = regMsg.Side,
            TimeInForce = regMsg.TimeInForce,
            OrderPrice = regMsg.Price,
            OrderVolume = regMsg.Volume,
            Balance = regMsg.Volume,
            OrderState = OrderStates.Pending,
            ServerTime = CurrentTime,
            Condition = condition,
        }, cancellationToken);
    }

    internal static PaytmMoneyOrderRequest CreateOrderRequest(
        OrderRegisterMessage message,
        PaytmMoneyOrderCondition condition,
        PaytmMoneyProducts product)
    {
        ArgumentNullException.ThrowIfNull(message);
        var (
            exchange,
            segment,
            securityId,
            _,
            _) = message.SecurityId
                .ToInstrumentKey()
                .ParseInstrumentKey();
        var triggerPrice = condition?.TriggerPrice;
        return new()
        {
            Source = "N",
            TransactionType = message.Side.ToNative(),
            Exchange = exchange,
            Segment = segment,
            Product = product.ToNative(),
            SecurityId = securityId,
            Quantity = message.Volume.To<long>(),
            Validity = message.TimeInForce.ToNative(),
            OrderType = (message.OrderType ?? OrderTypes.Limit)
                .ToNative(triggerPrice),
            Price = message.OrderType == OrderTypes.Market
                ? 0
                : message.Price,
            OffMarket = condition?.AfterMarket == true,
            TriggerPrice = triggerPrice,
            MarketType = "NL",
            LegNumber = condition?.LegNumber,
            ProfitValue = condition?.ProfitValue,
            StopLossValue = condition?.StopLossValue,
            AlgoOrderNumber = condition?.AlgoOrderNumber,
            Remarks = message.TransactionId.ToString(
                CultureInfo.InvariantCulture),
        };
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        var orderId = GetOrderId(
            replaceMsg.OldOrderStringId,
            replaceMsg.OldOrderId,
            replaceMsg.OriginalTransactionId);
        var order = await ResolveOrder(
            orderId, cancellationToken);
        var condition =
            replaceMsg.Condition as PaytmMoneyOrderCondition;
        var product = condition?.Product ??
            order.Product.ToProduct();
        var request = CreateOrderRequest(
            order, condition, product);
        request.Quantity = replaceMsg.Volume.To<long>();
        request.Price =
            replaceMsg.OrderType == OrderTypes.Market
                ? 0
                : replaceMsg.Price;
        request.OrderType =
            (replaceMsg.OrderType ?? order.OrderType.ToOrderType())
                .ToNative(condition?.TriggerPrice ??
                    (order.TriggerPrice > 0
                        ? order.TriggerPrice
                        : null));
        request.Validity = replaceMsg.TimeInForce.ToNative();
        request.Remarks = replaceMsg.TransactionId.ToString(
            CultureInfo.InvariantCulture);

        var result = await _restClient.ModifyOrder(
            request, product, cancellationToken);
        if (!result.OrderNumber.IsEmpty())
            _orderTransactions[result.OrderNumber] =
                replaceMsg.TransactionId;
        _orderTransactions[orderId] =
            replaceMsg.TransactionId;
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        var orderId = GetOrderId(
            cancelMsg.OrderStringId,
            cancelMsg.OrderId,
            cancelMsg.OriginalTransactionId);
        var order = await ResolveOrder(
            orderId, cancellationToken);
        var condition =
            cancelMsg.Condition as PaytmMoneyOrderCondition;
        var product = condition?.Product ??
            order.Product.ToProduct();
        var request = CreateOrderRequest(
            order, condition, product);
        request.Remarks = cancelMsg.TransactionId.ToString(
            CultureInfo.InvariantCulture);
        await _restClient.CancelOrder(
            request, product, cancellationToken);
    }

    private static PaytmMoneyOrderRequest CreateOrderRequest(
        PaytmMoneyOrder order,
        PaytmMoneyOrderCondition condition,
        PaytmMoneyProducts product)
        => new()
        {
            Source = "N",
            TransactionType = order.TransactionType,
            Exchange = order.Exchange,
            Segment = order.Segment,
            Product = product.ToNative(),
            SecurityId = order.SecurityId,
            Quantity = order.Quantity.To<long>(),
            Validity = order.Validity.IsEmpty("DAY"),
            OrderType = order.OrderType,
            Price = order.Price,
            OffMarket = order.OffMarket.ToBoolean(),
            TriggerPrice = condition?.TriggerPrice ??
                (order.TriggerPrice > 0
                    ? order.TriggerPrice
                    : null),
            MarketType = order.MarketType.IsEmpty("NL"),
            OrderNumber = order.OrderNumber,
            SerialNumber = order.SerialNumber,
            GroupId = order.GroupId,
            LegNumber = condition?.LegNumber
                .IsEmpty(order.LegNumber),
            ProfitValue = condition?.ProfitValue,
            StopLossValue = condition?.StopLossValue,
            AlgoOrderNumber = condition?.AlgoOrderNumber
                .IsEmpty(order.AlgoOrderNumber),
            ClientId = order.ClientId,
            Remarks = order.Remarks,
        };

    /// <inheritdoc />
    protected override async ValueTask OrderStatusAsync(
        OrderStatusMessage statusMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            statusMsg.TransactionId, cancellationToken);

        if (!statusMsg.IsSubscribe)
        {
            _orderStatusSubscriptionId = 0;
            return;
        }

        await SendOrderSnapshot(
            statusMsg.TransactionId,
            true,
            cancellationToken);
        if (!statusMsg.IsHistoryOnly())
        {
            _orderStatusSubscriptionId =
                statusMsg.TransactionId;
        }
        await SendSubscriptionResultAsync(
            statusMsg, cancellationToken);
    }

    private async ValueTask SendOrderSnapshot(
        long originalTransactionId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        foreach (var order in await _restClient
            .GetOrders(cancellationToken))
        {
            await ProcessOrder(
                order,
                originalTransactionId,
                isLookup,
                cancellationToken);
            if (order?.TradedQuantity is not > 0 ||
                order.OrderNumber.IsEmpty())
            {
                continue;
            }

            foreach (var trade in await _restClient
                .GetTradeDetails(
                    order.OrderNumber,
                    order.LegNumber,
                    order.Segment,
                    cancellationToken))
            {
                await ProcessTrade(
                    trade,
                    order,
                    originalTransactionId,
                    cancellationToken);
            }
        }
    }

    private async ValueTask ProcessOrder(
        PaytmMoneyOrder order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (order == null ||
            order.OrderNumber.IsEmpty() ||
            order.SecurityId.IsEmpty() ||
            order.Exchange.IsEmpty())
        {
            return;
        }

        _orderCache[order.OrderNumber] = order;
        var transactionId = ParseTransactionId(
            order.Remarks, order.OrderNumber);
        var state = order.Status
            .IsEmpty(order.DisplayStatus)
            .ToOrderState();
        var serverTime =
            order.ExchangeOrderTime.ToPaytmTime() ??
            order.LastUpdatedTime.ToPaytmTime() ??
            order.OrderDateTime.ToPaytmTime() ??
            CurrentTime;
        var originalId = isLookup
            ? originId
            : transactionId != 0
                ? transactionId
                : originId;
        var fingerprint =
            $"{order.Status}:{order.DisplayStatus}:" +
            $"{order.RemainingQuantity}:" +
            $"{order.TradedQuantity}:" +
            $"{order.AveragePrice}:{order.Reason}";
        var fingerprintKey =
            $"{originalId}:{order.OrderNumber}";
        if (_orderFingerprints.TryGetValue(
                fingerprintKey, out var previous) &&
            previous == fingerprint)
        {
            _orderFills[order.OrderNumber] =
                order.TradedQuantity;
            return;
        }
        _orderFingerprints[fingerprintKey] = fingerprint;

        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = originalId,
            TransactionId =
                isLookup ? transactionId : 0,
            OrderStringId = order.OrderNumber,
            SecurityId = await CreateSecurityId(
                order.Exchange,
                order.Segment,
                order.SecurityId,
                order.DisplayName,
                order.Isin,
                order.InstrumentType.IsEmpty(order.Instrument),
                cancellationToken),
            PortfolioName = _portfolioName,
            OrderType = order.OrderType.ToOrderType(),
            Side = order.TransactionType.ToSide(),
            TimeInForce =
                order.Validity.ToTimeInForce(),
            OrderPrice = order.Price,
            OrderVolume = order.Quantity,
            Balance = order.RemainingQuantity,
            AveragePrice = order.AveragePrice,
            OrderState = state,
            ServerTime = serverTime,
            Condition = new PaytmMoneyOrderCondition
            {
                Product = order.Product.ToProduct(),
                TriggerPrice = order.TriggerPrice > 0
                    ? order.TriggerPrice
                    : null,
                AfterMarket = order.OffMarket.ToBoolean(),
                LegNumber = order.LegNumber,
                AlgoOrderNumber = order.AlgoOrderNumber,
            },
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    order.Reason.IsEmpty(order.ErrorCode))
                : null,
        }, cancellationToken);
        _orderFills[order.OrderNumber] =
            order.TradedQuantity;
    }

    private async ValueTask ProcessTrade(
        PaytmMoneyTrade trade,
        PaytmMoneyOrder order,
        long originId,
        CancellationToken cancellationToken)
    {
        if (trade == null)
            return;
        var tradeId = trade.TradeNumber.IsEmpty(
            $"{order.OrderNumber}:" +
            $"{trade.ExchangeTradeTime}:" +
            trade.Quantity.ToString(
                CultureInfo.InvariantCulture));
        var seenKey = $"{originId}:{tradeId}";
        if (!_tradeIds.TryAdd(seenKey))
            return;

        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            OriginalTransactionId = originId,
            OrderStringId = order.OrderNumber,
            TradeStringId = tradeId,
            SecurityId = await CreateSecurityId(
                order.Exchange,
                order.Segment,
                order.SecurityId,
                order.DisplayName,
                order.Isin,
                order.InstrumentType.IsEmpty(order.Instrument),
                cancellationToken),
            PortfolioName = _portfolioName,
            Side = order.TransactionType.ToSide(),
            TradePrice = trade.Price,
            TradeVolume = trade.Quantity,
            ServerTime =
                trade.ExchangeTradeTime.ToPaytmTime() ??
                trade.ExchangeOrderTime.ToPaytmTime() ??
                CurrentTime,
        }, cancellationToken);
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
            _portfolioSubscriptionId = 0;
            return;
        }

        await SendOutMessageAsync(new PortfolioMessage
        {
            OriginalTransactionId =
                lookupMsg.TransactionId,
            PortfolioName = _portfolioName,
            BoardCode = "NSE_EQ",
        }, cancellationToken);
        _portfolioSubscriptionId =
            lookupMsg.TransactionId;
        await SendPortfolioSnapshot(cancellationToken);
        if (lookupMsg.IsHistoryOnly())
            _portfolioSubscriptionId = 0;
        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }

    private async ValueTask SendPortfolioSnapshot(
        CancellationToken cancellationToken)
    {
        var originId = _portfolioSubscriptionId;
        var funds = await _restClient.GetFunds(
            cancellationToken);
        if (funds != null)
        {
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originId,
                    PortfolioName = _portfolioName,
                    SecurityId = SecurityId.Money,
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.BeginValue,
                    funds.OpeningBalance,
                    true)
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    funds.TradeBalance,
                    true)
                .TryAdd(
                    PositionChangeTypes.BlockedValue,
                    funds.UtilizedAmount,
                    true),
                cancellationToken);
        }

        foreach (var position in await _restClient
            .GetPositions(cancellationToken))
        {
            if (position == null ||
                position.SecurityId.IsEmpty() ||
                position.Exchange.IsEmpty())
            {
                continue;
            }
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originId,
                    PortfolioName = _portfolioName,
                    SecurityId = await CreateSecurityId(
                        position.Exchange,
                        position.Segment,
                        position.SecurityId,
                        position.DisplayName,
                        position.Isin,
                        position.InstrumentType.IsEmpty(
                            position.Instrument),
                        cancellationToken),
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    position.NetQuantity,
                    true)
                .TryAdd(
                    PositionChangeTypes.AveragePrice,
                    position.NetAverage != 0
                        ? position.NetAverage
                        : position.CostPrice,
                    true)
                .TryAdd(
                    PositionChangeTypes.CurrentPrice,
                    position.LastPrice,
                    true)
                .TryAdd(
                    PositionChangeTypes.RealizedPnL,
                    position.RealizedProfit,
                    true),
                cancellationToken);
        }

        foreach (var holding in await _restClient
            .GetHoldings(cancellationToken))
        {
            if (holding == null)
                continue;
            var exchange =
                holding.Exchange?.ToUpperInvariant();
            if (exchange is not ("NSE" or "BSE"))
            {
                exchange = !holding.NseSecurityId.IsEmpty()
                    ? "NSE"
                    : "BSE";
            }
            var securityId = exchange == "NSE"
                ? holding.NseSecurityId
                : holding.BseSecurityId;
            var symbol = exchange == "NSE"
                ? holding.NseSymbol
                : holding.BseSymbol;
            if (securityId.IsEmpty())
                continue;
            var quantity =
                holding.Quantity.ToDecimalInvariant();
            var cost =
                holding.CostPrice.ToDecimalInvariant();
            var last =
                holding.LastPrice.ToDecimalInvariant();

            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originId,
                    PortfolioName = _portfolioName,
                    SecurityId = await CreateSecurityId(
                        exchange,
                        "E",
                        securityId,
                        symbol,
                        holding.Isin,
                        "EQUITY",
                        cancellationToken),
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    quantity,
                    true)
                .TryAdd(
                    PositionChangeTypes.AveragePrice,
                    cost,
                    true)
                .TryAdd(
                    PositionChangeTypes.CurrentPrice,
                    last,
                    true)
                .TryAdd(
                    PositionChangeTypes.UnrealizedPnL,
                    quantity is decimal qty &&
                    cost is decimal average &&
                    last is decimal price
                        ? (price - average) * qty
                        : null,
                    true),
                cancellationToken);
        }
    }

    private async Task<PaytmMoneyOrder> ResolveOrder(
        string orderId,
        CancellationToken cancellationToken)
    {
        if (_orderCache.TryGetValue(
            orderId, out var order))
        {
            return order;
        }

        foreach (var item in await _restClient
            .GetOrders(cancellationToken))
        {
            if (item?.OrderNumber.IsEmpty() != false)
                continue;
            _orderCache[item.OrderNumber] = item;
        }

        return _orderCache.TryGetValue(orderId, out order)
            ? order
            : throw new InvalidOperationException(
                $"Paytm Money order '{orderId}' was not found.");
    }

    private long ParseTransactionId(
        string remarks, string orderId)
    {
        if (long.TryParse(
            remarks,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var transactionId))
        {
            _orderTransactions[orderId] = transactionId;
        }
        else
        {
            _orderTransactions.TryGetValue(
                orderId, out transactionId);
        }
        return transactionId;
    }

    private async Task<SecurityId> CreateSecurityId(
        string exchange,
        string segment,
        string securityId,
        string symbol,
        string isin,
        string instrumentType,
        CancellationToken cancellationToken)
    {
        var instrument = await _restClient.FindInstrument(
            exchange,
            securityId,
            isin,
            cancellationToken);
        if (instrument != null)
            return instrument.ToSecurityId();

        var scripType =
            InferScripType(instrumentType, segment);
        segment = NormalizeSegment(segment, scripType);
        return new()
        {
            SecurityCode = symbol.IsEmpty(securityId),
            BoardCode = exchange.ToBoardCode(
                segment, scripType),
            Native = PaytmMoneyExtensions.CreateInstrumentKey(
                exchange,
                segment,
                securityId,
                scripType,
                scripType switch
                {
                    "INDEX" => "I",
                    "ETF" => "ETF",
                    "FUTURE" => "FUTSTK",
                    "OPTION" => "OPTSTK",
                    _ => "ES",
                }),
        };
    }

    private static string InferScripType(
        string instrumentType, string segment)
    {
        var value = instrumentType?.ToUpperInvariant();
        if (value?.Contains("INDEX") == true ||
            value == "I" || segment.EqualsIgnoreCase("I"))
        {
            return "INDEX";
        }
        if (value?.Contains("ETF") == true)
            return "ETF";
        if (value?.Contains("OPT") == true)
            return "OPTION";
        if (value?.Contains("FUT") == true)
            return "FUTURE";
        return "EQUITY";
    }

    private static string NormalizeSegment(
        string segment, string scripType)
    {
        segment = segment?.ToUpperInvariant();
        if (segment is "E" or "D" or "I")
            return segment;
        return scripType switch
        {
            "INDEX" => "I",
            "FUTURE" or "OPTION" => "D",
            _ => "E",
        };
    }

    private static string GetOrderId(
        string stringId,
        long? numericId,
        long originalTransactionId)
    {
        if (!stringId.IsEmpty())
            return stringId;
        if (numericId is long value)
        {
            return value.ToString(
                CultureInfo.InvariantCulture);
        }
        throw new InvalidOperationException(
            LocalizedStrings.OrderNoExchangeId.Put(
                originalTransactionId));
    }
}
