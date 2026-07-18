namespace StockSharp.WhiteBit;

public partial class WhiteBitMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var symbol = regMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(regMsg.SecurityId.SecurityCode));
        var boardCode = ResolveBoardCode(regMsg.SecurityId);
        var isCollateral = IsCollateralBoard(boardCode);
        var volume = regMsg.Volume.Abs();
        if (volume <= 0)
            throw new InvalidOperationException("Order volume must be positive.");
        if (regMsg.TimeInForce == TimeInForce.MatchOrCancel)
            throw new NotSupportedException("WhiteBIT does not expose Fill-or-Kill for these order endpoints.");

        var condition = regMsg.Condition as WhiteBitOrderCondition;
        var clientOrderId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId);
        var positionSide = isCollateral && condition?.PositionSide is not WhiteBitPositionSides.Both
            ? condition.PositionSide
            : (WhiteBitPositionSides?)null;
        var reduceOnly = isCollateral
            ? condition?.IsReduceOnly == true || regMsg.PositionEffect == OrderPositionEffects.CloseOnly
            : (bool?)null;

        WhiteBitOrder order;
        switch (regMsg.OrderType ?? OrderTypes.Limit)
        {
            case OrderTypes.Limit:
                if (regMsg.Price <= 0)
                    throw new InvalidOperationException("Limit order price must be positive.");
                order = await RestClient.RegisterLimitOrderAsync(new WhiteBitLimitOrderRequest
                {
                    Market = symbol,
                    Amount = volume.ToWire(),
                    Side = regMsg.Side.ToNative(),
                    Price = regMsg.Price.ToWire(),
                    IsPostOnly = regMsg.PostOnly == true,
                    IsImmediateOrCancel = regMsg.TimeInForce == TimeInForce.CancelBalance,
                    ClientOrderId = clientOrderId,
                    PositionSide = positionSide,
                    IsReduceOnly = reduceOnly,
                }, isCollateral, cancellationToken);
                break;

            case OrderTypes.Market:
                if (regMsg.PostOnly == true)
                    throw new InvalidOperationException("Market order cannot be post-only.");
                order = await RestClient.RegisterMarketOrderAsync(new WhiteBitMarketOrderRequest
                {
                    Market = symbol,
                    Amount = volume.ToWire(),
                    Side = regMsg.Side.ToNative(),
                    ClientOrderId = clientOrderId,
                    PositionSide = positionSide,
                    IsReduceOnly = reduceOnly,
                }, isCollateral, cancellationToken);
                break;

            case OrderTypes.Conditional:
                if (condition?.ActivationPrice is not decimal activationPrice || activationPrice <= 0)
                    throw new InvalidOperationException("Conditional order requires a positive activation price.");
                if (condition.ClosePositionPrice is decimal closePrice)
                {
                    if (closePrice <= 0)
                        throw new InvalidOperationException("Conditional limit price must be positive.");
                    order = await RestClient.RegisterStopLimitOrderAsync(new WhiteBitStopLimitOrderRequest
                    {
                        Market = symbol,
                        Amount = volume.ToWire(),
                        Side = regMsg.Side.ToNative(),
                        Price = closePrice.ToWire(),
                        ActivationPrice = activationPrice.ToWire(),
                        ClientOrderId = clientOrderId,
                        PositionSide = positionSide,
                        IsReduceOnly = reduceOnly,
                    }, isCollateral, cancellationToken);
                }
                else
                {
                    order = await RestClient.RegisterStopMarketOrderAsync(new WhiteBitStopMarketOrderRequest
                    {
                        Market = symbol,
                        Amount = volume.ToWire(),
                        Side = regMsg.Side.ToNative(),
                        ActivationPrice = activationPrice.ToWire(),
                        ClientOrderId = clientOrderId,
                        PositionSide = positionSide,
                        IsReduceOnly = reduceOnly,
                    }, isCollateral, cancellationToken);
                }
                break;

            default:
                throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, 0));
        }

        await SendOrderAsync(order, boardCode, regMsg.TransactionId, regMsg.TransactionId,
            regMsg.OrderType, regMsg.Price, volume, regMsg.Condition,
            regMsg.PositionEffect, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        if (replaceMsg.OldOrderId is not long oldOrderId)
            throw new NotSupportedException("WhiteBIT order modification requires a numeric exchange order ID.");
        if (replaceMsg.OrderType is OrderTypes.Conditional)
            throw new NotSupportedException("WhiteBIT conditional orders cannot be modified in place.");

        var symbol = replaceMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(replaceMsg.SecurityId.SecurityCode));
        var boardCode = ResolveBoardCode(replaceMsg.SecurityId);
        var order = await RestClient.ModifyOrderAsync(new WhiteBitModifyOrderRequest
        {
            OrderId = oldOrderId,
            Market = symbol,
            Price = replaceMsg.Price > 0 ? replaceMsg.Price.ToWire() : null,
            Amount = replaceMsg.Volume > 0 ? replaceMsg.Volume.Abs().ToWire() : null,
            ClientOrderId = CreateClientOrderId(replaceMsg.TransactionId, replaceMsg.UserOrderId),
        }, cancellationToken);

        await SendOrderAsync(order, boardCode, replaceMsg.TransactionId, replaceMsg.TransactionId,
            replaceMsg.OrderType, replaceMsg.Price, replaceMsg.Volume.Abs(), replaceMsg.Condition,
            replaceMsg.PositionEffect, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        if (cancelMsg.OrderId is not long orderId)
            throw new NotSupportedException("WhiteBIT order cancellation requires a numeric exchange order ID.");

        var boardCode = ResolveBoardCode(cancelMsg.SecurityId);
        var order = await RestClient.CancelOrderAsync(new WhiteBitCancelOrderRequest
        {
            Market = cancelMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(cancelMsg.SecurityId.SecurityCode)),
            OrderId = orderId,
        }, IsCollateralBoard(boardCode) &&
            (cancelMsg.OrderType == OrderTypes.Conditional || cancelMsg.Condition is not null), cancellationToken);

        await SendOrderAsync(order, boardCode, 0, cancelMsg.TransactionId,
            cancelMsg.OrderType, 0m, 0m, cancelMsg.Condition, null, cancellationToken,
            OrderStates.Done);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var symbol = cancelMsg.SecurityId.SecurityCode;
        var boardCode = cancelMsg.SecurityId == default
            ? BoardCodes.WhiteBit
            : ResolveBoardCode(cancelMsg.SecurityId);

        if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
        {
            foreach (var position in await RestClient.GetPositionsAsync(symbol, cancellationToken) ?? [])
            {
                if (position?.Market.IsEmpty() != false)
                    continue;
                await RestClient.ClosePositionAsync(position.Market,
                    position.PositionSide == WhiteBitPositionSides.Both ? null : position.PositionSide,
                    cancellationToken);
                await SendPositionAsync(position, cancelMsg.TransactionId, cancellationToken, 0m);
            }
        }

        var orders = new List<WhiteBitOrder>();
        if (cancelMsg.IsStop is not true)
            orders.AddRange(await RestClient.GetOpenOrdersAsync(symbol, cancellationToken) ?? []);
        if (cancelMsg.IsStop is not false && (IsSectionEnabled(WhiteBitSections.Margin) || IsSectionEnabled(WhiteBitSections.Futures)))
            orders.AddRange(await RestClient.GetConditionalOrdersAsync(symbol, cancellationToken) ?? []);

        foreach (var order in orders.Where(item => item?.OrderId > 0))
        {
            if (cancelMsg.Side is Sides side && order.Side.ToStockSharp() != side)
                continue;
            var isConditional = IsConditional(order.Type);
            if (cancelMsg.IsStop is bool isStop && isConditional != isStop)
                continue;

            var orderBoard = InferBoardCode(order, boardCode);
            var canceled = await RestClient.CancelOrderAsync(new WhiteBitCancelOrderRequest
            {
                Market = order.Market,
                OrderId = order.OrderId,
            }, isConditional && IsCollateralBoard(orderBoard), cancellationToken);
            await SendOrderAsync(canceled, orderBoard, 0, cancelMsg.TransactionId,
                null, 0m, 0m, null, null, cancellationToken, OrderStates.Done);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
        EnsurePrivateReady();

        if (!lookupMsg.IsSubscribe)
        {
            _portfolioSubscriptionId = 0;
            return;
        }

        _portfolioSubscriptionId = lookupMsg.TransactionId;
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = _portfolioName,
            BoardCode = BoardCodes.WhiteBit,
            OriginalTransactionId = lookupMsg.TransactionId,
        }, cancellationToken);

        if (IsSectionEnabled(WhiteBitSections.Spot))
        {
            foreach (var balance in await RestClient.GetSpotBalancesAsync(cancellationToken) ?? [])
                await SendSpotBalanceAsync(balance, lookupMsg.TransactionId, cancellationToken);
        }

        if (IsSectionEnabled(WhiteBitSections.Margin) || IsSectionEnabled(WhiteBitSections.Futures))
        {
            foreach (var balance in await RestClient.GetMarginBalancesAsync(cancellationToken) ?? [])
                await SendMarginBalanceAsync(balance, lookupMsg.TransactionId, cancellationToken);
            foreach (var position in await RestClient.GetPositionsAsync(null, cancellationToken) ?? [])
                await SendPositionAsync(position, lookupMsg.TransactionId, cancellationToken);
        }

        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
        EnsurePrivateReady();

        if (!statusMsg.IsSubscribe)
        {
            _orderStatusSubscriptionId = 0;
            return;
        }

        var symbol = statusMsg.SecurityId.SecurityCode;
        var fallbackBoard = statusMsg.SecurityId == default
            ? null
            : ResolveBoardCode(statusMsg.SecurityId);
        var limit = (statusMsg.Count ?? 100).Min(100).Max(1).To<int>();
        var orders = new List<WhiteBitOrder>();
        orders.AddRange(await RestClient.GetOpenOrdersAsync(symbol, cancellationToken) ?? []);
        orders.AddRange(await RestClient.GetOrderHistoryAsync(symbol, limit, cancellationToken) ?? []);
        if (IsSectionEnabled(WhiteBitSections.Margin) || IsSectionEnabled(WhiteBitSections.Futures))
            orders.AddRange(await RestClient.GetConditionalOrdersAsync(symbol, cancellationToken) ?? []);

        foreach (var order in orders
            .Where(static item => item?.OrderId > 0)
            .GroupBy(static item => item.OrderId)
            .Select(static group => group.OrderByDescending(GetOrderTime).First())
            .Where(item => (statusMsg.From is null || GetOrderTime(item) >= statusMsg.From) &&
                (statusMsg.To is null || GetOrderTime(item) <= statusMsg.To))
            .OrderBy(GetOrderTime)
            .Take(limit))
        {
            await SendOrderAsync(order, InferBoardCode(order, fallbackBoard),
                ParseTransactionId(order.ClientOrderId), statusMsg.TransactionId,
                null, 0m, 0m, null, null, cancellationToken);
        }

        foreach (var trade in (await RestClient.GetExecutedHistoryAsync(symbol, limit, cancellationToken) ?? [])
            .Where(item => (statusMsg.From is null || item.Time.ToUtcTime() >= statusMsg.From) &&
                (statusMsg.To is null || item.Time.ToUtcTime() <= statusMsg.To))
            .OrderBy(static item => item.Time))
        {
            await SendUserTradeAsync(trade, statusMsg.TransactionId, cancellationToken);
        }

        _orderStatusSubscriptionId = statusMsg.TransactionId;
        await SendSubscriptionResultAsync(statusMsg, cancellationToken);
    }

    private async ValueTask OnSpotBalancesAsync(WhiteBitSpotBalance[] balances,
        CancellationToken cancellationToken)
    {
        if (_portfolioSubscriptionId == 0)
            return;
        foreach (var balance in balances ?? [])
            await SendSpotBalanceAsync(balance, _portfolioSubscriptionId, cancellationToken);
    }

    private async ValueTask OnMarginBalancesAsync(WhiteBitMarginBalanceUpdate[] balances,
        CancellationToken cancellationToken)
    {
        if (_portfolioSubscriptionId == 0)
            return;
        foreach (var balance in balances ?? [])
        {
            await SendMarginBalanceAsync(new WhiteBitMarginBalance
            {
                Asset = balance.Asset,
                Balance = balance.Balance,
                Borrow = balance.Borrow,
                AvailableWithoutBorrow = balance.AvailableWithoutBorrow,
                AvailableWithBorrow = balance.AvailableWithBorrow,
            }, _portfolioSubscriptionId, cancellationToken);
        }
    }

    private ValueTask OnPendingOrderAsync(int eventId, WhiteBitOrder order,
        CancellationToken cancellationToken)
        => _orderStatusSubscriptionId == 0 || order is null
            ? default
            : SendOrderAsync(order, InferBoardCode(order, null), ParseTransactionId(order.ClientOrderId),
                _orderStatusSubscriptionId, null, 0m, 0m, null, null,
                cancellationToken, eventId == 3 ? OrderStates.Done : null);

    private ValueTask OnExecutedOrderAsync(WhiteBitOrder order, CancellationToken cancellationToken)
        => _orderStatusSubscriptionId == 0 || order is null
            ? default
            : SendOrderAsync(order, InferBoardCode(order, null), ParseTransactionId(order.ClientOrderId),
                _orderStatusSubscriptionId, null, 0m, 0m, null, null,
                cancellationToken, OrderStates.Done);

    private ValueTask OnUserTradeAsync(WhiteBitUserTrade trade, CancellationToken cancellationToken)
        => _orderStatusSubscriptionId == 0 || trade is null
            ? default
            : SendUserTradeAsync(trade, _orderStatusSubscriptionId, cancellationToken);

    private async ValueTask OnPositionsAsync(WhiteBitPosition[] positions,
        CancellationToken cancellationToken)
    {
        if (_portfolioSubscriptionId == 0)
            return;
        foreach (var position in positions ?? [])
            await SendPositionAsync(position, _portfolioSubscriptionId, cancellationToken);
    }

    private ValueTask SendOrderAsync(WhiteBitOrder order, string fallbackBoardCode,
        long transactionId, long originalTransactionId, OrderTypes? fallbackType, decimal fallbackPrice,
        decimal fallbackVolume, OrderCondition fallbackCondition, OrderPositionEffects? fallbackPositionEffect,
        CancellationToken cancellationToken,
        OrderStates? forcedState = null)
    {
        if (order is null)
            throw new InvalidDataException("WhiteBIT returned an empty order.");

        var boardCode = InferBoardCode(order, fallbackBoardCode);
        using (_sync.EnterScope())
        {
            if (order.OrderId > 0)
                _orderBoards[order.OrderId] = boardCode;
        }

        var left = order.Left.ToDecimal();
        var condition = IsConditional(order.Type) || !order.ActivationPrice.IsEmpty()
            ? new WhiteBitOrderCondition
            {
                ActivationPrice = order.ActivationPrice.ToDecimal(),
                ClosePositionPrice = order.Type is WhiteBitOrderTypes.StopLimit or WhiteBitOrderTypes.CollateralStopLimit
                    ? order.Price.ToDecimal()
                    : null,
                PositionSide = order.PositionSide,
                IsReduceOnly = order.IsReduceOnly,
            }
            : null;

        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = ToSecurityId(order.Market, boardCode),
            ServerTime = GetOrderTime(order),
            PortfolioName = _portfolioName,
            Side = order.Side.ToStockSharp(),
            OrderVolume = order.Amount.ToDecimal() ?? fallbackVolume,
            Balance = left ?? fallbackVolume,
            OrderPrice = order.Price.ToDecimal() ?? fallbackPrice,
            OrderType = ToOrderType(order.Type) ?? fallbackType ?? OrderTypes.Limit,
            OrderState = forcedState ?? (order.IsHistory ? OrderStates.Done : order.Status.ToStockSharp(left)),
            OrderId = order.OrderId,
            OrderStringId = order.ClientOrderId,
            TransactionId = transactionId,
            OriginalTransactionId = originalTransactionId,
            TimeInForce = order.IsImmediateOrCancel ? TimeInForce.CancelBalance : TimeInForce.PutInQueue,
            PostOnly = order.IsPostOnly,
            Condition = condition ?? fallbackCondition,
            PositionEffect = order.IsReduceOnly ? OrderPositionEffects.CloseOnly : fallbackPositionEffect,
        }, cancellationToken);
    }

    private ValueTask SendUserTradeAsync(WhiteBitUserTrade trade, long originalTransactionId,
        CancellationToken cancellationToken)
    {
        string boardCode;
        using (_sync.EnterScope())
            boardCode = _orderBoards.TryGetValue(trade.OrderId, out var current)
                ? current
                : InferMarketBoardCodeUnsafe(trade.Market, false);

        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = ToSecurityId(trade.Market, boardCode),
            ServerTime = trade.Time.ToUtcTime(),
            PortfolioName = _portfolioName,
            Side = trade.Side.ToStockSharp(),
            OrderId = trade.OrderId,
            TradeId = trade.TradeId,
            TradePrice = trade.Price.ToDecimal(),
            TradeVolume = trade.Amount.ToDecimal(),
            Commission = trade.Fee.ToDecimal(),
            CommissionCurrency = trade.FeeAsset,
            OriginalTransactionId = originalTransactionId,
        }, cancellationToken);
    }

    private ValueTask SendSpotBalanceAsync(WhiteBitSpotBalance balance, long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (balance?.Asset.IsEmpty() != false)
            return default;
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = _portfolioName,
            SecurityId = ToSecurityId(balance.Asset, BoardCodes.WhiteBit),
            ServerTime = CurrentTime,
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, balance.Available.ToDecimal(), true)
        .TryAdd(PositionChangeTypes.BlockedValue, balance.Frozen.ToDecimal(), true), cancellationToken);
    }

    private ValueTask SendMarginBalanceAsync(WhiteBitMarginBalance balance, long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (balance?.Asset.IsEmpty() != false)
            return default;
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = _portfolioName,
            SecurityId = ToSecurityId(balance.Asset, BoardCodes.WhiteBitMargin),
            ServerTime = CurrentTime,
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, balance.Balance.ToDecimal(), true)
        .TryAdd(PositionChangeTypes.BlockedValue, balance.Borrow.ToDecimal(), true),
        cancellationToken);
    }

    private ValueTask SendPositionAsync(WhiteBitPosition position, long originalTransactionId,
        CancellationToken cancellationToken, decimal? forcedValue = null)
    {
        if (position?.Market.IsEmpty() != false)
            return default;
        var amount = forcedValue ?? position.Amount.ToDecimal();
        var boardCode = InferPositionBoardCode(position.Market);
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = _portfolioName,
            SecurityId = ToSecurityId(position.Market, boardCode),
            ServerTime = position.ModifyDate > 0 ? position.ModifyDate.ToUtcTime() : CurrentTime,
            OriginalTransactionId = originalTransactionId,
            Side = position.PositionSide switch
            {
                WhiteBitPositionSides.Long => Sides.Buy,
                WhiteBitPositionSides.Short => Sides.Sell,
                _ => amount < 0 ? Sides.Sell : Sides.Buy,
            },
        }
        .TryAdd(PositionChangeTypes.CurrentValue, amount, true)
        .TryAdd(PositionChangeTypes.AveragePrice, position.BasePrice.ToDecimal(), true)
        .TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL.ToDecimal() ?? position.PnL.ToDecimal(), true)
        .TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL.ToDecimal(), true)
        .TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice.ToDecimal(), true),
        cancellationToken);
    }

    private string InferBoardCode(WhiteBitOrder order, string fallbackBoardCode)
    {
        if (!fallbackBoardCode.IsEmpty())
            return fallbackBoardCode;

        using (_sync.EnterScope())
        {
            if (_orderBoards.TryGetValue(order.OrderId, out var boardCode))
                return boardCode;
            return InferMarketBoardCodeUnsafe(order.Market,
                order.Type is WhiteBitOrderTypes.CollateralLimit or WhiteBitOrderTypes.CollateralMarket or
                    WhiteBitOrderTypes.CollateralStopLimit or WhiteBitOrderTypes.CollateralTriggerMarket);
        }
    }

    private string InferPositionBoardCode(string market)
    {
        using (_sync.EnterScope())
            return InferMarketBoardCodeUnsafe(market, true);
    }

    private string InferMarketBoardCodeUnsafe(string market, bool isCollateral)
    {
        if (_marketBoards.TryGetValue(market, out var boardCode) && boardCode.EqualsIgnoreCase(BoardCodes.WhiteBitFutures))
            return boardCode;
        if (market?.EndsWith("_PERP", StringComparison.OrdinalIgnoreCase) == true)
            return BoardCodes.WhiteBitFutures;
        return isCollateral ? BoardCodes.WhiteBitMargin : BoardCodes.WhiteBit;
    }

    private static DateTime GetOrderTime(WhiteBitOrder order)
        => order.ModifiedTime > 0 ? order.ModifiedTime.ToUtcTime()
            : order.FinishedTime > 0 ? order.FinishedTime.ToUtcTime()
            : order.Timestamp > 0 ? order.Timestamp.ToUtcTime()
            : DateTime.UtcNow;

    private static bool IsConditional(WhiteBitOrderTypes type)
        => type is WhiteBitOrderTypes.StopLimit or WhiteBitOrderTypes.StopMarket or
            WhiteBitOrderTypes.CollateralStopLimit or WhiteBitOrderTypes.CollateralTriggerMarket;

    private static OrderTypes? ToOrderType(WhiteBitOrderTypes type)
        => type switch
        {
            WhiteBitOrderTypes.Limit or WhiteBitOrderTypes.CollateralLimit => OrderTypes.Limit,
            WhiteBitOrderTypes.Market or WhiteBitOrderTypes.MarketStock or WhiteBitOrderTypes.CollateralMarket => OrderTypes.Market,
            WhiteBitOrderTypes.StopLimit or WhiteBitOrderTypes.StopMarket or
                WhiteBitOrderTypes.CollateralStopLimit or WhiteBitOrderTypes.CollateralTriggerMarket => OrderTypes.Conditional,
            _ => null,
        };
}
