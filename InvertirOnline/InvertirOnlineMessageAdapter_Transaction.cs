namespace StockSharp.InvertirOnline;

public partial class InvertirOnlineMessageAdapter
{
    private readonly SynchronizedDictionary<long, long>
        _orderTransactions = [];
    private readonly SynchronizedDictionary<long, long>
        _transactionOrders = [];
    private readonly SynchronizedDictionary<long, Sides> _orderSides = [];
    private readonly SynchronizedDictionary<long, SecurityId>
        _orderSecurities = [];
    private readonly SynchronizedDictionary<long, decimal>
        _executedQuantities = [];
    private readonly SynchronizedSet<string> _tradeIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<long> _trackedOrders = [];
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
        var condition =
            regMsg.Condition as InvertirOnlineOrderCondition;
        var request = CreateOrderRequest(
            native,
            regMsg.Volume,
            regMsg.Price,
            regMsg.OrderType ?? OrderTypes.Limit,
            regMsg.TimeInForce,
            regMsg.TillDate,
            condition,
            CurrentTime);
        var result = await _rest.PlaceOrder(
            regMsg.Side, request, cancellationToken);
        if (result.OperationNumber <= 0)
        {
            throw new InvalidDataException(
                "IOL returned no operation number.");
        }

        RememberOrder(result.OperationNumber, regMsg.TransactionId);
        _orderSides[result.OperationNumber] = regMsg.Side;
        _orderSecurities[result.OperationNumber] = regMsg.SecurityId;
        _trackedOrders.Add(result.OperationNumber);
        RememberInstrument(native, regMsg.SecurityId);

