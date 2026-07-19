namespace StockSharp.GmoCoin;

public partial class GmoCoinMessageAdapter
{
    private readonly record struct NativeOrderParameters(
        GmoCoinExecutionTypes ExecutionType, decimal? Price,
        GmoCoinTimeInForce TimeInForce, decimal Volume,
        GmoCoinOrderCondition Condition);

    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var market = GetMarket(regMsg.SecurityId);
        var parameters = CreateOrderParameters(market, regMsg);
        var condition = parameters.Condition;
        string result;
        if (condition.IsClosePosition)
        {
            result = await RestClient.CloseOrderAsync(new()
            {
                Symbol = market.Symbol,
                Side = regMsg.Side.ToGmoCoin(),
                ExecutionType = parameters.ExecutionType,
                TimeInForce = parameters.TimeInForce,
                Price = parameters.Price?.ToWire(),
                SettlementPositions =
                [
                    new GmoCoinSettlementPosition
                    {
                        PositionId = condition.PositionId.Value,
                        Size = parameters.Volume.ToWire(),
                    },
                ],
                IsCancelBefore = condition.IsCancelBefore ? true : null,
            }, cancellationToken);
        }
        else
        {
            result = await RestClient.PlaceOrderAsync(new()
            {
                Symbol = market.Symbol,
                Side = regMsg.Side.ToGmoCoin(),
                ExecutionType = parameters.ExecutionType,
                TimeInForce = parameters.TimeInForce,
                Price = parameters.Price?.ToWire(),
                LossCutPrice = condition.LossCutPrice?.ToWire(),
                Size = parameters.Volume.ToWire(),
                IsCancelBefore = condition.IsCancelBefore ? true : null,
            }, cancellationToken);
        }

