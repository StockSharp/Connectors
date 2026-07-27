namespace StockSharp.Primary;

public partial class PrimaryMessageAdapter
{
    private readonly SynchronizedDictionary<string, long>
        _orderTransactions =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<long, string>
        _transactionOrders = [];
    private readonly SynchronizedDictionary<string, string>
        _orderReferences =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, string>
        _exchangeOrderClients =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, Sides>
        _orderSides =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, SecurityId>
        _orderSecurities =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, decimal>
        _executedQuantities =
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
        EnsureAccount(regMsg.PortfolioName);
        ValidateOrder(
            regMsg.OrderType,
            regMsg.Volume,
            regMsg.Price,
            regMsg.TillDate,
            regMsg.Condition as PrimaryOrderCondition);

        var native = await ResolveNative(
            regMsg.SecurityId, cancellationToken);
        var condition = regMsg.Condition as PrimaryOrderCondition;
        var result = await _rest.NewOrder(
            native,
            Account,
            regMsg.Side,
            regMsg.OrderType,
            regMsg.Volume,
            regMsg.Price,
            regMsg.TimeInForce,
            regMsg.TillDate,
            condition?.CancelPrevious == true,
            condition?.Iceberg == true,
            condition?.DisplayVolume,
            cancellationToken);
        var clientOrderId = result?.ClientId;
        if (clientOrderId.IsEmpty())
        {
            throw new InvalidDataException(
                "Primary did not return a client order identifier.");
        }

        RememberOrder(
            clientOrderId,
            result.Proprietary,
            regMsg.TransactionId,
            regMsg.Side,
            regMsg.SecurityId);
        RememberInstrument(native, regMsg.SecurityId);

        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                OriginalTransactionId = regMsg.TransactionId,
                TransactionId = regMsg.TransactionId,
                OrderStringId = clientOrderId,
                UserOrderId = regMsg.UserOrderId,
                PortfolioName = Account,
                SecurityId = regMsg.SecurityId,
                Side = regMsg.Side,
                OrderType = regMsg.OrderType ?? OrderTypes.Limit,
                TimeInForce =
                    regMsg.TimeInForce ?? TimeInForce.PutInQueue,
                OrderPrice = regMsg.Price,
                OrderVolume = regMsg.Volume,
                Balance = regMsg.Volume,
                OrderState = OrderStates.Pending,
                ServerTime = CurrentTime,
                Condition = condition,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsureAccount(replaceMsg.PortfolioName);
        ValidateOrder(
            replaceMsg.OrderType,
            replaceMsg.Volume,
            replaceMsg.Price,
            replaceMsg.TillDate,
            replaceMsg.Condition as PrimaryOrderCondition);
        if (replaceMsg.OrderType == OrderTypes.Market)
        {
            throw new NotSupportedException(
                "Primary replaceById requires a replacement price.");
        }

        var oldClientOrderId = ResolveOrderId(
            replaceMsg.OldOrderId,
            replaceMsg.OldOrderStringId,
            replaceMsg.OriginalTransactionId);
        var proprietary = ResolveProprietary(oldClientOrderId);
        var result = await _rest.ReplaceOrder(
            oldClientOrderId,
            proprietary,
            replaceMsg.Volume,
            replaceMsg.Price,
            cancellationToken);
        var clientOrderId = result?.ClientId;
        if (clientOrderId.IsEmpty())
        {
            throw new InvalidDataException(
                "Primary did not return a replacement request identifier.");
        }

        RememberOrder(
            clientOrderId,
            result.Proprietary.IsEmpty(proprietary),
            replaceMsg.TransactionId,
            replaceMsg.Side,
            replaceMsg.SecurityId);

        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                OriginalTransactionId = replaceMsg.TransactionId,
                TransactionId = replaceMsg.TransactionId,
                OrderStringId = clientOrderId,
                PortfolioName = Account,
                SecurityId = replaceMsg.SecurityId,
                Side = replaceMsg.Side,
                OrderType =
                    replaceMsg.OrderType ?? OrderTypes.Limit,
                TimeInForce =
                    replaceMsg.TimeInForce ?? TimeInForce.PutInQueue,
                OrderPrice = replaceMsg.Price,
                OrderVolume = replaceMsg.Volume,
                Balance = replaceMsg.Volume,
                OrderState = OrderStates.Pending,
                ServerTime = CurrentTime,
                Condition = replaceMsg.Condition,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsureAccount(cancelMsg.PortfolioName);
        var clientOrderId = ResolveOrderId(
            cancelMsg.OrderId,
            cancelMsg.OrderStringId,
            cancelMsg.OriginalTransactionId);
        var proprietary = ResolveProprietary(clientOrderId);
        var result = await _rest.CancelOrder(
            clientOrderId,
            proprietary,
            cancellationToken);
        if (result?.ClientId.IsEmpty() == false)
        {
            RememberOrder(
                result.ClientId,
                result.Proprietary.IsEmpty(proprietary),
                cancelMsg.TransactionId,
                _orderSides.TryGetValue2(clientOrderId) ?? Sides.Buy,
                _orderSecurities.TryGetValue2(clientOrderId) ??
                    default);
        }
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