        await ProcessOrder(
            new()
            {
                Number = result.OperationNumber,
                OrderDate = CurrentTime,
                Side = regMsg.Side == Sides.Sell ? "venta" : "compra",
                State = "iniciada",
                Market = native.Market,
                Symbol = native.Symbol,
                Quantity = regMsg.Volume,
                OrderType = "precio_Limite",
                Price = regMsg.Price,
                Settlement = request.Value<string>("plazo"),
            },
            regMsg.TransactionId,
            false,
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
        await _rest.CancelOrder(orderId, cancellationToken);
        _trackedOrders.Add(orderId);

        try
        {
            var operation = await _rest.GetOperation(
                orderId, cancellationToken);
            if (operation != null)
            {
                await ProcessOrder(
                    operation.ToSummary(),
                    cancelMsg.TransactionId,
                    false,
                    cancellationToken);
            }
        }
        catch (HttpRequestException error) when (
            error.StatusCode == HttpStatusCode.NotFound)
        {
            this.AddVerboseLog(
                "IOL operation {0} is not queryable yet after cancellation.",
                orderId);
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
            var operation = await _rest.GetOperation(
                orderId, cancellationToken);
            if (operation != null)
            {
                await ProcessOrder(
                    operation.ToSummary(),
                    statusMsg.TransactionId,
                    true,
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
        if (count is <= 0)
            return;

        var dateTo = (to ?? CurrentTime).ToUniversalTime();
        var dateFrom = (from ??
            dateTo.AddMonths(-1)).ToUniversalTime();
        if (dateFrom > dateTo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(from),
                from,
                "IOL order-history start time cannot be after the end time.");
        }

        var operations = await _rest.GetOperations(
            null,
            "todas",
            dateFrom,
            dateTo,
            null,
            cancellationToken);
        var left = count ?? long.MaxValue;

        foreach (var operation in (operations ?? [])
            .Where(item => item?.Number > 0)
            .OrderBy(item => item.OrderDate))
        {
            await ProcessOrder(
                operation,
                originalTransactionId,
                isLookup,
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
                PortfolioName = _portfolioName,
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
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
        }
    }

    private async ValueTask SendPortfolioSnapshot(
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var accountState = await _rest.GetAccountState(cancellationToken);

        foreach (var account in accountState?.Accounts ?? [])
        {
            if (account is null)
                continue;

            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originalTransactionId,
                    PortfolioName = _portfolioName,
                    SecurityId = SecurityId.Money,
                    Description = account.Type,
                    ServerTime = CurrentTime,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    account.Balance,
                    true)
                .TryAdd(
                    PositionChangeTypes.BlockedValue,
                    account.Blocked,
                    true)
                .TryAdd(
                    PositionChangeTypes.Currency,
                    account.Currency.ToCurrency()),
                cancellationToken);

            foreach (var balance in account.Settlements ?? [])
            {
                if (balance is null)
                    continue;
                await SendOutMessageAsync(
                    new PositionChangeMessage
                    {
                        OriginalTransactionId =
                            originalTransactionId,
                        PortfolioName = _portfolioName,
                        SecurityId = SecurityId.Money,
                        Description =
                            $"{account.Type}:{balance.Settlement}",
                        ServerTime = CurrentTime,
                    }
                    .TryAdd(
                        PositionChangeTypes.CurrentValue,
                        balance.Balance,
                        true)
                    .TryAdd(
                        PositionChangeTypes.BlockedValue,
                        balance.Blocked,
                        true)
                    .TryAdd(
                        PositionChangeTypes.Currency,
                        account.Currency.ToCurrency()),
                    cancellationToken);
            }
        }

        foreach (var country in new[]
        {
            InvertirOnlineCountries.Argentina.ToNative(),
            InvertirOnlineCountries.UnitedStates.ToNative(),
        })
        {
            IolPortfolio portfolio;
            try
            {
                portfolio = await _rest.GetPortfolio(
                    country, cancellationToken);
            }
            catch (HttpRequestException error) when (
                error.StatusCode is HttpStatusCode.NotFound or
                    HttpStatusCode.BadRequest)
            {
                continue;
            }

            foreach (var position in portfolio?.Positions ?? [])
            {
                var title = position?.Title;
                if (title?.Symbol.IsEmpty() != false)
                    continue;

                var securityId = ResolveSecurityId(
                    title.Symbol,
                    title.Market,
                    title.Settlement,
                    title.Country.IsEmpty(country),
                    title.InstrumentType);
                await SendOutMessageAsync(
                    new PositionChangeMessage
                    {
                        OriginalTransactionId =
                            originalTransactionId,
                        PortfolioName = _portfolioName,
                        SecurityId = securityId,
                        Description = title.Settlement,
                        ServerTime = CurrentTime,
                    }
                    .TryAdd(
                        PositionChangeTypes.CurrentValue,
                        position.Quantity,
                        true)
                    .TryAdd(
                        PositionChangeTypes.BlockedValue,
                        position.Blocked,
                        true)
                    .TryAdd(
                        PositionChangeTypes.CurrentPrice,
                        position.LastPrice,
                        true)
                    .TryAdd(
                        PositionChangeTypes.AveragePrice,
                        position.AveragePrice,
                        true)
                    .TryAdd(
                        PositionChangeTypes.UnrealizedPnL,
                        position.Profit,
                        true)
                    .TryAdd(
                        PositionChangeTypes.Currency,
                        title.Currency.ToCurrency()),
                    cancellationToken);
            }
        }
    }

    private async ValueTask ProcessOrder(
        IolOperation operation,
        long originId,
        bool isLookup,
        CancellationToken cancellationToken)
    {
        if (operation?.Number is not > 0 ||
            operation.Symbol.IsEmpty())
        {
            return;
        }

        _orderTransactions.TryGetValue(
            operation.Number, out var transactionId);
        RememberOrder(operation.Number, transactionId);
        var side = _orderSides.TryGetValue2(operation.Number) ??
            operation.Side.ToSide();
        _orderSides[operation.Number] = side;
        var securityId =
            _orderSecurities.TryGetValue2(operation.Number) ??
            ResolveSecurityId(
                operation.Symbol,
                operation.Market,
                operation.Settlement);
        _orderSecurities[operation.Number] = securityId;

        var state = operation.State.ToOrderState();
        var executed = Math.Max(0, operation.ExecutedQuantity);
        var balance = Math.Max(0, operation.Quantity - executed);
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
                OrderId = operation.Number,
                OrderStringId = operation.Number.ToString(
                    CultureInfo.InvariantCulture),
                SecurityId = securityId,
                PortfolioName = _portfolioName,
                OrderType = operation.OrderType.ToOrderType(),
                Side = side,
                TimeInForce = TimeInForce.PutInQueue,
                OrderPrice = operation.Price,
                OrderVolume = operation.Quantity,
                Balance = balance,
                OrderState = state,
                ServerTime = operation.OrderDate.ToUtc(CurrentTime),
                Condition = new InvertirOnlineOrderCondition
                {
                    Settlement = ParseSettlement(operation.Settlement),
                },
                Error = state == OrderStates.Failed
                    ? new InvalidOperationException(
                        $"IOL order status: {operation.State}.")
                    : null,
            },
            cancellationToken);