        var orderId = ParseExchangeOrderId(result, "accepted order");
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            Symbol = market.Symbol,
            ExchangeOrderId = orderId,
            Side = regMsg.Side,
            OrderType = regMsg.OrderType ?? OrderTypes.Limit,
            Volume = parameters.Volume,
            Price = parameters.Price ?? 0m,
            IsPostOnly = parameters.TimeInForce == GmoCoinTimeInForce.StoreOrKill,
            TimeInForce = parameters.TimeInForce.ToStockSharp(),
            Condition = condition.Clone() as GmoCoinOrderCondition,
        };
        TrackOrder(tracked, result);
        await SendTrackedOrderAsync(tracked, OrderStates.Active, tracked.Volume,
            regMsg.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var orderId = ResolveOrderIdentifier(replaceMsg.OldOrderId,
            replaceMsg.OldOrderStringId, "replacement");
        var tracked = GetTrackedOrder(orderId);
        var market = replaceMsg.SecurityId.SecurityCode.IsEmpty()
            ? tracked is null
                ? throw new InvalidOperationException(
                    "GMO Coin replacement requires the order market when the order was not registered by this adapter.")
                : GetMarket(tracked.Symbol)
            : GetMarket(replaceMsg.SecurityId);
        if (replaceMsg.Price <= 0)
            throw new InvalidOperationException(
                "GMO Coin replacement price must be positive.");
        ValidateStep(replaceMsg.Price, market.TickSize, "price", market.Symbol);
        if (tracked is not null && replaceMsg.Volume != 0 &&
            replaceMsg.Volume.Abs() != tracked.Volume)
            throw new NotSupportedException(
                "GMO Coin changeOrder can change price and liquidation price, but not volume.");
        var condition = replaceMsg.Condition as GmoCoinOrderCondition;
        if (condition?.LossCutPrice is <= 0)
            throw new InvalidOperationException(
                "GMO Coin liquidation price must be positive.");

        await RestClient.ChangeOrderAsync(new()
        {
            OrderId = orderId,
            Price = replaceMsg.Price.ToWire(),
            LossCutPrice = condition?.LossCutPrice?.ToWire(),
        }, cancellationToken);

        tracked ??= new TrackedOrder
        {
            TransactionId = replaceMsg.TransactionId,
            Symbol = market.Symbol,
            ExchangeOrderId = orderId,
            Side = replaceMsg.Side,
            OrderType = replaceMsg.OrderType ?? OrderTypes.Limit,
            Volume = replaceMsg.Volume.Abs(),
            IsPostOnly = replaceMsg.PostOnly,
            TimeInForce = replaceMsg.TimeInForce,
            Condition = condition?.Clone() as GmoCoinOrderCondition,
        };
        tracked.Price = replaceMsg.Price;
        TrackOrder(tracked, orderId.ToString(CultureInfo.InvariantCulture));
        await SendTrackedOrderAsync(tracked, OrderStates.Active, tracked.Volume,
            replaceMsg.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var orderId = ResolveOrderIdentifier(cancelMsg.OrderId,
            cancelMsg.OrderStringId, "cancellation");
        await RestClient.CancelOrderAsync(new()
        {
            OrderId = orderId,
        }, cancellationToken);
        await SendCanceledOrderAsync(orderId, cancelMsg.TransactionId,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var markets = GetSelectedMarkets(cancelMsg.SecurityId);
        if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
        {
            foreach (var market in markets.Where(static value => value.IsMargin))
            {
                foreach (var position in await DownloadOpenPositionsAsync(market,
                    cancellationToken))
                {
                    if (cancelMsg.Side is Sides side &&
                        position.Side.ToStockSharp() != side)
                        continue;
                    _ = await RestClient.CloseOrderAsync(new()
                    {
                        Symbol = market.Symbol,
                        Side = position.Side.Opposite(),
                        ExecutionType = GmoCoinExecutionTypes.Market,
                        TimeInForce = GmoCoinTimeInForce.FillAndKill,
                        SettlementPositions =
                        [
                            new GmoCoinSettlementPosition
                            {
                                PositionId = position.PositionId,
                                Size = position.Size.ToWire(),
                            },
                        ],
                    }, cancellationToken);
                    await SendPositionAsync(position, cancelMsg.TransactionId,
                        cancellationToken, 0m);
                }
            }
        }

        if (cancelMsg.IsStop is null)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var canceled = await RestClient.CancelBulkOrderAsync(new()
                {
                    Symbols = [.. markets.Select(static value => value.Symbol)],
                    Side = cancelMsg.Side?.ToGmoCoin(),
                    IsDescending = false,
                }, cancellationToken) ?? [];
                foreach (var orderId in canceled)
                    await SendCanceledOrderAsync(orderId,
                        cancelMsg.TransactionId, cancellationToken);
                if (canceled.Length < 10)
                    break;
            }
            return;
        }

        var selected = new List<GmoCoinOrder>();
        foreach (var market in markets)
            selected.AddRange((await DownloadActiveOrdersAsync(market, 1000,
                cancellationToken)).Where(order =>
                    (order.ExecutionType == GmoCoinExecutionTypes.Stop) ==
                    cancelMsg.IsStop.Value &&
                    (cancelMsg.Side is null ||
                        order.Side.ToStockSharp() == cancelMsg.Side)));
        foreach (var chunk in selected.Chunk(10))
        {
            var result = await RestClient.CancelOrdersAsync(new()
            {
                OrderIds = [.. chunk.Select(static order => order.OrderId)],
            }, cancellationToken);
            if (result?.FailedOrders is { Length: > 0 })
            {
                var first = result.FailedOrders[0];
                throw new InvalidOperationException(
                    $"GMO Coin failed to cancel order {first.OrderId}: {first.Code} {first.Message}".Trim());
            }
            foreach (var orderId in result?.SuccessfulOrderIds ?? [])
                await SendCanceledOrderAsync(orderId, cancelMsg.TransactionId,
                    cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(
        PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
        EnsurePrivateReady();
        if (!lookupMsg.IsSubscribe)
        {
            using (_sync.EnterScope())
                _portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
            return;
        }

        var portfolio = GetPortfolioName();
        if (lookupMsg.PortfolioName.IsEmpty() ||
            lookupMsg.PortfolioName.EqualsIgnoreCase(portfolio))
        {
            await SendOutMessageAsync(new PortfolioMessage
            {
                PortfolioName = portfolio,
                BoardCode = BoardCodes.GmoCoin,
                OriginalTransactionId = lookupMsg.TransactionId,
            }, cancellationToken);
            await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
                cancellationToken);
        }
        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
        if (lookupMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
                cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _portfolioSubscriptions.Add(lookupMsg.TransactionId);
    }

    /// <inheritdoc />
    protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
        EnsurePrivateReady();
        if (!statusMsg.IsSubscribe)
        {
            using (_sync.EnterScope())
                _orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
            return;
        }
        if (statusMsg.Count is <= 0)
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }

        var market = statusMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(statusMsg.SecurityId);
        long? orderId = null;
        if (statusMsg.HasOrderId())
            orderId = ResolveOrderIdentifier(statusMsg.OrderId,
                statusMsg.OrderStringId, "lookup");
        var tracked = orderId is long nativeId ? GetTrackedOrder(nativeId) : null;
        if (market is null && tracked is not null)
            market = GetMarket(tracked.Symbol);
        var maximum = (statusMsg.Count ?? 500).Min(10000).Max(1).To<int>();

        if (orderId is long requestedOrderId)
        {
            var response = await RestClient.GetOrdersAsync(new()
            {
                OrderIds = requestedOrderId.ToString(CultureInfo.InvariantCulture),
            }, cancellationToken);
            var order = response?.Items?.FirstOrDefault();
            if (order is null)
                throw new InvalidDataException(
                    "GMO Coin returned no matching order.");
            await SendOrderAsync(order, statusMsg.TransactionId,
                cancellationToken);
            var executions = await RestClient.GetExecutionsAsync(new()
            {
                OrderIds = requestedOrderId.ToString(CultureInfo.InvariantCulture),
            }, cancellationToken);
            foreach (var execution in executions?.Items ?? [])
                await SendAccountTradeAsync(execution, statusMsg.TransactionId,
                    cancellationToken);
        }
        else
        {
            var markets = market is null ? GetSelectedMarkets(default) : [market];
            var sent = 0;
            foreach (var selectedMarket in markets)
            {
                foreach (var order in await DownloadActiveOrdersAsync(selectedMarket,
                    maximum - sent, cancellationToken))
                {
                    if (statusMsg.Side is not null &&
                        order.Side.ToStockSharp() != statusMsg.Side)
                        continue;
                    await SendOrderAsync(order, statusMsg.TransactionId,
                        cancellationToken);
                    if (++sent >= maximum)
                        break;
                }
                if (sent >= maximum)
                    break;
            }

            var executions = new List<GmoCoinExecution>();
            foreach (var selectedMarket in markets)
            {
                if (executions.Count >= maximum)
                    break;
                executions.AddRange(await DownloadExecutionsAsync(selectedMarket,
                    statusMsg, maximum - executions.Count, cancellationToken));
            }
            foreach (var group in executions.Where(execution =>
                statusMsg.Side is null ||
                execution.Side.ToStockSharp() == statusMsg.Side)
                .GroupBy(static execution => execution.OrderId))
                await SendCompletedOrderAsync(group, statusMsg.TransactionId,
                    cancellationToken);
            foreach (var execution in executions)
                if (statusMsg.Side is null ||
                    execution.Side.ToStockSharp() == statusMsg.Side)
                    await SendAccountTradeAsync(execution,
                        statusMsg.TransactionId, cancellationToken);
        }

        await SendSubscriptionResultAsync(statusMsg, cancellationToken);
        if (statusMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
                cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _orderSubscriptions[statusMsg.TransactionId] = new()
            {
                Symbol = market?.Symbol,
                OrderId = orderId,
                Side = statusMsg.Side,
            };
    }

    private NativeOrderParameters CreateOrderParameters(
        MarketDefinition market, OrderRegisterMessage message)
    {
        if (_serviceStatus != GmoCoinServiceStatuses.Open)
            throw new InvalidOperationException(
                $"GMO Coin is not open for trading ({_serviceStatus}).");
        var volume = message.Volume.Abs();
        if (volume <= 0)
            throw new InvalidOperationException(
                "GMO Coin order volume must be positive.");
        ValidateRange(volume, market.MinimumOrderSize, market.MaximumOrderSize,
            "volume", market.Symbol);
        ValidateStep(volume, market.SizeStep, "volume", market.Symbol);
        if (message.VisibleVolume is > 0 && message.VisibleVolume != volume)
            throw new NotSupportedException(
                "GMO Coin does not document iceberg orders.");
        if (message.TillDate is not null)
            throw new NotSupportedException(
                "GMO Coin does not support GTD orders.");

        var type = message.OrderType ?? OrderTypes.Limit;
        if (type is not (OrderTypes.Limit or OrderTypes.Market or
            OrderTypes.Conditional))
            throw new NotSupportedException(
                LocalizedStrings.OrderUnsupportedType.Put(type,
                    message.TransactionId));
        var condition = message.Condition as GmoCoinOrderCondition ?? new();
        if (condition.IsClosePosition &&
            (!market.IsMargin || condition.PositionId is not > 0))
            throw new InvalidOperationException(
                "A GMO Coin close order requires a margin market and a positive position ID.");
        if (!market.IsMargin && condition.LossCutPrice is not null)
            throw new InvalidOperationException(
                "GMO Coin liquidation price is available for margin orders only.");
        if (condition.LossCutPrice is <= 0)
            throw new InvalidOperationException(
                "GMO Coin liquidation price must be positive.");

        GmoCoinExecutionTypes executionType;
        decimal? price;
        switch (type)
        {
            case OrderTypes.Limit:
                if (message.Price <= 0)
                    throw new InvalidOperationException(
                        "GMO Coin limit orders require a positive price.");
                executionType = GmoCoinExecutionTypes.Limit;
                price = message.Price;
                break;
            case OrderTypes.Market:
                if (message.PostOnly == true)
                    throw new InvalidOperationException(
                        "A market order cannot be post-only.");
                executionType = GmoCoinExecutionTypes.Market;
                price = null;
                break;
            default:
                price = condition.TriggerPrice ??
                    (message.Price > 0 ? message.Price : null);
                if (price is not > 0)
                    throw new InvalidOperationException(
                        "GMO Coin STOP orders require a positive trigger price.");
                if (message.PostOnly == true)
                    throw new InvalidOperationException(
                        "A STOP order cannot be post-only.");
                executionType = GmoCoinExecutionTypes.Stop;
                break;
        }

        if (price is decimal orderPrice)
            ValidateStep(orderPrice, market.TickSize, "price", market.Symbol);
        if (condition.LossCutPrice is decimal lossCutPrice)
            ValidateStep(lossCutPrice, market.TickSize, "liquidation price",
                market.Symbol);
        var timeInForce = message.TimeInForce.ToGmoCoin(
            message.PostOnly == true, executionType);
        if (condition.IsCancelBefore &&
            (executionType != GmoCoinExecutionTypes.Market ||
            timeInForce != GmoCoinTimeInForce.FillAndKill ||
            message.Side != Sides.Sell))
            throw new InvalidOperationException(
                "GMO Coin cancelBefore is valid only for FAK market sell orders.");

        return new(executionType, price, timeInForce, volume, condition);
    }

    private static void ValidateRange(decimal value, decimal minimum,
        decimal maximum, string name, string symbol)
    {
        if (minimum > 0 && value < minimum)
            throw new InvalidOperationException(
                $"GMO Coin {name} must be at least {minimum} for '{symbol}'.");
        if (maximum > 0 && value > maximum)
            throw new InvalidOperationException(
                $"GMO Coin {name} must not exceed {maximum} for '{symbol}'.");
    }

    private static void ValidateStep(decimal value, decimal step, string name,
        string symbol)
    {
        if (step > 0 && value % step != 0)
            throw new InvalidOperationException(
                $"GMO Coin {name} must be aligned to step {step} for '{symbol}'.");
    }

    private MarketDefinition[] GetSelectedMarkets(SecurityId securityId)
    {
        if (!securityId.SecurityCode.IsEmpty())
            return [GetMarket(securityId)];
        using (_sync.EnterScope())
            return [.. _markets.Values.OrderBy(static value => value.Symbol,
                StringComparer.OrdinalIgnoreCase)];
    }

    private async ValueTask<GmoCoinOrder[]> DownloadActiveOrdersAsync(
        MarketDefinition market, int maximum,
        CancellationToken cancellationToken)
    {
        var values = new List<GmoCoinOrder>();
        for (var page = 1; values.Count < maximum; page++)
        {
            var pageSize = (maximum - values.Count).Min(100).Max(1);
            var response = await RestClient.GetActiveOrdersAsync(new()
            {
                Symbol = market.Symbol,
                Page = page,
                Count = pageSize,
            }, cancellationToken);
            var items = response?.Items ?? [];
            values.AddRange(items.Where(static item => item is not null));
            if (items.Length < pageSize)
                break;
        }
        return [.. values.Take(maximum)];
    }

    private async ValueTask<GmoCoinExecution[]> DownloadExecutionsAsync(
        MarketDefinition market, OrderStatusMessage message, int maximum,
        CancellationToken cancellationToken)
    {
        var from = message.From?.ToUniversalTime();
        var to = (message.To ?? DateTime.UtcNow).ToUniversalTime();
        var values = new List<GmoCoinExecution>();
        for (var page = 1; values.Count < maximum && page <= 100; page++)
        {
            var pageSize = (maximum - values.Count).Min(100).Max(1);
            var response = await RestClient.GetLatestExecutionsAsync(new()
            {
                Symbol = market.Symbol,
                Page = page,
                Count = pageSize,
            }, cancellationToken);
            var items = response?.Items ?? [];
            foreach (var execution in items)
            {
                if (execution is null)
                    continue;
                var time = execution.Timestamp.FromGmoCoinTime(DateTime.MinValue);
                if ((from is null || time >= from) && time <= to)
                    values.Add(execution);
            }
            if (items.Length < pageSize || (from is DateTime lower &&
                items.Any(item => item?.Timestamp.FromGmoCoinTime(
                    DateTime.MaxValue) < lower)))
                break;
        }
        return [.. values.OrderBy(static value => value.Timestamp)
            .TakeLast(maximum)];
    }

    private async ValueTask<GmoCoinPosition[]> DownloadOpenPositionsAsync(
        MarketDefinition market, CancellationToken cancellationToken)
    {
        if (!market.IsMargin)
            return [];
        var values = new List<GmoCoinPosition>();
        for (var page = 1; page <= 100; page++)
        {
            var response = await RestClient.GetOpenPositionsAsync(new()
            {
                Symbol = market.Symbol,
                Page = page,
                Count = 100,
            }, cancellationToken);
            var items = response?.Items ?? [];
            values.AddRange(items.Where(static item => item is not null));
            if (items.Length < 100)
                break;
        }
        return [.. values];
    }

    private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
        CancellationToken cancellationToken)
    {
        await SendAssetSnapshotAsync(originalTransactionId, cancellationToken);
        MarketDefinition[] marginMarkets;
        using (_sync.EnterScope())
            marginMarkets = [.. _markets.Values.Where(static market =>
                market.IsMargin)];
        foreach (var market in marginMarkets)
            foreach (var position in await DownloadOpenPositionsAsync(market,
                cancellationToken))
                await SendPositionAsync(position, originalTransactionId,
                    cancellationToken);
    }

    private async ValueTask SendAssetSnapshotAsync(long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var assets = await RestClient.GetAssetsAsync(cancellationToken);
        foreach (var asset in assets ?? [])
            await SendAssetAsync(asset, originalTransactionId, cancellationToken);
    }

    private ValueTask SendAssetAsync(GmoCoinAsset asset,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        if (asset?.Symbol.IsEmpty() != false)
            return default;
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = GetPortfolioName(),
            SecurityId = new SecurityId
            {
                SecurityCode = asset.Symbol.NormalizeSymbol(),
                BoardCode = BoardCodes.GmoCoin,
            },
            ServerTime = CurrentTime,
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, asset.Available, true)
        .TryAdd(PositionChangeTypes.BlockedValue,
            (asset.Amount - asset.Available).Max(0m), true)
        .TryAdd(PositionChangeTypes.CurrentPrice,
            asset.ConversionRate > 0 ? asset.ConversionRate : null, true),
            cancellationToken);
    }

    private ValueTask SendPositionAsync(GmoCoinPosition position,
        long originalTransactionId, CancellationToken cancellationToken,
        decimal? currentValue = null)
    {
        if (position?.Symbol.IsEmpty() != false || position.PositionId <= 0)
            return default;
        var value = currentValue ?? (position.Side == GmoCoinSides.Buy
            ? position.Size
            : -position.Size);
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = GetPortfolioName(),
            SecurityId = position.Symbol.ToStockSharp(),
            DepoName = position.PositionId.ToString(CultureInfo.InvariantCulture),
            ServerTime = position.Timestamp.FromGmoCoinTime(CurrentTime),
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, value, true)
        .TryAdd(PositionChangeTypes.BlockedValue, position.OrderedSize, true)
        .TryAdd(PositionChangeTypes.AveragePrice,
            position.Price > 0 ? position.Price : null, true)
        .TryAdd(PositionChangeTypes.UnrealizedPnL, position.LossGain, true)
        .TryAdd(PositionChangeTypes.Leverage,
            position.Leverage > 0 ? position.Leverage : null, true)
        .TryAdd(PositionChangeTypes.LiquidationPrice,
            position.LossCutPrice > 0 ? position.LossCutPrice : null, true),
            cancellationToken);
    }

    private ValueTask SendPositionSummaryAsync(
        GmoCoinPositionSummaryEvent summary, long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (summary?.Symbol.IsEmpty() != false)
            return default;
        var value = summary.Side == GmoCoinSides.Buy
            ? summary.SumPositionQuantity
            : -summary.SumPositionQuantity;
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = GetPortfolioName(),
            SecurityId = summary.Symbol.ToStockSharp(),
            DepoName = $"SUMMARY_{summary.Side}",
            ServerTime = summary.Timestamp.FromGmoCoinTime(CurrentTime),
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, value, true)
        .TryAdd(PositionChangeTypes.BlockedValue, summary.SumOrderQuantity, true)
        .TryAdd(PositionChangeTypes.AveragePrice,
            summary.AveragePositionRate > 0
                ? summary.AveragePositionRate
                : null, true)
        .TryAdd(PositionChangeTypes.UnrealizedPnL,
            summary.PositionLossGain, true), cancellationToken);
    }

    private ValueTask SendTrackedOrderAsync(TrackedOrder tracked,
        OrderStates state, decimal balance, long originalTransactionId,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = tracked.Symbol.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = tracked.Side,
            OrderVolume = tracked.Volume,
            Balance = balance,
            OrderPrice = tracked.Price,
            OrderType = tracked.OrderType,
            OrderState = state,
            OrderId = tracked.ExchangeOrderId,
            OrderStringId = tracked.ExchangeOrderId.ToString(
                CultureInfo.InvariantCulture),
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            PostOnly = tracked.IsPostOnly,
            TimeInForce = tracked.TimeInForce,
            Condition = tracked.Condition,
        }, cancellationToken);

    private async ValueTask SendOrderAsync(GmoCoinOrder order,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        if (order?.OrderId <= 0 || order.Symbol.IsEmpty())
            return;
        var tracked = GetTrackedOrder(order.OrderId) ?? new TrackedOrder
        {
            Symbol = order.Symbol.NormalizeSymbol(),
            ExchangeOrderId = order.OrderId,
            Side = order.Side.ToStockSharp(),
            OrderType = order.ExecutionType.ToStockSharp(),
            Volume = order.Size,
            Price = order.Price ?? 0m,
            IsPostOnly = order.TimeInForce == GmoCoinTimeInForce.StoreOrKill,
            TimeInForce = order.TimeInForce.ToStockSharp(),
            Condition = CreateCondition(order),
        };
        tracked.ExchangeOrderId = order.OrderId;
        TrackOrder(tracked, order.OrderId.ToString(CultureInfo.InvariantCulture));
        var state = order.CancelType is GmoCoinCancelTypes.StoreOrKillTaker or
            GmoCoinCancelTypes.PriceLimit
            ? OrderStates.Failed
            : order.Status.ToStockSharp();
        var balance = state == OrderStates.Active
            ? (order.Size - order.ExecutedSize).Max(0m)
            : 0m;
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.Symbol.ToStockSharp(),
            ServerTime = order.Timestamp.FromGmoCoinTime(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderVolume = order.Size,
            Balance = balance,
            OrderPrice = order.Price ?? 0m,
            OrderType = order.ExecutionType.ToStockSharp(),
            OrderState = state,
            OrderId = order.OrderId,
            OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            PostOnly = order.TimeInForce == GmoCoinTimeInForce.StoreOrKill,
            TimeInForce = order.TimeInForce.ToStockSharp(),
            Condition = tracked.Condition ?? CreateCondition(order),
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    $"GMO Coin rejected the order: {order.CancelType}.")
                : null,
        }, cancellationToken);
    }

    private async ValueTask SendCanceledOrderAsync(long orderId,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        var tracked = GetTrackedOrder(orderId);
        if (tracked is not null)
        {
            await SendTrackedOrderAsync(tracked, OrderStates.Done, 0m,
                originalTransactionId, cancellationToken);
            return;
        }
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            OrderId = orderId,
            OrderStringId = orderId.ToString(CultureInfo.InvariantCulture),
            OrderState = OrderStates.Done,
            Balance = 0m,
            OriginalTransactionId = originalTransactionId,
        }, cancellationToken);
    }

    private async ValueTask SendCompletedOrderAsync(
        IEnumerable<GmoCoinExecution> executions, long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var values = executions.ToArray();
        var first = values.FirstOrDefault();
        if (first is null || first.OrderId <= 0 || first.Symbol.IsEmpty())
            return;
        var tracked = GetTrackedOrder(first.OrderId);
        var volume = values.Sum(static execution => execution.Size);
        var average = volume > 0
            ? values.Sum(static execution => execution.Price * execution.Size) /
                volume
            : 0m;
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = first.Symbol.ToStockSharp(),
            ServerTime = values.Max(static execution => execution.Timestamp)
                .FromGmoCoinTime(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = first.Side.ToStockSharp(),
            OrderVolume = tracked?.Volume > 0 ? tracked.Volume : volume,
            Balance = 0m,
            OrderPrice = tracked?.Price ?? average,
            AveragePrice = average > 0 ? average : null,
            OrderType = tracked?.OrderType ?? OrderTypes.Limit,
            OrderState = OrderStates.Done,
            OrderId = first.OrderId,
            OrderStringId = first.OrderId.ToString(CultureInfo.InvariantCulture),
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = originalTransactionId,
            Condition = tracked?.Condition,
            Commission = values.Sum(static execution => execution.Fee),
        }, cancellationToken);
    }

    private ValueTask SendAccountTradeAsync(GmoCoinExecution execution,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        if (execution is null || execution.ExecutionId <= 0 ||
            execution.OrderId <= 0 || execution.Symbol.IsEmpty() ||
            execution.Price <= 0 || execution.Size <= 0 ||
            !AddAccountTrade(execution.ExecutionId))
            return default;
        var tracked = GetTrackedOrder(execution.OrderId);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = execution.Symbol.ToStockSharp(),
            ServerTime = execution.Timestamp.FromGmoCoinTime(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = execution.Side.ToStockSharp(),
            OrderId = execution.OrderId,
            OrderStringId = execution.OrderId.ToString(CultureInfo.InvariantCulture),
            TradeId = execution.ExecutionId,
            TradeStringId = execution.ExecutionId.ToString(
                CultureInfo.InvariantCulture),
            TradePrice = execution.Price,
            TradeVolume = execution.Size,
            Commission = execution.Fee,
            PnL = execution.LossGain,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = originalTransactionId,
        }, cancellationToken);
    }

    private async ValueTask OnOrderEventAsync(GmoCoinOrderEvent update,
        CancellationToken cancellationToken)
    {
        if (update?.OrderId <= 0 || update.Symbol.IsEmpty())
            return;
        var tracked = GetTrackedOrder(update.OrderId) ?? new TrackedOrder
        {
            Symbol = update.Symbol.NormalizeSymbol(),
            ExchangeOrderId = update.OrderId,
            Side = update.Side.ToStockSharp(),
            OrderType = update.ExecutionType.ToStockSharp(),
            Volume = update.OrderSize,
            Price = update.OrderPrice ?? 0m,
            IsPostOnly = update.TimeInForce == GmoCoinTimeInForce.StoreOrKill,
            TimeInForce = update.TimeInForce.ToStockSharp(),
            Condition = CreateCondition(update),
        };
        tracked.ExchangeOrderId = update.OrderId;
        TrackOrder(tracked, update.OrderId.ToString(CultureInfo.InvariantCulture));
        var state = update.CancelType is GmoCoinCancelTypes.StoreOrKillTaker or
            GmoCoinCancelTypes.PriceLimit
            ? OrderStates.Failed
            : update.OrderStatus.ToStockSharp();
        var side = update.Side.ToStockSharp();
        KeyValuePair<long, OrderSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _orderSubscriptions.Where(pair =>
                MatchesOrderSubscription(pair.Value, update.Symbol,
                    update.OrderId, side))];
        foreach (var pair in subscriptions)
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                SecurityId = update.Symbol.ToStockSharp(),
                ServerTime = update.OrderTimestamp.FromGmoCoinTime(CurrentTime),
                PortfolioName = GetPortfolioName(),
                Side = side,
                OrderVolume = update.OrderSize,
                Balance = state == OrderStates.Active
                    ? (update.OrderSize - update.OrderExecutedSize).Max(0m)
                    : 0m,
                OrderPrice = update.OrderPrice ?? 0m,
                OrderType = update.ExecutionType.ToStockSharp(),
                OrderState = state,
                OrderId = update.OrderId,
                OrderStringId = update.OrderId.ToString(
                    CultureInfo.InvariantCulture),
                TransactionId = tracked.TransactionId,
                OriginalTransactionId = pair.Key,
                PostOnly = update.TimeInForce == GmoCoinTimeInForce.StoreOrKill,
                TimeInForce = update.TimeInForce.ToStockSharp(),
                Condition = tracked.Condition ?? CreateCondition(update),
                Error = state == OrderStates.Failed
                    ? new InvalidOperationException(
                        $"GMO Coin rejected the order: {update.CancelType}.")
                    : null,
            }, cancellationToken);
    }

    private async ValueTask OnExecutionEventAsync(GmoCoinExecutionEvent update,
        CancellationToken cancellationToken)
    {
        if (update?.ExecutionId <= 0 || update.OrderId <= 0 ||
            update.Symbol.IsEmpty() || update.ExecutionPrice <= 0 ||
            update.ExecutionSize <= 0 || !AddAccountTrade(update.ExecutionId))
            return;
        var tracked = GetTrackedOrder(update.OrderId) ?? new TrackedOrder
        {
            Symbol = update.Symbol.NormalizeSymbol(),
            ExchangeOrderId = update.OrderId,
            Side = update.Side.ToStockSharp(),
            OrderType = update.ExecutionType.ToStockSharp(),
            Volume = update.OrderSize,
            Price = update.OrderPrice ?? 0m,
            IsPostOnly = update.TimeInForce == GmoCoinTimeInForce.StoreOrKill,
            TimeInForce = update.TimeInForce.ToStockSharp(),
        };
        tracked.ExchangeOrderId = update.OrderId;
        TrackOrder(tracked, update.OrderId.ToString(CultureInfo.InvariantCulture));
        var side = update.Side.ToStockSharp();
        KeyValuePair<long, OrderSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _orderSubscriptions.Where(pair =>
                MatchesOrderSubscription(pair.Value, update.Symbol,
                    update.OrderId, side))];
        var serverTime = update.ExecutionTimestamp.FromGmoCoinTime(CurrentTime);
        foreach (var pair in subscriptions)
        {
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                SecurityId = update.Symbol.ToStockSharp(),
                ServerTime = serverTime,
                PortfolioName = GetPortfolioName(),
                Side = side,
                OrderVolume = update.OrderSize,
                Balance = (update.OrderSize - update.OrderExecutedSize).Max(0m),
                AveragePrice = update.ExecutionPrice,
                OrderPrice = update.OrderPrice ?? 0m,
                OrderType = update.ExecutionType.ToStockSharp(),
                OrderState = update.OrderExecutedSize >= update.OrderSize
                    ? OrderStates.Done
                    : OrderStates.Active,
                OrderId = update.OrderId,
                OrderStringId = update.OrderId.ToString(
                    CultureInfo.InvariantCulture),
                TransactionId = tracked.TransactionId,
                OriginalTransactionId = pair.Key,
                PostOnly = update.TimeInForce == GmoCoinTimeInForce.StoreOrKill,
                TimeInForce = update.TimeInForce.ToStockSharp(),
                Condition = tracked.Condition,
            }, cancellationToken);
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                SecurityId = update.Symbol.ToStockSharp(),
                ServerTime = serverTime,
                PortfolioName = GetPortfolioName(),
                Side = side,
                OrderId = update.OrderId,
                OrderStringId = update.OrderId.ToString(
                    CultureInfo.InvariantCulture),
                TradeId = update.ExecutionId,
                TradeStringId = update.ExecutionId.ToString(
                    CultureInfo.InvariantCulture),
                TradePrice = update.ExecutionPrice,
                TradeVolume = update.ExecutionSize,
                Commission = update.Fee,
                PnL = update.LossGain,
                TransactionId = tracked.TransactionId,
                OriginalTransactionId = pair.Key,
            }, cancellationToken);
        }

        long[] portfolioSubscriptions;
        using (_sync.EnterScope())
            portfolioSubscriptions = [.. _portfolioSubscriptions];
        if (portfolioSubscriptions.Length > 0)
        {
            var assets = await RestClient.GetAssetsAsync(cancellationToken);
            foreach (var subscriptionId in portfolioSubscriptions)
                foreach (var asset in assets ?? [])
                    await SendAssetAsync(asset, subscriptionId,
                        cancellationToken);
        }
    }

    private async ValueTask OnPositionEventAsync(GmoCoinPositionEvent update,
        CancellationToken cancellationToken)
    {
        if (update is null)
            return;
        long[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _portfolioSubscriptions];
        var position = new GmoCoinPosition
        {
            PositionId = update.PositionId,
            Symbol = update.Symbol,
            Side = update.Side,
            Size = update.Size,
            OrderedSize = update.OrderedSize,
            Price = update.Price,
            LossGain = update.LossGain,
            Leverage = update.Leverage,
            LossCutPrice = update.LossCutPrice,
            Timestamp = update.Timestamp,
        };
        foreach (var subscriptionId in subscriptions)
            await SendPositionAsync(position, subscriptionId,
                cancellationToken,
                update.MessageType == GmoCoinPositionMessageTypes.Close
                    ? 0m
                    : null);
    }

    private async ValueTask OnPositionSummaryEventAsync(
        GmoCoinPositionSummaryEvent update,
        CancellationToken cancellationToken)
    {
        if (update is null)
            return;
        long[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _portfolioSubscriptions];
        foreach (var subscriptionId in subscriptions)
            await SendPositionSummaryAsync(update, subscriptionId,
                cancellationToken);
    }

    private static GmoCoinOrderCondition CreateCondition(GmoCoinOrder order)
        => order.ExecutionType == GmoCoinExecutionTypes.Stop ||
            order.LossCutPrice is > 0 ||
            order.SettlementType == GmoCoinSettlementTypes.Close
                ? new()
                {
                    TriggerPrice = order.ExecutionType == GmoCoinExecutionTypes.Stop
                        ? order.Price
                        : null,
                    LossCutPrice = order.LossCutPrice,
                    IsClosePosition =
                        order.SettlementType == GmoCoinSettlementTypes.Close,
                }
                : null;

    private static GmoCoinOrderCondition CreateCondition(GmoCoinOrderEvent order)
        => order.ExecutionType == GmoCoinExecutionTypes.Stop ||
            order.LossCutPrice is > 0 ||
            order.SettlementType == GmoCoinSettlementTypes.Close
                ? new()
                {
                    TriggerPrice = order.ExecutionType == GmoCoinExecutionTypes.Stop
                        ? order.OrderPrice
                        : null,
                    LossCutPrice = order.LossCutPrice,
                    IsClosePosition =
                        order.SettlementType == GmoCoinSettlementTypes.Close,
                }
                : null;

    private static bool MatchesOrderSubscription(OrderSubscription subscription,
        string symbol, long orderId, Sides side)
        => (subscription.Symbol.IsEmpty() ||
            subscription.Symbol.EqualsIgnoreCase(symbol)) &&
            (subscription.OrderId is null || subscription.OrderId == orderId) &&
            (subscription.Side is null || subscription.Side == side);

    private static long ParseExchangeOrderId(string value, string operation)
    {
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
            out var orderId) && orderId > 0)
            return orderId;
        throw new InvalidDataException(
            $"GMO Coin returned an invalid identifier for the {operation}: '{value}'.");
    }

    private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
