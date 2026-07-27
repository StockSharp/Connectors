namespace StockSharp.Ppi;

public partial class PpiMessageAdapter
{
    private readonly SynchronizedDictionary<long, long> _orderTransactions = [];
    private readonly SynchronizedDictionary<long, long> _transactionOrders = [];
    private readonly SynchronizedDictionary<long, Sides> _orderSides = [];
    private readonly SynchronizedDictionary<long, decimal>
        _executedQuantities = [];
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
        var native = await ResolveNative(
            regMsg.SecurityId, cancellationToken);
        var condition = regMsg.Condition as PpiOrderCondition;
        var request = CreateOrderRequest(
            _accountNumber,
            native,
            regMsg.Volume,
            regMsg.Price,
            regMsg.Side,
            regMsg.OrderType ?? OrderTypes.Limit,
            regMsg.TimeInForce,
            regMsg.TillDate,
            condition,
            regMsg.TransactionId);
        var budget = await _rest.BudgetOrder(
            request, cancellationToken);
        request["disclaimers"] = new JArray(
            (budget?.Disclaimers ?? [])
                .Where(item => !item.Code.IsEmpty())
                .Select(item => new JObject
                {
                    ["code"] = item.Code,
                    ["accepted"] = true,
                }));
        var order = await _rest.ConfirmOrder(
            request, cancellationToken);
        if (order?.Id is not > 0)
        {
            throw new InvalidDataException(
                "PPI order confirmation returned no order ID.");
        }