        await ProcessExecutionDelta(
            operation,
            securityId,
            side,
            cancellationToken);
        if (state is OrderStates.Done or OrderStates.Failed)
            _trackedOrders.Remove(operation.Number);
        else
            _trackedOrders.Add(operation.Number);
    }

    private async ValueTask ProcessExecutionDelta(
        IolOperation operation,
        SecurityId securityId,
        Sides side,
        CancellationToken cancellationToken)
    {
        var previous =
            _executedQuantities.TryGetValue2(operation.Number) ?? 0;
        var current = Math.Max(previous, operation.ExecutedQuantity);
        _executedQuantities[operation.Number] = current;
        var delta = current - previous;
        var price = operation.ExecutionPrice.Positive() ??
            operation.Price.Positive();
        if (delta <= 0 || price is null)
            return;

        var time = operation.ExecutionDate.ToUtc(
            operation.OrderDate.ToUtc(CurrentTime));
        var tradeId =
            $"{operation.Number}:{current}:{time:O}";
        if (!_tradeIds.TryAdd(tradeId))
            return;

        var transactionId =
            _orderTransactions.TryGetValue2(operation.Number) ?? 0;
        await SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                OriginalTransactionId = transactionId != 0
                    ? transactionId
                    : _orderStatusSubscriptionId,
                TransactionId = 0,
                OrderId = operation.Number,
                OrderStringId = operation.Number.ToString(
                    CultureInfo.InvariantCulture),
                TradeStringId = tradeId,
                SecurityId = securityId,
                PortfolioName = _portfolioName,
                Side = side,
                TradePrice = price.Value,
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
            !portfolioName.EqualsIgnoreCase(_portfolioName))
        {
            throw new InvalidOperationException(
                LocalizedStrings.AccountNotFound);
        }
    }

    internal static JObject CreateOrderRequest(
        IolSecurityKey security,
        decimal volume,
        decimal price,
        OrderTypes orderType,
        TimeInForce? timeInForce,
        DateTime? tillDate,
        InvertirOnlineOrderCondition condition,
        DateTime currentTime)
    {
        if (volume <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(volume),
                volume,
                "IOL order quantity must be positive.");
        }
        if (orderType != OrderTypes.Limit)
        {
            throw new NotSupportedException(
                "IOL connector supports limit orders only because market buy orders use a cash amount rather than a security quantity.");
        }
        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                price,
                "IOL limit order price must be positive.");
        }
        if (timeInForce is TimeInForce.MatchOrCancel or
            TimeInForce.CancelBalance)
        {
            throw new NotSupportedException(
                "IOL API does not expose FOK or IOC for this order endpoint.");
        }

        var validity = (tillDate ?? currentTime.Date).Date;
        if (validity < currentTime.Date)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tillDate),
                tillDate,
                "IOL order validity cannot be in the past.");
        }
        var settlement =
            (condition?.Settlement ?? ParseSettlement(security.Settlement))
                .ToNative();

        return new()
        {
            ["mercado"] = security.Market,
            ["simbolo"] = security.Symbol,
            ["cantidad"] = volume,
            ["precio"] = price,
            ["plazo"] = settlement,
            ["validez"] = validity.ToString(
                "yyyy-MM-ddTHH:mm:ss",
                CultureInfo.InvariantCulture),
            ["tipoOrden"] = orderType.ToNativeOrderType(),
        };
    }

    private static InvertirOnlineSettlements ParseSettlement(string value)
        => Enum.TryParse<InvertirOnlineSettlements>(
            value.ToSettlement("t1"),
            true,
            out var settlement)
                ? settlement
                : InvertirOnlineSettlements.T1;
}
