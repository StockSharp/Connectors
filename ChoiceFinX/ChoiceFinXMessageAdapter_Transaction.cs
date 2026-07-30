namespace StockSharp.ChoiceFinX;

public partial class ChoiceFinXMessageAdapter
{
    private readonly SynchronizedDictionary<string, long>
        _orderTransactions =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<long, string>
        _transactionOrders = [];
    private readonly SynchronizedDictionary<string, string>
        _orderFingerprints =
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
        var instrument = await ResolveInstrument(
            regMsg.SecurityId.ToInstrumentKey(),
            cancellationToken);
        var condition =
            regMsg.Condition as ChoiceFinXOrderCondition;
        var request = CreateOrderRequest(
            regMsg,
            instrument,
            condition,
            DefaultProduct,
            PriceDivisor,
            ModeType,
            Mode,
            DeviceId);
        var orderId = await _restClient.PlaceOrder(
            request, cancellationToken);
        RememberOrder(orderId, regMsg.TransactionId);

        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                OriginalTransactionId =
                    regMsg.TransactionId,
                OrderStringId = orderId,
                SecurityId = regMsg.SecurityId,
                PortfolioName = _portfolioName,
                OrderType =
                    regMsg.OrderType ??
                    OrderTypes.Limit,
                Side = regMsg.Side,
                TimeInForce =
                    regMsg.TimeInForce ??
                    TimeInForce.PutInQueue,
                OrderPrice = regMsg.Price,
                OrderVolume = regMsg.Volume,
                Balance = regMsg.Volume,
                OrderState = OrderStates.Pending,
                ServerTime = CurrentTime,
                Condition = condition?.Clone() ??
                    new ChoiceFinXOrderCondition
                    {
                        Product = DefaultProduct,
                    },
            },
            cancellationToken);
    }

    internal static ChoiceFinXOrderRequest
        CreateOrderRequest(
        OrderRegisterMessage message,
        ChoiceFinXInstrument instrument,
        ChoiceFinXOrderCondition condition,
        ChoiceFinXProducts defaultProduct,
        decimal priceDivisor,
        string modeType,
        int? mode,
        string deviceId)
    {
        var orderType =
            message.OrderType ?? OrderTypes.Limit;
        ValidateOrderType(orderType);
        if (message.TimeInForce ==
            TimeInForce.MatchOrCancel)
        {
            throw new NotSupportedException(
                "Choice FinX does not document fill-or-kill orders.");
        }

        var triggerPrice = condition?.TriggerPrice;
        if (orderType == OrderTypes.Conditional &&
            triggerPrice is not > 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Choice FinX stop order.");
        }
        var isMarket =
            orderType == OrderTypes.Market ||
            orderType == OrderTypes.Conditional &&
            message.Price <= 0;
        if (!isMarket && message.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message.Price),
                message.Price,
                "A positive limit price is required.");
        }

        var quantity = ToQuantity(
            message.Volume,
            nameof(message.Volume),
            false);
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

        var hasTrigger = triggerPrice is > 0;
        var nativeOrderType = (hasTrigger, isMarket) switch
        {
            (true, true) => "SL_MKT",
            (true, false) => "SL_LIMIT",
            (false, true) => "RL_MKT",
            _ => "RL_LIMIT",
        };
        var afterMarket =
            condition?.IsAfterMarket == true;
        return new()
        {
            SegmentId = instrument.SegmentId,
            Token = instrument.Token,
            OrderType = nativeOrderType,
            Side = message.Side.ToNative(),
            Quantity = quantity,
            DisclosedQuantity = disclosed,
            Price = isMarket
                ? 0
                : ToNativePrice(
                    message.Price, priceDivisor),
            TriggerPrice = triggerPrice is > 0
                ? ToNativePrice(
                    triggerPrice.Value,
                    priceDivisor)
                : 0,
            Validity =
                message.TimeInForce.ToValidity(),
            ProductType =
                (condition?.Product ?? defaultProduct)
                    .ToProduct(afterMarket),
            IsEdisRequired =
                condition?.IsEdisRequired == true,
            Remarks = condition?.Remarks.IsEmpty(
                message.TransactionId.ToString(
                    CultureInfo.InvariantCulture)),
            ModeType = modeType,
            Mode = mode,
            DeviceId = deviceId,
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
                "Choice FinX does not document fill-or-kill orders.");
        }

        var current = await ResolveOrder(
            replaceMsg.OldOrderStringId,
            replaceMsg.OriginalTransactionId,
            cancellationToken);
        var instrument = await ResolveInstrument(
            current.SegmentId > 0 &&
            current.Token > 0
                ? ChoiceFinXExtensions
                    .CreateInstrumentKey(
                        current.SegmentId,
                        current.Token)
                : replaceMsg.SecurityId
                    .ToInstrumentKey(),
            cancellationToken);
        var condition =
            replaceMsg.Condition as
                ChoiceFinXOrderCondition;
        var orderType =
            replaceMsg.OrderType ??
            current.OrderType.ToOrderType();
        ValidateOrderType(orderType);
        var triggerPrice =
            condition?.TriggerPrice ??
            (current.TriggerPrice > 0
                ? current.TriggerPrice
                : null);
        var isMarket =
            orderType == OrderTypes.Market ||
            orderType == OrderTypes.Conditional &&
            replaceMsg.Price <= 0;
        if (orderType == OrderTypes.Conditional &&
            triggerPrice is not > 0)
        {
            throw new InvalidOperationException(
                "A positive trigger price is required for a Choice FinX stop order.");
        }
        if (!isMarket && replaceMsg.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replaceMsg.Price),
                replaceMsg.Price,
                "A positive limit price is required.");
        }

        var totalQuantity = ToQuantity(
            replaceMsg.Volume,
            nameof(replaceMsg.Volume),
            false);
        var tradedQuantity = ToQuantity(
            current.TradedQuantity,
            nameof(current.TradedQuantity),
            true);
        var pendingQuantity =
            Math.Max(0, totalQuantity - tradedQuantity);
        if (pendingQuantity == 0)
        {
            throw new InvalidOperationException(
                "A Choice FinX replacement must leave a positive pending quantity.");
        }
        var disclosed = ToQuantity(
            condition?.DisclosedVolume ??
                current.DisclosedQuantity,
            nameof(condition.DisclosedVolume),
            true);
        if (disclosed > pendingQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(condition.DisclosedVolume),
                disclosed,
                "Disclosed quantity cannot exceed pending quantity.");
        }
        var hasTrigger = triggerPrice is > 0;
        var afterMarket =
            condition?.IsAfterMarket == true ||
            current.ProductType is "AM" or "AD";

        var result = await _restClient.ModifyOrder(
            new ChoiceFinXModifyOrderRequest
            {
                ClientOrderNo = current.OrderId,
                TradedQuantity = tradedQuantity,
                ModeType = ModeType,
                SegmentId = instrument.SegmentId,
                Token = instrument.Token,
                OrderType =
                    (hasTrigger, isMarket) switch
                    {
                        (true, true) => "SL_MKT",
                        (true, false) => "SL_LIMIT",
                        (false, true) => "RL_MKT",
                        _ => "RL_LIMIT",
                    },
                Side = current.Side is 1 or 2
                    ? current.Side
                    : replaceMsg.Side.ToNative(),
                Quantity = pendingQuantity,
                DisclosedQuantity = disclosed,
                Price = isMarket
                    ? 0
                    : ToNativePrice(
                        replaceMsg.Price,
                        PriceDivisor),
                TriggerPrice =
                    triggerPrice is > 0
                        ? ToNativePrice(
                            triggerPrice.Value,
                            PriceDivisor)
                        : 0,
                Validity =
                    replaceMsg.TimeInForce.ToValidity(),
                ProductType =
                    (condition?.Product ??
                        current.ProductType.ToProduct())
                    .ToProduct(afterMarket),
                IsEdisRequired =
                    condition?.IsEdisRequired == true,
                Remarks =
                    condition?.Remarks.IsEmpty(
                        replaceMsg.TransactionId.ToString(
                            CultureInfo.InvariantCulture)),
                Mode = Mode,
                DeviceId = DeviceId,
            },
            cancellationToken);
        RememberOrder(
            result.IsEmpty(current.OrderId),
            replaceMsg.TransactionId);
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
            new ChoiceFinXCancelOrderRequest
            {
                ClientOrderNo = current.OrderId,
                SegmentId = current.SegmentId,
                ModeType = ModeType,
                Mode = Mode,
                DeviceId = DeviceId,
            },
            cancellationToken);
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
                statusMsg.TransactionId,
                cancellationToken);
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
            (await _restClient.GetOrders(
                cancellationToken))
            .OrderBy(item =>
                item.OrderTime ??
                item.ModifiedTime ??
                DateTime.MinValue))
        {
            var time = order.OrderTime ??
                order.ModifiedTime ??
                DateTime.MinValue;
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

        foreach (var trade in await _restClient
            .GetTrades(cancellationToken))
        {
            await ProcessTrade(
                trade,
                originalTransactionId,
                cancellationToken);
        }
    }

    private async ValueTask ProcessOrder(
        ChoiceFinXOrder order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (order == null || order.OrderId.IsEmpty())
            return;

        var transactionId = ParseTransactionId(
            order.Remarks, order.OrderId);
        var state = order.Status.ToOrderState();
        var quantity = order.Quantity;
        var balance = order.PendingQuantity;
        if (balance <= 0 &&
            state is not
                OrderStates.Done and not
                OrderStates.Failed)
        {
            balance = Math.Max(
                0,
                quantity - order.TradedQuantity);
        }
        var originalId = isLookup
            ? originId
            : transactionId != 0
                ? transactionId
                : originId != 0
                    ? originId
                    : _orderStatusSubscriptionId;
        var fingerprint =
            $"{order.Status}:{balance}:" +
            $"{order.TradedQuantity}:" +
            $"{order.AveragePrice}:" +
            $"{order.RejectReason}";
        var fingerprintKey =
            $"{originalId}:{order.OrderId}";
        if (_orderFingerprints.TryGetValue(
                fingerprintKey,
                out var previous) &&
            previous == fingerprint)
        {
            return;
        }
        _orderFingerprints[fingerprintKey] =
            fingerprint;

        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                OriginalTransactionId =
                    originalId,
                TransactionId =
                    isLookup ? transactionId : 0,
                OrderStringId = order.OrderId,
                OrderBoardId =
                    order.ExchangeOrderId,
                SecurityId = GetSecurityId(
                    order.SegmentId,
                    order.Token,
                    order.Symbol),
                PortfolioName = _portfolioName,
                OrderType =
                    order.OrderType.ToOrderType(),
                Side = order.Side.ToSide(),
                TimeInForce =
                    order.Validity.ToTimeInForce(),
                OrderPrice = order.Price,
                OrderVolume = quantity,
                Balance = balance,
                AveragePrice =
                    Positive(order.AveragePrice),
                OrderState = state,
                ServerTime =
                    order.ModifiedTime ??
                    order.OrderTime ??
                    CurrentTime,
                Condition = new
                    ChoiceFinXOrderCondition
                {
                    Product =
                        order.ProductType.ToProduct(),
                    TriggerPrice =
                        Positive(order.TriggerPrice),
                    DisclosedVolume =
                        Positive(
                            order.DisclosedQuantity),
                    IsAfterMarket =
                        order.ProductType is
                            "AM" or "AD",
                    Remarks = order.Remarks,
                },
                Error = state == OrderStates.Failed
                    ? new InvalidOperationException(
                        order.RejectReason.IsEmpty(
                            $"Choice FinX order status: {order.Status}."))
                    : null,
            },
            cancellationToken);
    }

    private async ValueTask ProcessTrade(
        ChoiceFinXTrade trade,
        long originId,
        CancellationToken cancellationToken)
    {
        if (trade == null)
            return;
        var tradeId = trade.TradeId.IsEmpty(
            $"{trade.OrderId}:{trade.TradeTime}:" +
            $"{trade.Price}:{trade.Quantity}");
        var seenKey = $"{originId}:{tradeId}";
        if (!_tradeIds.TryAdd(seenKey))
            return;

        var transactionId =
            _orderTransactions.TryGetValue2(
                trade.OrderId) ?? 0;
        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                OriginalTransactionId =
                    originId != 0
                        ? originId
                        : transactionId != 0
                            ? transactionId
                            : _orderStatusSubscriptionId,
                TransactionId =
                    originId != 0
                        ? transactionId
                        : 0,
                OrderStringId = trade.OrderId,
                TradeStringId = tradeId,
                SecurityId = GetSecurityId(
                    trade.SegmentId,
                    trade.Token,
                    trade.Symbol),
                PortfolioName = _portfolioName,
                Side = trade.Side.ToSide(),
                TradePrice = trade.Price,
                TradeVolume = trade.Quantity,
                ServerTime =
                    trade.TradeTime ?? CurrentTime,
            },
            cancellationToken);
    }

    private async ValueTask OnSocketOrderReceived(
        JObject root,
        CancellationToken cancellationToken)
    {
        var payload = NormalizeSocketPayload(
            ChoiceFinXSocketClient.GetPayload(root));
        var order =
            ChoiceFinXRestClient.ParseOrder(
                payload, true);
        if (!order.OrderId.IsEmpty() &&
            (_orderStatusSubscriptionId != 0 ||
                _orderTransactions.ContainsKey(
                    order.OrderId)))
        {
            await ProcessOrder(
                order,
                _orderStatusSubscriptionId,
                false,
                cancellationToken);
        }
    }

    private async ValueTask OnSocketTradeReceived(
        JObject root,
        CancellationToken cancellationToken)
    {
        var payload = NormalizeSocketPayload(
            ChoiceFinXSocketClient.GetPayload(root));
        var trade =
            ChoiceFinXRestClient.ParseTrade(
                payload, true);
        await ProcessTrade(
            trade, 0, cancellationToken);
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
        await SendOutMessageAsync(
            new PortfolioMessage
            {
                OriginalTransactionId =
                    lookupMsg.TransactionId,
                PortfolioName = _portfolioName,
                BoardCode = "NSE",
            },
            cancellationToken);
        await SendPortfolioSnapshot(
            lookupMsg.TransactionId,
            cancellationToken);
        if (lookupMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                lookupMsg.TransactionId,
                cancellationToken);
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
        var funds = await _restClient.GetFunds(
            cancellationToken);
        await SendOutMessageAsync(
            new PositionChangeMessage
            {
                OriginalTransactionId =
                    originalTransactionId,
                PortfolioName = _portfolioName,
                SecurityId = SecurityId.Money,
                ServerTime = CurrentTime,
            }
            .TryAdd(
                PositionChangeTypes.BeginValue,
                NonZero(funds.OpeningBalance),
                true)
            .TryAdd(
                PositionChangeTypes.CurrentValue,
                NonZero(
                    funds.AvailableBalance,
                    funds.CurrentBalance),
                true)
            .TryAdd(
                PositionChangeTypes.BlockedValue,
                NonZero(funds.UtilizedAmount),
                true),
            cancellationToken);

        foreach (var position in await _restClient
            .GetPositions(cancellationToken))
        {
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId =
                        originalTransactionId,
                    PortfolioName = _portfolioName,
                    SecurityId = GetSecurityId(
                        position.SegmentId,
                        position.Token,
                        position.Symbol),
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    position.NetQuantity,
                    true)
                .TryAdd(
                    PositionChangeTypes.AveragePrice,
                    Positive(position.AveragePrice),
                    true)
                .TryAdd(
                    PositionChangeTypes.CurrentPrice,
                    Positive(position.LastPrice),
                    true)
                .TryAdd(
                    PositionChangeTypes.RealizedPnL,
                    position.RealizedPnL,
                    true)
                .TryAdd(
                    PositionChangeTypes.UnrealizedPnL,
                    position.UnrealizedPnL,
                    true),
                cancellationToken);
        }

        foreach (var holding in await _restClient
            .GetHoldings(cancellationToken))
        {
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId =
                        originalTransactionId,
                    PortfolioName = _portfolioName,
                    SecurityId = GetSecurityId(
                        holding.SegmentId,
                        holding.Token,
                        holding.Symbol),
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    holding.Quantity,
                    true)
                .TryAdd(
                    PositionChangeTypes.BlockedValue,
                    Positive(
                        holding.BlockedQuantity),
                    true)
                .TryAdd(
                    PositionChangeTypes.AveragePrice,
                    Positive(
                        holding.AveragePrice),
                    true)
                .TryAdd(
                    PositionChangeTypes.CurrentPrice,
                    Positive(holding.LastPrice),
                    true),
                cancellationToken);
        }
    }

    private async Task<ChoiceFinXOrder> ResolveOrder(
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

        var order = (await _restClient.GetOrders(
                cancellationToken))
            .FirstOrDefault(item =>
                item.OrderId.EqualsIgnoreCase(orderId) ||
                item.ExchangeOrderId
                    .EqualsIgnoreCase(orderId));
        return order ??
            throw new InvalidOperationException(
                $"Choice FinX order '{orderId}' was not found in the current order book.");
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
            RememberOrder(orderId, transactionId);
        }
        else
        {
            _orderTransactions.TryGetValue(
                orderId, out transactionId);
        }
        return transactionId;
    }

    private void RememberOrder(
        string orderId, long transactionId)
    {
        if (orderId.IsEmpty() || transactionId == 0)
            return;
        _orderTransactions[orderId] = transactionId;
        _transactionOrders[transactionId] = orderId;
    }

    private SecurityId GetSecurityId(
        int segmentId, long token, string symbol)
    {
        if (segmentId > 0 && token > 0)
        {
            var key =
                ChoiceFinXExtensions
                    .CreateInstrumentKey(
                        segmentId, token);
            return _securityIds.TryGetValue2(key) ??
                segmentId.ToSecurityId(token, symbol);
        }
        return new()
        {
            SecurityCode = symbol.IsEmpty(
                token > 0
                    ? token.ToString(
                        CultureInfo.InvariantCulture)
                    : "UNKNOWN"),
            BoardCode = segmentId > 0
                ? segmentId.ToBoardCode()
                : "NSE",
        };
    }

    private void EnsurePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(
                _portfolioName))
        {
            throw new InvalidOperationException(
                LocalizedStrings.AccountNotFound);
        }
    }

    private static JObject NormalizeSocketPayload(
        JObject payload)
    {
        if (payload == null ||
            payload.GetInt(
                "SegmentId", "Segment") > 0)
        {
            return payload;
        }
        var exchange = payload.GetInt("Exchange");
        var text = string.Join(
            " ",
            payload.GetText(
                "InstrumentName", "Symbol"),
            payload.GetText("Series"),
            payload.GetText(
                "Option_Type", "OptionType"))
            .ToUpperInvariant();
        var segmentId = exchange switch
        {
            1 when text.Contains("FUT") ||
                text.Contains("OPT") => 2,
            1 => 1,
            2 when text.Contains("FUT") ||
                text.Contains("OPT") => 4,
            2 => 3,
            3 => 5,
            4 => 7,
            5 => 13,
            _ => 0,
        };
        if (segmentId > 0)
            payload["SegmentId"] = segmentId;
        return payload;
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
                "Choice FinX supports market, limit, stop-limit, and stop-market orders.");
        }
    }

    private static int ToQuantity(
        decimal value,
        string parameterName,
        bool allowZero)
    {
        if (value < 0 ||
            !allowZero && value == 0 ||
            value != decimal.Truncate(value) ||
            value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Choice FinX quantities must be whole numbers within Int32 range.");
        }
        return decimal.ToInt32(value);
    }

    private static long ToNativePrice(
        decimal value, decimal divisor)
    {
        if (value < 0 || divisor <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value,
                "Choice FinX prices and divisor must be non-negative.");
        }
        var scaled = decimal.Round(
            value * divisor,
            0,
            MidpointRounding.AwayFromZero);
        if (scaled > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value,
                "Choice FinX native price exceeds Int64 range.");
        }
        return decimal.ToInt64(scaled);
    }

    private static decimal? Positive(decimal value)
        => value > 0 ? value : null;

    private static decimal? NonZero(
        params decimal[] values)
    {
        foreach (var value in values)
        {
            if (value != 0)
                return value;
        }

        return null;
    }
}