        RememberOrder(order.Id, regMsg.TransactionId);
        _orderSides[order.Id] = regMsg.Side;
        RememberInstrument(native, regMsg.SecurityId);
        await ProcessOrder(
            order,
            regMsg.TransactionId,
            false,
            null,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePortfolio(cancelMsg.PortfolioName);
        var orderId = ResolveOrderId(
            cancelMsg.OrderId,
            cancelMsg.OrderStringId,
            cancelMsg.OriginalTransactionId);
        var transactionId =
            _orderTransactions.TryGetValue2(orderId) ?? 0;
        var order = await _rest.CancelOrder(
            _accountNumber,
            orderId,
            transactionId == 0
                ? null
                : transactionId.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        if (order != null)
        {
            await ProcessOrder(
                order,
                cancelMsg.TransactionId,
                false,
                null,
                cancellationToken);
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

        EnsurePortfolio(statusMsg.PortfolioName);
        if (statusMsg.OrderId is > 0 ||
            !statusMsg.OrderStringId.IsEmpty() ||
            statusMsg.OriginalTransactionId != 0)
        {
            var orderId = ResolveOrderId(
                statusMsg.OrderId,
                statusMsg.OrderStringId,
                statusMsg.OriginalTransactionId);
            var order = await _rest.GetOrderDetail(
                _accountNumber,
                orderId,
                null,
                cancellationToken);
            if (order != null)
            {
                await ProcessOrder(
                    order,
                    statusMsg.TransactionId,
                    true,
                    null,
                    cancellationToken);
            }
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
            await _stream.SubscribeAccount(
                _accountNumber, cancellationToken);
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
        var dateTo = (to ?? CurrentTime).ToUniversalTime();
        var dateFrom = (from ?? dateTo.AddMonths(-3)).ToUniversalTime();
        if (dateFrom > dateTo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(from),
                from,
                "PPI order-history start time cannot be after the end time.");
        }

        var orders = new Dictionary<long, PpiOrder>();
        if (count is <= 0)
            return;
        foreach (var order in await _rest.GetOrders(
            _accountNumber, dateFrom, dateTo, cancellationToken) ?? [])
        {
            if (order?.Id > 0)
                orders[order.Id] = order;
        }
        foreach (var order in await _rest.GetActiveOrders(
            _accountNumber, cancellationToken) ?? [])
        {
            if (order?.Id > 0)
                orders[order.Id] = order;
        }

        var left = count ?? long.MaxValue;
        foreach (var order in orders.Values.OrderBy(item => item.Date))
        {
            await ProcessOrder(
                order,
                originalTransactionId,
                isLookup,
                null,
                cancellationToken);
            if (--left <= 0)
                break;
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
        await SendOutMessageAsync(
            new PortfolioMessage
            {
                OriginalTransactionId = lookupMsg.TransactionId,
                PortfolioName = _accountNumber,
                BoardCode = DefaultMarket.ToBoardCode(),
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
            await _stream.SubscribeAccount(
                _accountNumber, cancellationToken);
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
        }
    }

    private async ValueTask SendPortfolioSnapshot(
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        foreach (var balance in await _rest.GetAvailableBalance(
            _accountNumber, cancellationToken) ?? [])
        {
            if (balance is null)
                continue;
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originalTransactionId,
                    PortfolioName = _accountNumber,
                    SecurityId = SecurityId.Money,
                    Description = balance.Settlement,
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    balance.Amount,
                    true)
                .TryAdd(
                    PositionChangeTypes.Currency,
                    balance.Symbol.ToCurrency() ??
                        balance.Name.ToCurrency()),
                cancellationToken);
        }

        var snapshot = await _rest.GetBalancesAndPositions(
            _accountNumber, cancellationToken);
        if (snapshot is not JObject root)
            return;

        var groups = root.GetValue(
            "groupedInstruments",
            StringComparison.OrdinalIgnoreCase) as JArray;
        foreach (var group in groups ?? [])
        {
            var groupName = group.Value<string>("name");
            foreach (var item in group["instruments"] as JArray ?? [])
            {
                var ticker = item.Value<string>("ticker");
                if (ticker.IsEmpty())
                    continue;
                var instrumentType =
                    item.Value<string>("type").IsEmpty(groupName);
                var settlement = item.Value<string>("settlement")
                    .IsEmpty(DefaultSettlement);
                var securityId = await ResolveSecurityId(
                    ticker,
                    instrumentType,
                    settlement,
                    cancellationToken);
                var currency =
                    item.Value<string>("currency").ToCurrency();
                await SendOutMessageAsync(
                    new PositionChangeMessage
                    {
                        OriginalTransactionId = originalTransactionId,
                        PortfolioName = _accountNumber,
                        SecurityId = securityId,
                        Description = settlement,
                        ServerTime = CurrentTime,
                    }
                    .TryAdd(
                        PositionChangeTypes.CurrentValue,
                        item.Value<decimal?>("quantity"),
                        true)
                    .TryAdd(
                        PositionChangeTypes.BlockedValue,
                        item.Value<decimal?>("collateralQuantity"),
                        true)
                    .TryAdd(
                        PositionChangeTypes.CurrentPrice,
                        item.Value<decimal?>("price"),
                        true)
                    .TryAdd(
                        PositionChangeTypes.Currency,
                        currency),
                    cancellationToken);
            }
        }
    }

    private async ValueTask OnAccountUpdate(
        PpiAccountUpdate update,
        CancellationToken cancellationToken)
    {
        if (update is null)
            return;
        try
        {
            if (update.Type.EqualsIgnoreCase("OrderNotification") &&
                long.TryParse(
                    update.OrderId,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var orderId) &&
                orderId > 0)
            {
                var order = await _rest.GetOrderDetail(
                    _accountNumber,
                    orderId,
                    null,
                    cancellationToken);
                if (order != null)
                {
                    await ProcessOrder(
                        order,
                        _orderStatusSubscriptionId,
                        false,
                        update.QuantityExecuted,
                        cancellationToken);
                    await ProcessExecutionDelta(
                        order,
                        update,
                        cancellationToken);
                }
            }
            else if (_portfolioSubscriptionId != 0 &&
                update.Type.EqualsIgnoreCase("AccountNotification"))
            {
                await SendPortfolioSnapshot(
                    _portfolioSubscriptionId, cancellationToken);
                _lastPortfolioPoll = CurrentTime;
            }
        }
        catch (Exception error)
        {
            await SendOutErrorAsync(error, cancellationToken);
        }
    }

    private async ValueTask ProcessOrder(
        PpiOrder order,
        long originId,
        bool isLookup,
        decimal? executedQuantity,
        CancellationToken cancellationToken)
    {
        if (order?.Id is not > 0 || order.Ticker.IsEmpty())
            return;

        _orderTransactions.TryGetValue(
            order.Id, out var transactionId);
        if (transactionId == 0 &&
            long.TryParse(
                order.ExternalId,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var externalTransactionId))
        {
            transactionId = externalTransactionId;
        }
        RememberOrder(order.Id, transactionId);

        var state = order.Status.ToOrderState();
        var side = _orderSides.TryGetValue2(order.Id) ??
            order.Operation.ToSide();
        if (!order.Operation.IsEmpty() &&
            !order.Operation.ContainsIgnoreCase("stop"))
            _orderSides[order.Id] = side;
        var executed = executedQuantity ??
            (_executedQuantities.TryGetValue2(order.Id) ?? 0);
        var balance = state == OrderStates.Done
            ? 0
            : Math.Max(0, order.Quantity - executed);
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
                OrderId = order.Id,
                OrderStringId = order.Id.ToString(
                    CultureInfo.InvariantCulture),
                SecurityId = await ResolveSecurityId(
                    order.Ticker,
                    order.InstrumentType,
                    order.Settlement,
                    cancellationToken),
                PortfolioName = _accountNumber,
                OrderType = order.OperationType.ToOrderType(),
                Side = side,
                TimeInForce = TimeInForce.PutInQueue,
                OrderPrice = order.Price ?? 0,
                OrderVolume = order.Quantity,
                Balance = balance,
                OrderState = state,
                ServerTime = order.Date.ToUtc(CurrentTime),
                Condition = new PpiOrderCondition
                {
                    Settlement = order.Settlement,
                },
                Error = state == OrderStates.Failed
                    ? new InvalidOperationException(
                        $"PPI order status: {order.Status}.")
                    : null,
            },
            cancellationToken);
    }

