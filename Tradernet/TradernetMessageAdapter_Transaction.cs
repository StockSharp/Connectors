namespace StockSharp.Tradernet;

public partial class TradernetMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Volume <= 0 ||
            message.Volume !=
                decimal.Truncate(message.Volume) ||
            message.Volume > long.MaxValue)
        {
            throw new InvalidOperationException(
                "Tradernet order quantity must be a positive whole number.");
        }
        if (message.TillDate is not null)
        {
            throw new NotSupportedException(
                "Tradernet public API does not support a specific good-till date.");
        }

        var orderType =
            message.OrderType ?? OrderTypes.Limit;
        if (orderType is not (
            OrderTypes.Market or OrderTypes.Limit))
        {
            throw new NotSupportedException(
                "Tradernet standard registration supports market and limit orders.");
        }
        if (orderType == OrderTypes.Limit &&
            message.Price <= 0)
        {
            throw new InvalidOperationException(
                "Tradernet limit order price must be positive.");
        }

        var security = await GetSecurity(
            message.SecurityId, cancellationToken);
        var response = await Rest.PlaceOrder(new()
        {
            Ticker = security.Ticker,
            Action = message.Side == Sides.Buy ? 1 : 3,
            OrderType = orderType.ToNativeOrderType(),
            Quantity = (long)message.Volume,
            LimitPrice = orderType == OrderTypes.Limit
                ? message.Price : null,
            Expiration =
                message.TimeInForce.ToNativeExpiration(),
            UserOrderId = message.TransactionId,
        }, cancellationToken);
        if (response is null || response.OrderId <= 0)
        {
            throw new InvalidDataException(
                "Tradernet returned no order identifier.");
        }

        var orderId = response.OrderId;
        _orderTransactions[orderId] =
            message.TransactionId;
        _trackedOrders.Add(orderId);
        await Socket.SubscribeOrders(cancellationToken);
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId =
                message.TransactionId,
            TransactionId = message.TransactionId,
            OrderId = orderId,
            OrderStringId = orderId.ToString(
                CultureInfo.InvariantCulture),
            PortfolioName = message.PortfolioName
                .IsEmpty(_portfolioName)
                .IsEmpty("TRADERNET"),
            SecurityId = security.ToSecurityId(),
            Side = message.Side,
            OrderType = orderType,
            OrderPrice = message.Price,
            OrderVolume = message.Volume,
            Balance = message.Volume,
            OrderState = OrderStates.Pending,
            TimeInForce = message.TimeInForce ??
                TimeInForce.PutInQueue,
            ServerTime = CurrentTime,
        }, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage message,
        CancellationToken cancellationToken)
    {
        var orderId = GetOrderId(
            message.OrderId, message.OrderStringId,
            message.OriginalTransactionId);
        var response = await Rest.CancelOrder(
            orderId, cancellationToken);
        if (response is not null &&
            response.OrderId != 0 &&
            response.OrderId != orderId)
        {
            throw new InvalidDataException(
                $"Tradernet canceled unexpected order {response.OrderId}.");
        }
        _orderTransactions[orderId] =
            message.TransactionId;
        _trackedOrders.Add(orderId);
        await Socket.SubscribeOrders(cancellationToken);
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
                _portfolioNameFilter = null;
            }
            return;
        }

        await SendPortfolioSnapshot(
            message.TransactionId,
            message.PortfolioName,
            cancellationToken);
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
            _portfolioNameFilter =
                message.PortfolioName;
            await Socket.SubscribePortfolio(
                cancellationToken);
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

        await Socket.SubscribeOrders(cancellationToken);
        await SendOrderSnapshot(
            message.TransactionId,
            message, cancellationToken);
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

    private async Task SendPortfolioSnapshot(
        long originalTransactionId, string portfolioName,
        CancellationToken cancellationToken)
    {
        var portfolio =
            await Rest.GetPortfolio(cancellationToken);
        await ProcessPortfolioSnapshot(
            portfolio, originalTransactionId,
            portfolioName, cancellationToken);
    }

    private ValueTask ProcessPortfolio(
        TradernetPortfolio portfolio,
        CancellationToken cancellationToken)
    {
        if (_portfolioSubscriptionId == 0)
            return default;
        return ProcessPortfolioSnapshot(
            portfolio, _portfolioSubscriptionId,
            _portfolioNameFilter, cancellationToken);
    }

    private async ValueTask ProcessPortfolioSnapshot(
        TradernetPortfolio portfolio,
        long originalTransactionId,
        string portfolioNameFilter,
        CancellationToken cancellationToken)
    {
        portfolio = portfolio?.Nested ?? portfolio;
        if (portfolio is null)
            return;

        var name = NormalizePortfolioName(
            portfolio.Key).IsEmpty(_portfolioName)
            .IsEmpty("TRADERNET");
        _portfolioName = name;
        if (!portfolioNameFilter.IsEmpty() &&
            !portfolioNameFilter.EqualsIgnoreCase(name) &&
            !portfolioNameFilter.EqualsIgnoreCase(
                portfolio.Key))
            return;

        var currency =
            (portfolio.Accounts ?? [])
            .Select(account =>
                account.Currency.ToCurrency())
            .FirstOrDefault(value =>
                value is not null);
        await SendOutMessageAsync(new PortfolioMessage
        {
            OriginalTransactionId =
                originalTransactionId,
            PortfolioName = name,
            BoardCode = "TRADERNET",
            Currency = currency,
        }, cancellationToken);

        foreach (var account in
            portfolio.Accounts ?? [])
        {
            var available =
                account.Available.ToDecimal();
            var expected =
                (account.ForecastIn.ToDecimal() ?? 0m) -
                (account.ForecastOut.ToDecimal() ?? 0m);
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId =
                        originalTransactionId,
                    PortfolioName = name,
                    SecurityId = SecurityId.Money,
                    ServerTime = CurrentTime,
                }
                .TryAdd(PositionChangeTypes.CurrentValue,
                    available, true)
                .TryAdd(PositionChangeTypes.VariationMargin,
                    expected)
                .TryAdd(PositionChangeTypes.Currency,
                    account.Currency.ToCurrency()),
                cancellationToken);
        }

        foreach (var position in
            portfolio.Positions ?? [])
        {
            if (position?.Ticker.IsEmpty() != false)
                continue;
            var securityId =
                _securities.TryGetValue(
                    position.Ticker, out var security)
                    ? security.ToSecurityId()
                    : position.Ticker.ToSecurityId(
                        position.IssueNumber);
            await SendOutMessageAsync(
                new PositionChangeMessage
                {
                    OriginalTransactionId =
                        originalTransactionId,
                    PortfolioName = name,
                    SecurityId = securityId,
                    ServerTime = CurrentTime,
                }
                .TryAdd(PositionChangeTypes.CurrentValue,
                    position.Quantity.ToDecimal(), true)
                .TryAdd(PositionChangeTypes.AveragePrice,
                    position.BalancePrice.ToDecimal(), true)
                .TryAdd(PositionChangeTypes.CurrentPrice,
                    position.MarketPrice.ToDecimal(), true)
                .TryAdd(PositionChangeTypes.RealizedPnL,
                    position.RealizedPnl.ToDecimal())
                .TryAdd(PositionChangeTypes.UnrealizedPnL,
                    position.UnrealizedPnl.ToDecimal())
                .TryAdd(PositionChangeTypes.Currency,
                    position.Currency.ToCurrency()),
                cancellationToken);
        }
    }

    private async Task SendOrderSnapshot(
        long originalTransactionId,
        OrderStatusMessage filter,
        CancellationToken cancellationToken)
    {
        if (filter is null)
            return;

        var orders = new Dictionary<long, TradernetOrder>();
        foreach (var order in
            await Rest.GetCurrentOrders(
                false, cancellationToken))
        {
            if (order.GetOrderId() > 0)
                orders[order.GetOrderId()] = order;
        }

        if (filter.From is not null ||
            filter.To is not null)
        {
            var from = (filter.From ??
                DateTime.UtcNow.AddDays(-30))
                .ToUniversalTime();
            var to = (filter.To ?? DateTime.UtcNow)
                .ToUniversalTime();
            foreach (var order in
                await Rest.GetHistoricalOrders(
                    from, to, cancellationToken))
            {
                if (order.GetOrderId() > 0)
                    orders[order.GetOrderId()] = order;
            }
        }

        var skip = Math.Max(0, filter.Skip ?? 0);
        var left = filter.Count ?? long.MaxValue;
        foreach (var order in orders.Values
            .OrderBy(value => value.GetOrderTime()))
        {
            if (!Matches(order, filter))
                continue;
            if (skip > 0)
            {
                skip--;
                continue;
            }
            await ProcessOrder(
                order, originalTransactionId,
                cancellationToken);
            if (--left <= 0)
                break;
        }
    }

    private async ValueTask ProcessOrders(
        TradernetOrder[] orders,
        CancellationToken cancellationToken)
    {
        foreach (var order in orders ?? [])
        {
            var orderId = order.GetOrderId();
            var local = orderId > 0 &&
                _orderTransactions.ContainsKey(orderId);
            var subscription =
                _orderStatusSubscriptionId != 0 &&
                Matches(order, _orderStatusFilter);
            if (!local && !subscription)
                continue;
            await ProcessOrder(
                order,
                subscription
                    ? _orderStatusSubscriptionId : 0,
                cancellationToken);
        }
    }

    private bool Matches(TradernetOrder order,
        OrderStatusMessage filter)
    {
        if (order is null || filter is null)
            return false;

        var orderId = order.GetOrderId();
        if (filter.OrderId is long numericId &&
            orderId != numericId)
            return false;
        if (!filter.OrderStringId.IsEmpty() &&
            !filter.OrderStringId.EqualsIgnoreCase(
                orderId.ToString(
                    CultureInfo.InvariantCulture)))
            return false;
        if (filter.Side is Sides side &&
            (side == Sides.Buy) !=
            IsBuy(order.Operation))
            return false;
        if (filter.States?.Length > 0 &&
            !filter.States.Contains(
                order.Status.ToOrderState()))
            return false;

        var time = order.GetOrderTime();
        if (filter.From is DateTime from &&
            time < from.ToUniversalTime())
            return false;
        if (filter.To is DateTime to &&
            time > to.ToUniversalTime())
            return false;

        var portfolio = GetOrderPortfolio(order);
        if (!filter.PortfolioName.IsEmpty() &&
            !filter.PortfolioName.EqualsIgnoreCase(
                portfolio))
            return false;

        var securityIds = filter.SecurityIds ?? [];
        if (filter.SecurityId != default)
        {
            securityIds = securityIds
                .Append(filter.SecurityId).ToArray();
        }
        return securityIds.Length == 0 ||
            securityIds.Any(security =>
            {
                var ticker = security.Native as string;
                if (!ticker.IsEmpty())
                {
                    return ticker.EqualsIgnoreCase(
                        order.Ticker);
                }
                return security.SecurityCode.IsEmpty() ||
                    security.SecurityCode
                        .EqualsIgnoreCase(order.Ticker) ||
                    order.Ticker.StartsWith(
                        security.SecurityCode + ".",
                        StringComparison.OrdinalIgnoreCase);
            });
    }

    private async ValueTask ProcessOrder(
        TradernetOrder order,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var orderId = order.GetOrderId();
        if (orderId <= 0)
            return;

        var localTransactionId =
            _orderTransactions.TryGetValue(
                orderId, out var transactionId)
                ? transactionId
                : order.UserOrderId ?? 0;
        var effectiveOriginalId =
            originalTransactionId == 0
                ? localTransactionId
                : originalTransactionId;
        var state = order.Status.ToOrderState();
        var fingerprint =
            $"{order.Status}:{order.StatusDate}:" +
            $"{order.LeavesQuantity}:" +
            $"{order.Trades?.Length ?? 0}:" +
            $"{order.Trades?.LastOrDefault()?.Id}";
        var fingerprintKey =
            $"{effectiveOriginalId}:{orderId}";
        var changed =
            !_orderFingerprints.TryGetValue(
                fingerprintKey, out var previous) ||
            previous != fingerprint;
        _orderFingerprints[fingerprintKey] =
            fingerprint;

        var portfolio = GetOrderPortfolio(order);
        if (!_portfolioName.IsEmpty())
            portfolio = portfolio.IsEmpty(_portfolioName);
        var securityId =
            _securities.TryGetValue(
                order.Ticker, out var security)
                ? security.ToSecurityId()
                : TradernetExtensions.ToSecurityId(
                    order.Ticker);
        if (changed)
        {
            await SendOutMessageAsync(
                new ExecutionMessage
                {
                    DataTypeEx =
                        DataType.Transactions,
                    HasOrderInfo = true,
                    OriginalTransactionId =
                        effectiveOriginalId,
                    TransactionId =
                        localTransactionId,
                    OrderId = orderId,
                    OrderStringId = orderId.ToString(
                        CultureInfo.InvariantCulture),
                    PortfolioName =
                        portfolio.IsEmpty("TRADERNET"),
                    SecurityId = securityId,
                    Side = IsBuy(order.Operation)
                        ? Sides.Buy : Sides.Sell,
                    OrderType =
                        order.Type.ToOrderType(),
                    OrderPrice =
                        order.Price.ToDecimal() ?? 0m,
                    OrderVolume =
                        order.Quantity.ToDecimal(),
                    Balance =
                        order.LeavesQuantity.ToDecimal(),
                    OrderState = state,
                    TimeInForce =
                        order.Expiration.ToTimeInForce(),
                    ServerTime = order.GetOrderTime() ==
                        DateTime.MinValue
                            ? CurrentTime
                            : order.GetOrderTime(),
                    Error = state == OrderStates.Failed
                        ? new InvalidOperationException(
                            order.Text.IsEmpty(
                                order.AlternativeText)
                            .IsEmpty(
                                "Tradernet rejected the order."))
                        : null,
                }, cancellationToken);
        }

        foreach (var trade in order.Trades ?? [])
        {
            await ProcessOwnTrade(
                trade, order, securityId,
                portfolio, effectiveOriginalId,
                cancellationToken);
        }

        if (state is OrderStates.Done or
            OrderStates.Failed)
            _trackedOrders.Remove(orderId);
        else
            _trackedOrders.Add(orderId);
    }

    private ValueTask ProcessOwnTrade(
        TradernetOwnTrade trade,
        TradernetOrder order, SecurityId securityId,
        string portfolioName,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (trade is null || trade.Id <= 0)
            return default;
        var seenKey =
            $"{originalTransactionId}:{trade.Id}";
        if (_seenTrades.Contains(seenKey))
            return default;
        _seenTrades.Add(seenKey);

        return SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                OriginalTransactionId =
                    originalTransactionId,
                OrderId = order.GetOrderId(),
                OrderStringId = order.GetOrderId()
                    .ToString(
                        CultureInfo.InvariantCulture),
                TradeId = trade.Id,
                TradeStringId = trade.Id.ToString(
                    CultureInfo.InvariantCulture),
                PortfolioName =
                    portfolioName.IsEmpty("TRADERNET"),
                SecurityId = securityId,
                Side = IsBuy(order.Operation)
                    ? Sides.Buy : Sides.Sell,
                TradePrice =
                    trade.Price.ToDecimal(),
                TradeVolume =
                    trade.Quantity.ToDecimal(),
                ServerTime = trade.Date.ParseTimestamp(
                    order.GetOrderTime() ==
                        DateTime.MinValue
                            ? CurrentTime
                            : order.GetOrderTime()),
            }, cancellationToken);
    }

    private static bool IsBuy(int operation)
        => operation is 1 or 2;

    private static string GetOrderPortfolio(
        TradernetOrder order)
        => NormalizePortfolioName(
            order?.OwnerLogin
                .IsEmpty(order?.Login));

    private static string NormalizePortfolioName(
        string value)
        => value?.Trim().TrimStart('%');

    private static long GetOrderId(
        long? numericId, string stringId,
        long originalTransactionId)
    {
        if (numericId is long value)
            return value;
        if (long.TryParse(stringId,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value))
            return value;
        throw new InvalidOperationException(
            LocalizedStrings.OrderNoExchangeId.Put(
                originalTransactionId));
    }
}
