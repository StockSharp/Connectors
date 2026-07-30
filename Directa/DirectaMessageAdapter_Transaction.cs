namespace StockSharp.Directa;

public partial class DirectaMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Volume <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message.Volume), message.Volume,
                "Directa order quantity must be positive.");
        }
        if (message.TillDate is not null)
        {
            throw new NotSupportedException(
                "Directa Darwin API does not accept a good-till date.");
        }
        if (message.TimeInForce is not (
            null or TimeInForce.PutInQueue))
        {
            throw new NotSupportedException(
                $"Directa does not support {message.TimeInForce} time-in-force.");
        }

        var orderType =
            message.OrderType ?? OrderTypes.Limit;
        if (orderType == OrderTypes.Limit &&
            message.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message.Price), message.Price,
                "Directa limit price must be positive.");
        }
        var triggerPrice =
            (message.Condition as
                DirectaOrderCondition)?.TriggerPrice;
        if (orderType == OrderTypes.Conditional &&
            triggerPrice is not > 0)
        {
            throw new InvalidOperationException(
                "Directa conditional orders require DirectaOrderCondition.TriggerPrice.");
        }

        var ticker = GetTicker(message.SecurityId);
        var orderId = "ORD" +
            message.TransactionId.ToString(
                CultureInfo.InvariantCulture);
        var command =
            DirectaProtocol.CreateOrderCommand(
                orderId, ticker, message.Side,
                orderType, message.Volume,
                message.Price, triggerPrice);
        var operation = command[..command.IndexOf(' ')];
        _orderTransactions[orderId] =
            message.TransactionId;
        _trackedOrders[orderId] = new()
        {
            Ticker = ticker,
            Quantity = message.Volume,
            Operation = operation,
        };
        try
        {
            await Trading.Send(command, cancellationToken);
        }
        catch
        {
            _orderTransactions.Remove(orderId);
            _trackedOrders.Remove(orderId);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage message,
        CancellationToken cancellationToken)
    {
        var orderId = GetOrderId(
            message.OldOrderStringId,
            message.OldOrderId,
            message.OriginalTransactionId);
        if (_trackedOrders.TryGetValue(
                orderId, out var tracked) &&
            message.Volume > 0 &&
            message.Volume != tracked.Quantity)
        {
            throw new NotSupportedException(
                "Directa MODORD can change price only; quantity must remain unchanged.");
        }

        var triggerPrice =
            (message.Condition as
                DirectaOrderCondition)?.TriggerPrice;
        _orderTransactions[orderId] =
            message.TransactionId;
        await Trading.Send(
            DirectaProtocol.CreateReplaceCommand(
                orderId, message.Price, triggerPrice),
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage message,
        CancellationToken cancellationToken)
    {
        var orderId = GetOrderId(
            message.OrderStringId, message.OrderId,
            message.OriginalTransactionId);
        _orderTransactions[orderId] =
            message.TransactionId;
        await Trading.Send(
            "REVORD " + orderId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Mode.HasFlag(
            OrderGroupCancelModes.ClosePositions))
        {
            await base.CancelOrderGroupAsync(
                message, cancellationToken);
            return;
        }
        if (!message.Mode.HasFlag(
            OrderGroupCancelModes.CancelOrders))
            return;
        if (message.SecurityId == default)
        {
            throw new NotSupportedException(
                "Directa REVALL requires a security.");
        }

        await Trading.Send(
            "REVALL " + GetTicker(message.SecurityId),
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(
        PortfolioLookupMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);

        if (!message.IsSubscribe)
        {
            if (_portfolioSubscriptionId ==
                message.OriginalTransactionId)
            {
                _portfolioSubscriptionId = 0;
                _portfolioSubscriptionFilter = null;
            }
            return;
        }

        _portfolioSnapshotId = message.TransactionId;
        _portfolioSnapshotFilter = message.PortfolioName;
        try
        {
            await Trading.Send(
                "INFOACCOUNT", cancellationToken);
            await Trading.Send(
                "INFOAVAILABILITY", cancellationToken);
            await RequestBlock(
                "INFOSTOCKS",
                "BEGIN STOCKLIST", "END STOCKLIST",
                1018, cancellationToken);
        }
        finally
        {
            _portfolioSnapshotId = 0;
            _portfolioSnapshotFilter = null;
        }

        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId,
                cancellationToken);
        }
        else
        {
            _portfolioSubscriptionId =
                message.TransactionId;
            _portfolioSubscriptionFilter =
                message.PortfolioName;
            await SendSubscriptionResultAsync(
                message, cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OrderStatusAsync(
        OrderStatusMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);

        if (!message.IsSubscribe)
        {
            if (_orderStatusSubscriptionId ==
                message.OriginalTransactionId)
            {
                _orderStatusSubscriptionId = 0;
                _orderStatusFilter = null;
            }
            return;
        }

        _orderSnapshotId = message.TransactionId;
        _orderSnapshotFilter =
            (OrderStatusMessage)message.Clone();
        _orderSnapshotSkip =
            Math.Max(0, message.Skip ?? 0);
        _orderSnapshotLeft =
            message.Count ?? long.MaxValue;
        try
        {
            await RequestBlock(
                message.States?.Length > 0 &&
                    message.States.All(state =>
                        state is OrderStates.Active or
                            OrderStates.Pending)
                        ? "ORDERLISTPENDING"
                        : "ORDERLIST",
                "BEGIN ORDERLIST", "END ORDERLIST",
                1019, cancellationToken);
        }
        finally
        {
            _orderSnapshotId = 0;
            _orderSnapshotFilter = null;
            _orderSnapshotSkip = 0;
            _orderSnapshotLeft = 0;
        }

        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId,
                cancellationToken);
        }
        else
        {
            _orderStatusSubscriptionId =
                message.TransactionId;
            _orderStatusFilter =
                (OrderStatusMessage)message.Clone();
            await SendSubscriptionResultAsync(
                message, cancellationToken);
        }
    }

    private async ValueTask ProcessTradingMessage(
        string line,
        CancellationToken cancellationToken)
    {
        try
        {
            var type = DirectaProtocol.Split(line)
                .FirstOrDefault();
            switch (type)
            {
                case "INFOACCOUNT":
                    await ProcessAccount(
                        DirectaProtocol.ParseAccount(
                            line, _timeZone),
                        cancellationToken);
                    break;
                case "AVAILABILITY":
                    await ProcessAvailability(
                        DirectaProtocol.ParseAvailability(
                            line, _timeZone),
                        cancellationToken);
                    break;
                case "STOCK":
                    await ProcessPosition(
                        DirectaProtocol.ParsePosition(
                            line, _timeZone),
                        cancellationToken);
                    break;
                case "ORDER":
                    await ProcessOrder(
                        DirectaProtocol.ParseOrder(
                            line, _timeZone),
                        cancellationToken);
                    break;
                case "TRADOK":
                case "TRADERR":
                case "TRADCONFIRM":
                    await ProcessTradeResult(
                        DirectaProtocol.ParseTradeResult(
                            line),
                        cancellationToken);
                    break;
                case "ERR":
                    await SendOutErrorAsync(
                        CreateProtocolError(line),
                        cancellationToken);
                    break;
                case "DARWIN_STATUS":
                case "FLOWPOINT":
                case "UPDATEORDER":
                case "PRICEEXE":
                case "LOGCMD":
                case "AUTOREC":
                case "TradingRiconesso":
                case "Trading Disconnesso":
                    break;
                case string marker when
                    marker.StartsWith(
                        "BEGIN ", StringComparison.OrdinalIgnoreCase) ||
                    marker.StartsWith(
                        "END ", StringComparison.OrdinalIgnoreCase):
                    break;
                default:
                    if (_commandBlock is null)
                    {
                        this.AddDebugLog(
                            "Directa ignored trading line '{0}'.",
                            line);
                    }
                    break;
            }
        }
        catch (Exception error)
            when (error is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
        {
            await SendOutErrorAsync(
                error, cancellationToken);
        }
    }

    private async ValueTask ProcessAccount(
        DirectaAccount account,
        CancellationToken cancellationToken)
    {
        _portfolioName =
            account.Account.IsEmpty("DIRECTA");

        foreach (var originalId in
            GetPortfolioOutputIds())
        {
            if (!PortfolioMatches(originalId))
                continue;
            await SendOutMessageAsync(
                new PortfolioMessage
                {
                    OriginalTransactionId = originalId,
                    PortfolioName = _portfolioName,
                    BoardCode = "DIRECTA",
                    Currency = CurrencyTypes.EUR,
                }, cancellationToken);
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originalId,
                    PortfolioName = _portfolioName,
                    SecurityId = SecurityId.Money,
                    ServerTime = account.Time,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    account.Liquidity, true)
                .TryAdd(
                    PositionChangeTypes.RealizedPnL,
                    account.Gain)
                .TryAdd(
                    PositionChangeTypes.UnrealizedPnL,
                    account.OpenProfitLoss)
                .TryAdd(
                    PositionChangeTypes.Currency,
                    CurrencyTypes.EUR),
                cancellationToken);
        }
    }

    private async ValueTask ProcessAvailability(
        DirectaAvailability availability,
        CancellationToken cancellationToken)
    {
        foreach (var originalId in
            GetPortfolioOutputIds())
        {
            if (!PortfolioMatches(originalId))
                continue;
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originalId,
                    PortfolioName =
                        _portfolioName.IsEmpty("DIRECTA"),
                    SecurityId = SecurityId.Money,
                    ServerTime = availability.Time,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    availability.TotalLiquidity, true)
                .TryAdd(
                    PositionChangeTypes.Currency,
                    CurrencyTypes.EUR),
                cancellationToken);
        }
    }

    private async ValueTask ProcessPosition(
        DirectaPosition position,
        CancellationToken cancellationToken)
    {
        foreach (var originalId in
            GetPortfolioOutputIds())
        {
            if (!PortfolioMatches(originalId))
                continue;
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId = originalId,
                    PortfolioName =
                        _portfolioName.IsEmpty("DIRECTA"),
                    SecurityId =
                        DirectaProtocol.ToSecurityId(
                            position.Ticker),
                    ServerTime = position.Time,
                }
                .TryAdd(
                    PositionChangeTypes.CurrentValue,
                    position.Quantity, true)
                .TryAdd(
                    PositionChangeTypes.BlockedValue,
                    position.TradingQuantity?.Abs(), true)
                .TryAdd(
                    PositionChangeTypes.AveragePrice,
                    position.AveragePrice, true)
                .TryAdd(
                    PositionChangeTypes.UnrealizedPnL,
                    position.Gain),
                cancellationToken);
        }
    }

    private async ValueTask ProcessOrder(
        DirectaOrder order,
        CancellationToken cancellationToken)
    {
        if (order?.OrderId.IsEmpty() != false)
            return;

        var localTransactionId =
            _orderTransactions.TryGetValue(
                order.OrderId, out var transactionId)
                    ? transactionId : 0;
        long originalId;
        if (_orderSnapshotId != 0 &&
            Matches(order, _orderSnapshotFilter))
        {
            if (_orderSnapshotSkip > 0)
            {
                _orderSnapshotSkip--;
                return;
            }
            if (_orderSnapshotLeft <= 0)
                return;
            _orderSnapshotLeft--;
            originalId = _orderSnapshotId;
        }
        else if (_orderStatusSubscriptionId != 0 &&
            Matches(order, _orderStatusFilter))
        {
            originalId = _orderStatusSubscriptionId;
        }
        else if (localTransactionId != 0)
        {
            originalId = localTransactionId;
        }
        else
            return;

        _trackedOrders[order.OrderId] = new()
        {
            Ticker = order.Ticker,
            Quantity = order.Quantity ?? 0,
            Operation = order.Operation,
        };
        var state =
            DirectaProtocol.ToOrderState(order.Status);
        var fingerprint =
            $"{order.Status}:{order.LimitPrice}:" +
            $"{order.TriggerPrice}:{order.AveragePrice}:" +
            $"{order.ExecutionPrice}";
        var fingerprintKey =
            $"{originalId}:{order.OrderId}";
        if (!_orderFingerprints.TryGetValue(
                fingerprintKey, out var previous) ||
            previous != fingerprint)
        {
            _orderFingerprints[fingerprintKey] =
                fingerprint;
            await SendOutMessageAsync(
                CreateOrderMessage(
                    order, originalId,
                    localTransactionId, state),
                cancellationToken);
        }

        if (state == OrderStates.Done &&
            order.Status == 2003)
        {
            var tradePrice =
                order.ExecutionPrice ??
                order.AveragePrice;
            if (tradePrice is > 0 &&
                order.Quantity is > 0)
            {
                await SendOwnTrade(
                    order.OrderId,
                    order.DirectaId,
                    order.Ticker,
                    order.Operation,
                    tradePrice.Value,
                    order.Quantity.Value,
                    order.Time, originalId,
                    cancellationToken);
            }
        }
    }

    private ExecutionMessage CreateOrderMessage(
        DirectaOrder order, long originalId,
        long localTransactionId, OrderStates state)
        => new()
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = originalId,
            TransactionId = localTransactionId,
            OrderStringId = order.OrderId,
            PortfolioName =
                _portfolioName.IsEmpty("DIRECTA"),
            SecurityId =
                DirectaProtocol.ToSecurityId(order.Ticker),
            Side =
                DirectaProtocol.ToSide(order.Operation),
            OrderType =
                DirectaProtocol.ToOrderType(
                    order.Operation),
            OrderPrice = order.LimitPrice ?? 0,
            OrderVolume = order.Quantity,
            Balance = state is OrderStates.Done or
                OrderStates.Failed
                    ? 0 : order.Quantity,
            OrderState = state,
            TimeInForce = TimeInForce.PutInQueue,
            Condition =
                DirectaProtocol.ToOrderType(
                    order.Operation) ==
                    OrderTypes.Conditional
                        ? new DirectaOrderCondition
                        {
                            TriggerPrice =
                                order.TriggerPrice,
                        }
                        : null,
            ServerTime = order.Time,
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    "Directa rejected the order.")
                : null,
        };

    private async ValueTask ProcessTradeResult(
        DirectaTradeResult result,
        CancellationToken cancellationToken)
    {
        var localTransactionId =
            _orderTransactions.TryGetValue(
                result.OrderId, out var transactionId)
                    ? transactionId : 0;
        var originalId = localTransactionId != 0
            ? localTransactionId
            : _orderStatusSubscriptionId;

        if (result.MessageType == "TRADCONFIRM")
        {
            if (AutoConfirmOrders &&
                localTransactionId != 0)
            {
                await Trading.Send(
                    "CONFORD " + result.OrderId,
                    cancellationToken);
            }
            else if (originalId == 0)
            {
                return;
            }
        }

        if (result.MessageType == "TRADERR")
        {
            var error = new InvalidOperationException(
                result.Error.IsEmpty(
                    DirectaProtocol.GetError(
                        result.Code)));
            if (originalId == 0)
            {
                await SendOutErrorAsync(
                    error, cancellationToken);
                return;
            }
            await SendOutMessageAsync(
                CreateResultOrderMessage(
                    result, originalId,
                    localTransactionId,
                    OrderStates.Failed, error),
                cancellationToken);
            return;
        }

        if (originalId == 0)
            return;
        var state = result.Code switch
        {
            3000 => OrderStates.Active,
            3001 when
                result.RemainingQuantity is > 0
                    => OrderStates.Active,
            3001 or 3002 => OrderStates.Done,
            3003 => OrderStates.Pending,
            _ => OrderStates.Pending,
        };
        var fingerprint =
            $"{result.Code}:{result.EntryPrice}:" +
            $"{result.ExecutionPrice}:" +
            $"{result.ExecutedQuantity}:" +
            $"{result.RemainingQuantity}";
        var fingerprintKey =
            $"{originalId}:{result.OrderId}";
        if (!_orderFingerprints.TryGetValue(
                fingerprintKey, out var previous) ||
            previous != fingerprint)
        {
            _orderFingerprints[fingerprintKey] =
                fingerprint;
            await SendOutMessageAsync(
                CreateResultOrderMessage(
                    result, originalId,
                    localTransactionId, state, null),
                cancellationToken);
        }

        if (result.Code == 3001)
        {
            var price =
                result.ExecutionPrice ??
                result.EntryPrice;
            var volume =
                result.ExecutedQuantity ??
                result.RequestedQuantity;
            if (price is > 0 && volume is > 0)
            {
                await SendOwnTrade(
                    result.OrderId,
                    result.DirectaId,
                    result.Ticker,
                    result.Operation,
                    price.Value, volume.Value,
                    CurrentTime, originalId,
                    cancellationToken);
            }
        }
    }

    private ExecutionMessage CreateResultOrderMessage(
        DirectaTradeResult result, long originalId,
        long localTransactionId, OrderStates state,
        Exception error)
    {
        var tracked = _trackedOrders.TryGetValue(
            result.OrderId, out var value)
                ? value : null;
        var operation =
            result.Operation.IsEmpty(
                tracked?.Operation);
        var quantity =
            result.RequestedQuantity ??
            tracked?.Quantity;
        return new()
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = originalId,
            TransactionId = localTransactionId,
            OrderStringId = result.OrderId,
            PortfolioName =
                _portfolioName.IsEmpty("DIRECTA"),
            SecurityId =
                DirectaProtocol.ToSecurityId(
                    result.Ticker.IsEmpty(
                        tracked?.Ticker)),
            Side = DirectaProtocol.ToSide(operation),
            OrderType =
                DirectaProtocol.ToOrderType(operation),
            OrderPrice = result.EntryPrice ?? 0,
            OrderVolume = quantity,
            Balance = result.RemainingQuantity ??
                (state is OrderStates.Done or
                    OrderStates.Failed
                        ? 0 : quantity),
            OrderState = state,
            TimeInForce = TimeInForce.PutInQueue,
            ServerTime = CurrentTime,
            Error = error,
        };
    }

    private ValueTask SendOwnTrade(
        string orderId, string tradeId,
        string ticker, string operation,
        decimal price, decimal volume,
        DateTime time, long originalId,
        CancellationToken cancellationToken)
    {
        tradeId = tradeId.IsEmpty(
            $"{orderId}:{DirectaProtocol.FormatDecimal(price)}:" +
            DirectaProtocol.FormatDecimal(volume));
        var seenKey = $"{originalId}:{tradeId}";
        if (_seenTrades.Contains(seenKey))
            return default;
        _seenTrades.Add(seenKey);

        return SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                OriginalTransactionId = originalId,
                OrderStringId = orderId,
                TradeStringId = tradeId,
                PortfolioName =
                    _portfolioName.IsEmpty("DIRECTA"),
                SecurityId =
                    DirectaProtocol.ToSecurityId(ticker),
                Side =
                    DirectaProtocol.ToSide(operation),
                TradePrice = price,
                TradeVolume = volume,
                ServerTime = time,
            }, cancellationToken);
    }

    private bool Matches(
        DirectaOrder order, OrderStatusMessage filter)
    {
        if (order is null || filter is null)
            return false;
        if (filter.OrderId is long numericId &&
            (!long.TryParse(
                order.OrderId,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var orderNumericId) ||
            orderNumericId != numericId))
            return false;
        if (!filter.OrderStringId.IsEmpty() &&
            !filter.OrderStringId.EqualsIgnoreCase(
                order.OrderId))
            return false;
        if (filter.Side is Sides side &&
            side != DirectaProtocol.ToSide(
                order.Operation))
            return false;
        if (filter.States?.Length > 0 &&
            !filter.States.Contains(
                DirectaProtocol.ToOrderState(
                    order.Status)))
            return false;
        if (filter.From is DateTime from &&
            order.Time < from.ToUniversalTime())
            return false;
        if (filter.To is DateTime to &&
            order.Time > to.ToUniversalTime())
            return false;
        if (!filter.PortfolioName.IsEmpty() &&
            !filter.PortfolioName.EqualsIgnoreCase(
                _portfolioName.IsEmpty("DIRECTA")))
            return false;

        var securities = filter.SecurityIds ?? [];
        if (filter.SecurityId != default)
            securities = securities
                .Append(filter.SecurityId).ToArray();
        return securities.Length == 0 ||
            securities.Any(security =>
                security == default ||
                security.ToTicker()
                    .EqualsIgnoreCase(order.Ticker));
    }

    private long[] GetPortfolioOutputIds()
        => new[]
        {
            _portfolioSnapshotId,
            _portfolioSubscriptionId,
        }
        .Where(value => value != 0)
        .Distinct()
        .ToArray();

    private bool PortfolioMatches(long originalId)
    {
        var filter = originalId ==
            _portfolioSnapshotId
                ? _portfolioSnapshotFilter
                : _portfolioSubscriptionFilter;
        return filter.IsEmpty() ||
            filter.EqualsIgnoreCase(
                _portfolioName.IsEmpty("DIRECTA"));
    }

    private static string GetOrderId(
        string stringId, long? numericId,
        long originalTransactionId)
    {
        if (!stringId.IsEmpty())
            return DirectaProtocol.NormalizeTicker(stringId);
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
