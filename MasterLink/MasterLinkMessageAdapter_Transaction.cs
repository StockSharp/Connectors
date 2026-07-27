namespace StockSharp.MasterLink;

public partial class MasterLinkMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(regMsg.PortfolioName);
        var security =
            regMsg.SecurityId.ParseMasterLinkSecurity();
        CacheSecurity(security);
        if (security.ToSecurityType() is not SecurityTypes.Stock and
            not SecurityTypes.Etf and not SecurityTypes.Warrant)
        {
            throw new NotSupportedException(
                "Taishin Nova trading supports Taiwan stocks, ETFs, and warrants.");
        }

        var orderType = regMsg.OrderType ?? OrderTypes.Limit;
        if (orderType is not OrderTypes.Limit and
            not OrderTypes.Market)
        {
            throw new NotSupportedException(
                "Taishin Nova supports limit and market orders through this adapter.");
        }

        var condition =
            regMsg.Condition as MasterLinkOrderCondition ?? new();
        var marketType =
            condition.MarketType.ToNative(regMsg.SecurityId);
        var priceType =
            condition.PriceType.ToNative(orderType);
        var timeInForce =
            regMsg.TimeInForce ?? TimeInForce.PutInQueue;
        ValidateOrderCombination(
            marketType, priceType, timeInForce);
        var quantity = ToOrderQuantity(
            regMsg.Volume, marketType, nameof(regMsg.Volume));
        if (priceType == "Limit" && regMsg.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regMsg.Price),
                regMsg.Price,
                "A positive price is required for a Taishin limit order.");
        }

        var response = await SafeClient().PlaceOrder(new()
        {
            BuySell = regMsg.Side == Sides.Buy ? "Buy" : "Sell",
            Symbol = security.Symbol,
            Price = priceType == "Limit"
                ? regMsg.Price.ToString(CultureInfo.InvariantCulture)
                : null,
            Quantity = quantity,
            MarketType = marketType,
            PriceType = priceType,
            TimeInForce = timeInForce.ToNative(),
            OrderType = condition.OrderType.ToNative(),
        }, cancellationToken);
        var orderNo = response?.OrderNo.ThrowIfEmpty(
            nameof(response.OrderNo));
        var tracker = new OrderTracker
        {
            TransactionId = regMsg.TransactionId,
            SecurityId = regMsg.SecurityId,
            PortfolioName =
                regMsg.PortfolioName.IsEmpty(PortfolioName),
            Side = regMsg.Side,
            OrderType = orderType,
            TimeInForce = timeInForce,
            Condition = condition,
        };
        _orders[orderNo] = tracker;
        _transactionOrders[regMsg.TransactionId] = orderNo;

        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = regMsg.TransactionId,
            TransactionId = regMsg.TransactionId,
            OrderStringId = orderNo,
            SecurityId = regMsg.SecurityId,
            PortfolioName = tracker.PortfolioName,
            OrderType = orderType,
            Side = regMsg.Side,
            TimeInForce = timeInForce,
            OrderPrice = regMsg.Price,
            OrderVolume = regMsg.Volume,
            Balance = regMsg.Volume,
            OrderState = OrderStates.Pending,
            ServerTime =
                MasterLinkExtensions.ParseMasterLinkTradeTime(
                    response.OrderDate.IsEmpty(response.WorkDate),
                    response.OrderTime) ??
                CurrentTime,
            Condition = condition,
        }, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(replaceMsg.PortfolioName);
        var orderNo = ResolveOrderNo(
            replaceMsg.OldOrderStringId,
            replaceMsg.OriginalTransactionId);
        var record = await FindOrder(
            orderNo, cancellationToken);
        var sequence = record.SeqNo;
        var changePrice =
            replaceMsg.Price > 0 &&
            replaceMsg.Price != record.OrderPrice;
        var currentBalance = Math.Max(
            0, record.OrgQty - record.FilledQty - record.CelQty);
        var changeVolume =
            replaceMsg.Volume > 0 &&
            replaceMsg.Volume != currentBalance;

        if (changePrice && changeVolume)
        {
            throw new NotSupportedException(
                "Taishin Nova modifies price and remaining quantity in separate requests.");
        }
        if (!changePrice && !changeVolume)
        {
            throw new InvalidOperationException(
                "Taishin replacement must change the price or remaining quantity.");
        }

        if (changePrice)
        {
            await SafeClient().ModifyPrice(
                orderNo,
                sequence,
                replaceMsg.Price,
                "Limit",
                cancellationToken);
        }
        else
        {
            var target = ToPositiveWholeNumber(
                replaceMsg.Volume, nameof(replaceMsg.Volume));
            var decrease = currentBalance - target;
            if (decrease <= 0)
            {
                throw new InvalidOperationException(
                    "Taishin Nova can only decrease an order's remaining quantity.");
            }
            await SafeClient().ModifyVolume(
                orderNo,
                sequence,
                ToIntQuantity(
                    decrease,
                    "Quantity decrease exceeds the Nova API limit."),
                cancellationToken);
        }

        if (_orders.TryGetValue(orderNo, out var previous))
        {
            var tracker = new OrderTracker
            {
                TransactionId = replaceMsg.TransactionId,
                SecurityId = previous.SecurityId,
                PortfolioName =
                    previous.PortfolioName.IsEmpty(
                        replaceMsg.PortfolioName)
                    .IsEmpty(PortfolioName),
                Side = previous.Side,
                OrderType = previous.OrderType,
                TimeInForce = previous.TimeInForce,
                Condition = previous.Condition,
            };
            _orders[orderNo] = tracker;
            if (!sequence.IsEmpty())
                _orders[sequence] = tracker;
        }
        _transactionOrders[replaceMsg.TransactionId] = orderNo;
        await ProcessOrder(
            await FindOrder(orderNo, cancellationToken),
            replaceMsg.TransactionId,
            false,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(cancelMsg.PortfolioName);
        var orderNo = ResolveOrderNo(
            cancelMsg.OrderStringId,
            cancelMsg.OriginalTransactionId);
        var record = await FindOrder(
            orderNo, cancellationToken);
        await SafeClient().CancelOrder(
            orderNo, record.SeqNo, cancellationToken);
        _transactionOrders[cancelMsg.TransactionId] = orderNo;
        await ProcessOrder(
            await FindOrder(orderNo, cancellationToken),
            cancelMsg.TransactionId,
            false,
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
            cancellationToken,
            statusMsg);
        if (statusMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                statusMsg.TransactionId, cancellationToken);
        }
        else
        {
            _orderStatusSubscriptionId = statusMsg.TransactionId;
            await SendSubscriptionResultAsync(
                statusMsg, cancellationToken);
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
                _portfolioFilter = null;
            }
            return;
        }

        await SendPortfolioSnapshot(
            lookupMsg.TransactionId,
            lookupMsg.PortfolioName,
            cancellationToken);
        if (lookupMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                lookupMsg.TransactionId, cancellationToken);
        }
        else
        {
            _portfolioSubscriptionId = lookupMsg.TransactionId;
            _portfolioFilter = lookupMsg.PortfolioName;
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
        }
    }

    private async ValueTask OnOrder(
        JToken data,
        CancellationToken cancellationToken)
    {
        foreach (var token in EnumeratePayload(data))
        {
            var ack = token.ToObject<MasterLinkOrderAck>();
            if (ack == null)
                continue;
            await ProcessOrder(new()
            {
                WorkDate = ack.WorkDate,
                OrderDate = ack.OrderDateTime,
                OrderNo = ack.OrderNo,
                Symbol = ack.Symbol,
                BuySell = ack.BuySell,
                MarketType = ack.MarketType,
                PriceType = ack.PriceType,
                TimeInForce = ack.TimeInForce,
                OrderType = ack.OrderType,
                OrderPrice = ack.OrderPrice,
                OrgQty = ack.OrgQty,
                FilledQty = ack.FilledQty,
                CelQty = ack.CelQty,
                CanCancel = ack.CanCancel,
                ErrCode = ack.ErrCode,
                ErrMsg = ack.ErrMsg,
                SeqNo = ack.OrderSeqNo,
                IsPreOrder = ack.IsPreOrder,
            }, 0, false, cancellationToken);
        }
    }

    private async ValueTask OnFill(
        JToken data,
        CancellationToken cancellationToken)
    {
        foreach (var token in EnumeratePayload(data))
        {
            await ProcessFill(
                token.ToObject<MasterLinkFill>(),
                0,
                cancellationToken);
        }
    }

    private async ValueTask SendOrderSnapshot(
        long originId,
        CancellationToken cancellationToken,
        OrderStatusMessage filter = null)
    {
        var symbol =
            filter?.SecurityId.SecurityCode;
        var skip = Math.Max(0, filter?.Skip ?? 0);
        var left = filter?.Count ?? long.MaxValue;
        var orders = (await SafeClient().GetOrders(
            symbol, "All", cancellationToken)) ?? [];
        foreach (var order in orders.OrderBy(GetOrderTime))
        {
            if (!MatchesOrderFilter(order, filter))
                continue;
            if (skip-- > 0)
                continue;
            await ProcessOrder(
                order, originId, true, cancellationToken);
            if (--left <= 0)
                break;
        }

        if (left > 0)
        {
            var fills = (await SafeClient().GetFills(
                symbol, cancellationToken)) ?? [];
            foreach (var fill in fills.OrderBy(GetFillTime))
            {
                if (!MatchesFillFilter(fill, filter))
                    continue;
                if (skip-- > 0)
                    continue;
                await ProcessFill(
                    fill, originId, cancellationToken);
                if (--left <= 0)
                    break;
            }
        }
        _lastOrderRefresh = CurrentTime;
    }

    private async ValueTask SendPortfolioSnapshot(
        long originId,
        string portfolioName,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(portfolioName);
        var name = PortfolioName;
        var snapshot = await SafeClient().GetPortfolio(
            cancellationToken);
        await SendOutMessageAsync(new PortfolioMessage
        {
            OriginalTransactionId = originId,
            PortfolioName = name,
            BoardCode = "TWSE",
        }, cancellationToken);

        var balances = snapshot?.BankBalances ?? [];
        var available =
            balances.Sum(item => item.AvailableBalance);
        var reserved =
            balances.Sum(item => item.ReservedAmount);
        var current =
            balances.Sum(item => item.DedicatedAccountBalance);
        if (current == 0 && (available != 0 || reserved != 0))
            current = available + reserved;
        var pnl = snapshot?.Pnl;
        await SendOutMessageAsync(new PositionChangeMessage
        {
            OriginalTransactionId = originId,
            PortfolioName = name,
            SecurityId = SecurityId.Money,
            ServerTime = CurrentTime,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, current, true)
        .TryAdd(
            PositionChangeTypes.BlockedValue,
            Math.Max(reserved, current - available),
            true)
        .TryAdd(
            PositionChangeTypes.UnrealizedPnL,
            pnl?.UnrealizedProfitLossTotal.ToNullableDecimal(),
            true)
        .TryAdd(
            PositionChangeTypes.RealizedPnL,
            pnl?.RealizedProfitLossTotal.ToNullableDecimal(),
            true)
        .TryAdd(
            PositionChangeTypes.Currency,
            CurrencyTypes.TWD),
            cancellationToken);

        foreach (var position in
            snapshot?.Inventory?.PositionSummaries ?? [])
        {
            if (position?.Symbol.IsEmpty() != false)
                continue;
            var security = ResolveSecurity(position.Symbol);
            await SendOutMessageAsync(new PositionChangeMessage
            {
                OriginalTransactionId = originId,
                PortfolioName = name,
                SecurityId = security.ToSecurityId(),
                ServerTime = CurrentTime,
            }
            .TryAdd(
                PositionChangeTypes.CurrentValue,
                position.CurrentQuantity.ToNullableDecimal(),
                true)
            .TryAdd(
                PositionChangeTypes.AveragePrice,
                position.AveragePrice.ToNullableDecimal(),
                true)
            .TryAdd(
                PositionChangeTypes.CurrentPrice,
                position.CurrentPrice.ToNullableDecimal(),
                true)
            .TryAdd(
                PositionChangeTypes.UnrealizedPnL,
                position.UnrealizedProfitLoss
                    .IsEmpty(position.UnrealizedProfit)
                    .ToNullableDecimal(),
                true)
            .TryAdd(
                PositionChangeTypes.RealizedPnL,
                position.RealizedProfit.ToNullableDecimal(),
                true)
            .TryAdd(
                PositionChangeTypes.BlockedValue,
                position.PledgeQuantity.ToNullableDecimal(),
                true)
            .TryAdd(
                PositionChangeTypes.Currency,
                CurrencyTypes.TWD),
                cancellationToken);
        }
        _lastPortfolioRefresh = CurrentTime;
    }

    private async ValueTask ProcessOrder(
        MasterLinkOrderRecord order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (order == null ||
            order.OrderNo.IsEmpty() && order.SeqNo.IsEmpty())
        {
            return;
        }
        var key = order.OrderNo.IsEmpty(order.SeqNo);
        _orders.TryGetValue(key, out var tracker);
        if (tracker == null && !order.SeqNo.IsEmpty())
            _orders.TryGetValue(order.SeqNo, out tracker);
        if (tracker != null)
            CacheOrderTracker(order, tracker);

        var securityId = tracker?.SecurityId ??
            ResolveSecurity(
                order.Symbol,
                order.Market,
                order.MarketType).ToSecurityId();
        var state = order.ToOrderState();
        var messageOrigin = isLookup
            ? originId
            : originId != 0
                ? originId
                : tracker?.TransactionId is > 0
                    ? tracker.TransactionId
                    : _orderStatusSubscriptionId;
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = messageOrigin,
            TransactionId =
                isLookup ? tracker?.TransactionId ?? 0 : 0,
            OrderStringId = key,
            SecurityId = securityId,
            PortfolioName =
                tracker?.PortfolioName.IsEmpty(PortfolioName),
            OrderType =
                tracker?.OrderType ??
                order.PriceType.ToOrderType(),
            Side = tracker?.Side ?? order.BuySell.ToSide(),
            TimeInForce =
                tracker?.TimeInForce ??
                order.TimeInForce.ToTimeInForce(),
            OrderPrice = order.OrderPrice,
            OrderVolume = order.OrgQty,
            Balance = Math.Max(
                0,
                order.OrgQty - order.FilledQty - order.CelQty),
            AveragePrice =
                order.AvgPrice is > 0
                    ? order.AvgPrice
                    : null,
            OrderState = state,
            ServerTime = GetOrderTime(order),
            Condition =
                tracker?.Condition ?? order.ToCondition(),
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    order.ErrMsg.IsEmpty(
                        $"Taishin order error {order.ErrCode}."))
                : null,
        }, cancellationToken);
    }

    private async ValueTask ProcessFill(
        MasterLinkFill fill,
        long originId,
        CancellationToken cancellationToken)
    {
        if (fill?.Symbol.IsEmpty() != false ||
            fill.FilledPrice <= 0 ||
            fill.FilledQty <= 0)
        {
            return;
        }
        var fillId = fill.MktSeqNo.IsEmpty(
            $"{fill.OrderNo}:{fill.FilledDate}:{fill.FilledTime}:{fill.FilledPrice}:{fill.FilledQty}");
        if (!_tradeIds.TryAdd($"{fill.OrderNo}|{fillId}"))
            return;
        _orders.TryGetValue(fill.OrderNo, out var tracker);
        if (tracker == null && !fill.OrderSeqNo.IsEmpty())
            _orders.TryGetValue(fill.OrderSeqNo, out tracker);
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            OriginalTransactionId = originId != 0
                ? originId
                : tracker?.TransactionId ??
                    _orderStatusSubscriptionId,
            OrderStringId = fill.OrderNo,
            TradeStringId = fillId,
            SecurityId = tracker?.SecurityId ??
                ResolveSecurity(
                    fill.Symbol,
                    fill.Market,
                    fill.MarketType).ToSecurityId(),
            PortfolioName =
                tracker?.PortfolioName
                    .IsEmpty(fill.Account)
                    .IsEmpty(PortfolioName),
            Side = tracker?.Side ?? fill.BuySell.ToSide(),
            TradePrice = fill.FilledPrice,
            TradeVolume = fill.FilledQty,
            ServerTime = GetFillTime(fill),
        }, cancellationToken);
    }

    private async Task<MasterLinkOrderRecord> FindOrder(
        string orderNo,
        CancellationToken cancellationToken)
    {
        var orders = await SafeClient().GetOrders(
            null, "All", cancellationToken);
        return orders?.FirstOrDefault(order =>
            order.OrderNo.EqualsIgnoreCase(orderNo) ||
            order.SeqNo.EqualsIgnoreCase(orderNo)) ??
            throw new InvalidOperationException(
                $"Taishin order '{orderNo}' was not found.");
    }

    private void CacheOrderTracker(
        MasterLinkOrderRecord order,
        OrderTracker tracker)
    {
        if (!order.OrderNo.IsEmpty())
            _orders[order.OrderNo] = tracker;
        if (!order.SeqNo.IsEmpty())
            _orders[order.SeqNo] = tracker;
        if (tracker.TransactionId > 0 &&
            !order.OrderNo.IsEmpty())
        {
            _transactionOrders[tracker.TransactionId] =
                order.OrderNo;
        }
    }

    private string ResolveOrderNo(
        string orderNo,
        long transactionId)
    {
        if (!orderNo.IsEmpty())
            return orderNo;
        if (_transactionOrders.TryGetValue(
            transactionId, out orderNo) &&
            !orderNo.IsEmpty())
        {
            return orderNo;
        }
        throw new InvalidOperationException(
            LocalizedStrings.OrderNoExchangeId.Put(transactionId));
    }

    private void EnsurePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(PortfolioName) &&
            !portfolioName.EqualsIgnoreCase(Account))
        {
            throw new InvalidOperationException(
                LocalizedStrings.AccountNotFound);
        }
    }

    private static void ValidateOrderCombination(
        string marketType,
        string priceType,
        TimeInForce timeInForce)
    {
        if (marketType == "Fixing")
        {
            if (priceType != "Reference" ||
                timeInForce != TimeInForce.PutInQueue)
            {
                throw new NotSupportedException(
                    "Taishin Fixing orders require Reference price and ROD.");
            }
        }
        else if (marketType is "IntradayOdd" or "Odd")
        {
            if (priceType == "Market" ||
                timeInForce != TimeInForce.PutInQueue)
            {
                throw new NotSupportedException(
                    "Taishin odd-lot orders do not support Market price, IOC, or FOK.");
            }
        }
        else if (marketType == "Emg")
        {
            if (priceType != "Limit" ||
                timeInForce != TimeInForce.PutInQueue)
            {
                throw new NotSupportedException(
                    "Taishin emerging-stock orders require Limit price and ROD.");
            }
        }
    }

    private static int ToOrderQuantity(
        decimal volume,
        string marketType,
        string parameterName)
    {
        var quantity = ToPositiveWholeNumber(
            volume, parameterName);
        if (quantity > 499000)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                volume,
                "Taishin Nova accepts at most 499000 shares per order.");
        }
        if (marketType is "IntradayOdd" or "Odd")
        {
            if (quantity > 999)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    volume,
                    "Taiwan odd-lot orders must contain between 1 and 999 shares.");
            }
        }
        else if (quantity < 1000 || quantity % 1000 != 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                volume,
                "Taiwan board-lot orders must be a positive multiple of 1000 shares.");
        }
        return ToIntQuantity(
            quantity,
            "Quantity exceeds the Nova API Int32 range.");
    }

    private static decimal ToPositiveWholeNumber(
        decimal value,
        string parameterName)
    {
        if (value <= 0 ||
            value != decimal.Truncate(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Taishin order quantities must be positive whole numbers.");
        }
        return value;
    }

    private static int ToIntQuantity(
        decimal value,
        string message)
    {
        if (value > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), value, message);
        return decimal.ToInt32(value);
    }

    private static bool MatchesOrderFilter(
        MasterLinkOrderRecord order,
        OrderStatusMessage filter)
    {
        if (filter == null)
            return true;
        var time = GetOrderTime(order);
        if (filter.From is DateTime from &&
            time < MasterLinkExtensions.NormalizeUtc(from))
        {
            return false;
        }
        if (filter.To is DateTime to &&
            time > MasterLinkExtensions.NormalizeUtc(to))
        {
            return false;
        }
        return true;
    }

    private bool MatchesFillFilter(
        MasterLinkFill fill,
        OrderStatusMessage filter)
    {
        if (filter == null)
            return true;
        var time = GetFillTime(fill);
        if (filter.From is DateTime from &&
            time < MasterLinkExtensions.NormalizeUtc(from))
        {
            return false;
        }
        if (filter.To is DateTime to &&
            time > MasterLinkExtensions.NormalizeUtc(to))
        {
            return false;
        }
        return filter.PortfolioName.IsEmpty() ||
            filter.PortfolioName.EqualsIgnoreCase(fill.Account) ||
            filter.PortfolioName.EqualsIgnoreCase(PortfolioName);
    }

    private static DateTime GetOrderTime(
        MasterLinkOrderRecord order)
        => MasterLinkExtensions.ParseMasterLinkTradeTime(
            order.ChgDate.IsEmpty(
                order.OrderDate).IsEmpty(order.WorkDate),
            order.ChgTime.IsEmpty(order.OrderTime)) ??
            CurrentTimeFallback();

    private static DateTime GetFillTime(MasterLinkFill fill)
        => MasterLinkExtensions.ParseMasterLinkTradeTime(
            fill.FilledDate,
            fill.FilledTime) ??
            CurrentTimeFallback();

    private static DateTime CurrentTimeFallback()
        => DateTime.UtcNow;

    private static IEnumerable<JToken> EnumeratePayload(
        JToken data)
    {
        if (data is JArray array)
        {
            foreach (var item in array)
                yield return item;
        }
        else if (data != null)
        {
            yield return data;
        }
    }
}