        EnsureAccount(statusMsg.PortfolioName);
        if (statusMsg.OrderId is > 0 ||
            !statusMsg.OrderStringId.IsEmpty() ||
            statusMsg.OriginalTransactionId != 0)
        {
            var clientOrderId = ResolveOrderId(
                statusMsg.OrderId,
                statusMsg.OrderStringId,
                statusMsg.OriginalTransactionId);
            var order = await _rest.GetOrder(
                clientOrderId,
                ResolveProprietary(clientOrderId),
                cancellationToken);
            await ProcessOrder(
                order,
                statusMsg.TransactionId,
                true,
                cancellationToken);
        }
        else
        {
            await SendOrderSnapshot(
                statusMsg.TransactionId,
                true,
                statusMsg.From,
                statusMsg.To,
                statusMsg.Count,
                cancellationToken);
        }
        _lastOrderPoll = CurrentTime;

        if (statusMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                statusMsg.TransactionId, cancellationToken);
        }
        else
        {
            _orderStatusSubscriptionId = statusMsg.TransactionId;
            await _socket.SubscribeOrders(Account, cancellationToken);
            await SendSubscriptionResultAsync(
                statusMsg, cancellationToken);
        }
    }

    private async ValueTask SendOrderSnapshot(
        long originalTransactionId,
        bool isLookup,
        DateTime? from,
        DateTime? to,
        long? count,
        CancellationToken cancellationToken)
    {
        EnsureAccount(null);
        if (count is <= 0)
            return;
        var fromUtc = from?.ToUniversalTime();
        var toUtc = to?.ToUniversalTime();
        if (fromUtc > toUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(from),
                from,
                "Primary order-history start time cannot be after the end time.");
        }

        var orders = (await _rest.GetOrders(
            Account, cancellationToken))
            .Where(order =>
            {
                var time = order.TransactionTime.ToUtc(CurrentTime);
                return (fromUtc is null || time >= fromUtc) &&
                    (toUtc is null || time <= toUtc);
            })
            .OrderBy(order =>
                order.TransactionTime.ToUtc(CurrentTime))
            .ToArray();
        if (count is > 0)
        {
            orders =
            [
                .. orders.TakeLast(
                    (int)Math.Min(count.Value, int.MaxValue)),
            ];
        }

        foreach (var order in orders)
        {
            await ProcessOrder(
                order,
                originalTransactionId,
                isLookup,
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

        EnsureAccount(lookupMsg.PortfolioName);
        await SendOutMessageAsync(
            new PortfolioMessage
            {
                OriginalTransactionId = lookupMsg.TransactionId,
                PortfolioName = Account,
                BoardCode = "ROFEX",
            },
            cancellationToken);
        await SendPortfolioSnapshot(
            lookupMsg.TransactionId, cancellationToken);
        _lastPortfolioPoll = CurrentTime;

        if (lookupMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                lookupMsg.TransactionId, cancellationToken);
        }
        else
        {
            _portfolioSubscriptionId = lookupMsg.TransactionId;
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
        }
    }

    private async ValueTask SendPortfolioSnapshot(
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        EnsureAccount(null);
        var report = await _rest.GetAccountReport(
            Account, cancellationToken);
        var reportTime = report?.LastCalculation > 0
            ? report.LastCalculation.ToUtc(CurrentTime)
            : CurrentTime;

        if (report is not null)
        {
            var sentCash = false;
            foreach (var settlement in
                report.Detailed?.Values ??
                    Enumerable.Empty<PrimaryDetailedAccountReport>())
            {
                foreach (var pair in
                    settlement?.CurrencyBalance?.Detailed ??
                        new Dictionary<string, PrimaryCurrencyBalance>())
                {
                    var currency = pair.Key.ToCurrency();
                    if (currency is null || pair.Value is null)
                        continue;
                    sentCash = true;
                    await SendOutMessageAsync(
                        new PositionChangeMessage
                        {
                            OriginalTransactionId =
                                originalTransactionId,
                            PortfolioName = Account,
                            SecurityId = SecurityId.Money,
                            Description = pair.Key,
                            ServerTime = reportTime,
                        }
                        .TryAdd(
                            PositionChangeTypes.CurrentValue,
                            pair.Value.Available,
                            true)
                        .TryAdd(
                            PositionChangeTypes.BlockedValue,
                            pair.Value.Consumed,
                            true)
                        .TryAdd(
                            PositionChangeTypes.Currency,
                            currency),
                        cancellationToken);
                }
            }

            if (!sentCash)
            {
                await SendOutMessageAsync(
                    new PositionChangeMessage
                    {
                        OriginalTransactionId =
                            originalTransactionId,
                        PortfolioName = Account,
                        SecurityId = SecurityId.Money,
                        ServerTime = reportTime,
                    }
                    .TryAdd(
                        PositionChangeTypes.CurrentValue,
                        report.CurrentCash,
                        true)
                    .TryAdd(
                        PositionChangeTypes.BlockedValue,
                        report.Margin,
                        true)
                    .TryAdd(
                        PositionChangeTypes.VariationMargin,
                        report.DailyDifference,
                        true)
                    .TryAdd(
                        PositionChangeTypes.Currency,
                        CurrencyTypes.ARS),
                    cancellationToken);
            }
        }

        foreach (var position in await _rest.GetPositions(
            Account, cancellationToken))
        {
            var symbol = position?.TradingSymbol
                .IsEmpty(position?.Symbol);
            if (symbol.IsEmpty())
                continue;

            var current = position.BuySize - position.SellSize;
            var averagePrice = current switch
            {
                > 0 => position.BuyPrice,
                < 0 => position.SellPrice,
                _ => 0,
            };
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originalTransactionId,
                    PortfolioName = Account,
                    SecurityId = ResolveSecurityId(
                        DefaultMarket, symbol),
                    Description =
                        position.Instrument?.SymbolReference,
                    ServerTime = reportTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    current,
                    true)
                .TryAdd(
                    PositionChangeTypes.AveragePrice,
                    averagePrice.Positive(),
                    true)
                .TryAdd(
                    PositionChangeTypes.UnrealizedPnL,
                    position.TotalDifference,
                    true)
                .TryAdd(
                    PositionChangeTypes.VariationMargin,
                    position.TotalDailyDifference,
                    true),
                cancellationToken);
        }
    }

    private ValueTask ProcessOrderUpdate(
        PrimaryOrder order,
        CancellationToken cancellationToken)
        => ProcessOrder(
            order,
            _orderStatusSubscriptionId,
            false,
            cancellationToken);

    private async ValueTask ProcessOrder(
        PrimaryOrder order,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (order?.ClientOrderId.IsEmpty() != false ||
            order.InstrumentId?.Symbol.IsEmpty() != false)
        {
            return;
        }

        _orderTransactions.TryGetValue(
            order.ClientOrderId, out var transactionId);
        var securityId =
            _orderSecurities.TryGetValue2(order.ClientOrderId) ??
            ResolveSecurityId(
                order.InstrumentId.MarketId,
                order.InstrumentId.Symbol);
        var side =
            _orderSides.TryGetValue2(order.ClientOrderId) ??
            order.Side.ToSide();
        RememberOrder(
            order.ClientOrderId,
            order.Proprietary,
            transactionId,
            side,
            securityId);
        if (!order.OrderId.IsEmpty())
        {
            _exchangeOrderClients[order.OrderId] =
                order.ClientOrderId;
        }

        var state = order.Status.ToOrderState();
        var executed = Math.Max(0, order.CumulativeQuantity);
        var balance = order.LeavesQuantity >= 0
            ? order.LeavesQuantity
            : Math.Max(0, order.Quantity - executed);
        if (balance == 0 &&
            state is OrderStates.Active or OrderStates.Pending &&
            order.Quantity > executed)
        {
            balance = order.Quantity - executed;
        }
        var time = order.TransactionTime.ToUtc(CurrentTime);

        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                OriginalTransactionId = isLookup
                    ? originId
                    : transactionId != 0
                        ? transactionId
                        : originId,
                TransactionId = isLookup ? transactionId : 0,
                OrderId = long.TryParse(
                    order.OrderId,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var numericOrderId)
                        ? numericOrderId
                        : null,
                OrderStringId = order.ClientOrderId,
                OrderBoardId = order.OrderId,
                UserOrderId = order.WebSocketClientOrderId,
                SecurityId = securityId,
                PortfolioName =
                    order.AccountId?.Id.IsEmpty(Account),
                OrderType = order.OrderType.ToOrderType(),
                Side = side,
                TimeInForce =
                    order.TimeInForce.ToTimeInForce(),
                OrderPrice = order.Price,
                OrderVolume = order.Quantity,
                Balance = balance,
                AveragePrice = order.AveragePrice.Positive(),
                OrderState = state,
                ServerTime = time,
                Error = state == OrderStates.Failed
                    ? new InvalidOperationException(
                        $"Primary order rejected: " +
                        order.Text.IsEmpty("no reason supplied"))
                    : null,
            },
            cancellationToken);

        await ProcessExecutionDelta(
            order,
            securityId,
            side,
            time,
            cancellationToken);
    }

    private ValueTask ProcessExecutionDelta(
        PrimaryOrder order,
        SecurityId securityId,
        Sides side,
        DateTime time,
        CancellationToken cancellationToken)
    {
        var orderKey = order.OrderId
            .IsEmpty(order.ClientOrderId);
        var previous =
            _executedQuantities.TryGetValue2(orderKey) ?? 0;
        var current = Math.Max(previous, order.CumulativeQuantity);
        _executedQuantities[orderKey] = current;
        var delta = current - previous;
        var price = order.LastPrice.Positive() ??
            order.AveragePrice.Positive() ??
            order.Price.Positive();
        if (delta <= 0 || price is null)
            return default;

        var tradeId = order.ExecutionId.IsEmpty(
            $"{orderKey}:{current}:{time:O}");
        if (!_tradeIds.TryAdd(tradeId))
            return default;

        var transactionId =
            _orderTransactions.TryGetValue2(
                order.ClientOrderId) ?? 0;
        return SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                OriginalTransactionId = transactionId != 0
                    ? transactionId
                    : _orderStatusSubscriptionId,
                OrderStringId = order.ClientOrderId,
                OrderBoardId = order.OrderId,
                TradeStringId = tradeId,
                SecurityId = securityId,
                PortfolioName = Account,
                Side = side,
                TradePrice = price.Value,
                TradeVolume = delta,
                ServerTime = time,
            },
            cancellationToken);
    }

    private string ResolveOrderId(
        long? orderId,
        string orderStringId,
        long originalTransactionId)
    {
        if (!orderStringId.IsEmpty())
            return orderStringId;
        if (orderId is > 0)
        {
            var exchangeId = orderId.Value.ToString(
                CultureInfo.InvariantCulture);
            if (_exchangeOrderClients.TryGetValue(
                exchangeId, out var clientOrderId))
            {
                return clientOrderId;
            }
            return exchangeId;
        }
        if (_transactionOrders.TryGetValue(
            originalTransactionId, out var mapped))
        {
            return mapped;
        }
        throw new InvalidOperationException(
            LocalizedStrings.OrderNoExchangeId.Put(
                originalTransactionId));
    }

    private string ResolveProprietary(string clientOrderId)
    {
        _orderReferences.TryGetValue(
            clientOrderId, out var proprietary);
        return proprietary.IsEmpty(
            Proprietary.IsEmpty(IsDemo ? "PBCP" : "api"));
    }

    private void RememberOrder(
        string clientOrderId,
        string proprietary,
        long transactionId,
        Sides side,
        SecurityId securityId)
    {
        if (clientOrderId.IsEmpty())
            return;
        if (!proprietary.IsEmpty())
            _orderReferences[clientOrderId] = proprietary;
        _orderSides[clientOrderId] = side;
        if (!securityId.SecurityCode.IsEmpty())
            _orderSecurities[clientOrderId] = securityId;
        if (transactionId == 0)
            return;
        _orderTransactions[clientOrderId] = transactionId;
        _transactionOrders[transactionId] = clientOrderId;
    }

    private void EnsureAccount(string portfolioName)
    {
        if (Account.IsEmpty())
        {
            throw new InvalidOperationException(
                "Primary trading account is not configured.");
        }
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(Account))
        {
            throw new InvalidOperationException(
                LocalizedStrings.AccountNotFound);
        }
    }

    internal static void ValidateOrder(
        OrderTypes? orderType,
        decimal volume,
        decimal price,
        DateTime? tillDate,
        PrimaryOrderCondition condition)
    {
        if (orderType is not (
            null or OrderTypes.Market or OrderTypes.Limit))
        {
            throw new NotSupportedException(
                "Primary supports market and limit orders through this endpoint.");
        }
        if (volume <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(volume),
                volume,
                "Primary order quantity must be positive.");
        }
        if (orderType != OrderTypes.Market && price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                price,
                "Primary limit order price must be positive.");
        }
        if (tillDate is not null &&
            tillDate.Value.Date < DateTime.UtcNow.Date)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tillDate),
                tillDate,
                "Primary GTD expiration cannot be in the past.");
        }
        if (condition?.Iceberg == true)
        {
            if (condition.DisplayVolume is not > 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(condition.DisplayVolume),
                    condition.DisplayVolume,
                    "Primary iceberg display quantity must be positive.");
            }
            if (condition.DisplayVolume > volume)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(condition.DisplayVolume),
                    condition.DisplayVolume,
                    "Primary iceberg display quantity cannot exceed the order quantity.");
            }
        }
    }
}