    private async ValueTask ProcessExecutionDelta(
        PpiOrder order,
        PpiAccountUpdate update,
        CancellationToken cancellationToken)
    {
        var previous = _executedQuantities.TryGetValue2(order.Id) ?? 0;
        var current = Math.Max(previous, update.QuantityExecuted);
        _executedQuantities[order.Id] = current;
        var delta = current - previous;
        if (delta <= 0 || order.Price is not > 0)
            return;

        var time = update.LastUpdateDate.ToUtc(CurrentTime);
        var tradeId = $"{order.Id}:{current}:{time:O}";
        if (!_tradeIds.TryAdd(tradeId))
            return;
        var transactionId =
            _orderTransactions.TryGetValue2(order.Id) ?? 0;
        var side = _orderSides.TryGetValue2(order.Id) ??
            order.Operation.ToSide();
        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                OriginalTransactionId = transactionId != 0
                    ? transactionId
                    : _orderStatusSubscriptionId,
                TransactionId = 0,
                OrderId = order.Id,
                OrderStringId = order.Id.ToString(
                    CultureInfo.InvariantCulture),
                TradeStringId = tradeId,
                SecurityId = await ResolveSecurityId(
                    order.Ticker,
                    order.InstrumentType,
                    order.Settlement,
                    cancellationToken),
                PortfolioName = _accountNumber,
                Side = side,
                TradePrice = order.Price.Value,
                TradeVolume = delta,
                ServerTime = time,
            },
            cancellationToken);
    }

    private long ResolveOrderId(
        long? orderId,
        string orderStringId,
        long originalTransactionId)
    {
        if (orderId is > 0)
            return orderId.Value;
        if (long.TryParse(
            orderStringId,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed) &&
            parsed > 0)
        {
            return parsed;
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

    private void RememberOrder(long orderId, long transactionId)
    {
        if (orderId <= 0 || transactionId == 0)
            return;
        _orderTransactions[orderId] = transactionId;
        _transactionOrders[transactionId] = orderId;
    }

    private void EnsurePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(_accountNumber))
        {
            throw new InvalidOperationException(
                LocalizedStrings.AccountNotFound);
        }
    }

    internal static JObject CreateOrderRequest(
        string accountNumber,
        PpiInstrumentKey instrument,
        decimal volume,
        decimal price,
        Sides side,
        OrderTypes orderType,
        TimeInForce? timeInForce,
        DateTime? tillDate,
        PpiOrderCondition condition,
        long transactionId)
    {
        accountNumber.ThrowIfEmpty(nameof(accountNumber));
        if (volume <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(volume),
                volume,
                "PPI order quantity must be positive.");
        }
        if (orderType is not OrderTypes.Limit and
            not OrderTypes.Market and
            not OrderTypes.Conditional)
        {
            throw new NotSupportedException(
                "PPI supports limit, market, and stop orders.");
        }
        if (orderType != OrderTypes.Market && price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                price,
                "PPI limit and stop orders require a positive price.");
        }
        if (timeInForce is TimeInForce.MatchOrCancel or
            TimeInForce.CancelBalance)
        {
            throw new NotSupportedException(
                "PPI API does not expose FOK or IOC orders.");
        }

        var activationPrice = condition?.ActivationPrice;
        if (orderType == OrderTypes.Conditional &&
            activationPrice is not > 0)
        {
            throw new InvalidOperationException(
                "PPI stop orders require a positive activation price.");
        }
        if (activationPrice is <= 0)
            activationPrice = null;

        var operationTerm = condition?.OperationTerm ??
            (tillDate is null
                ? PpiOperationTerms.UntilExecution
                : PpiOperationTerms.UntilDate);
        DateTime? operationMaxDate =
            operationTerm == PpiOperationTerms.UntilDate
                ? tillDate ?? throw new InvalidOperationException(
                    "PPI valid-until-date orders require an expiration date.")
                : null;
        var nativeOrderType = orderType == OrderTypes.Conditional
            ? price > 0 ? OrderTypes.Limit : OrderTypes.Market
            : orderType;

        return new()
        {
            ["accountNumber"] = accountNumber,
            ["quantity"] = volume,
            ["price"] = nativeOrderType == OrderTypes.Market
                ? null
                : price,
            ["activationPrice"] = activationPrice,
            ["ticker"] = instrument.Ticker,
            ["instrumentType"] = instrument.Type,
            ["quantityType"] =
                (condition?.QuantityType ??
                    PpiQuantityTypes.Papers).ToNative(),
            ["operationTerm"] = operationTerm.ToNative(),
            ["operationMaxDate"] = operationMaxDate is null
                ? null
                : operationMaxDate.Value.ToUniversalTime().ToString(
                    "O", CultureInfo.InvariantCulture),
            ["operation"] = activationPrice is > 0
                ? "Stop Order"
                : side.ToNativeOperation(),
            ["settlement"] = condition?.Settlement
                .IsEmpty(instrument.Settlement),
            ["operationType"] =
                nativeOrderType.ToNativeOperationType(),
            ["disclaimers"] = new JArray(),
            ["externalID"] = transactionId.ToString(
                CultureInfo.InvariantCulture),
        };
    }
}
