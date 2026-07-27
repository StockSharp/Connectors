namespace StockSharp.Comdirect;

public partial class ComdirectMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Volume <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(message.Volume), message.Volume,
                "comdirect requires a positive order volume.");

        var orderType = message.OrderType ?? OrderTypes.Limit;
        if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
            throw new NotSupportedException(
                "comdirect REST API supports market and limit orders.");
        if (orderType == OrderTypes.Limit && message.Price <= 0)
            throw new InvalidOperationException(
                "comdirect limit order price must be positive.");

        var depot = ResolveDepot(message.PortfolioName);
        var instrument = await GetInstrument(
            message.SecurityId.SecurityCode, cancellationToken);
        var currency = instrument?.StaticData?.Currency
            .IsEmpty(DefaultCurrency);
        var bestEx = message.SecurityId.BoardCode.IsEmpty() ||
            message.SecurityId.BoardCode.EqualsIgnoreCase("COMDIRECT");
        var native = new ComdirectOrder
        {
            DepotId = depot.DepotId,
            SettlementAccountId = depot.DefaultSettlementAccountId,
            BestEx = bestEx,
            OrderType = orderType == OrderTypes.Market
                ? "MARKET" : "LIMIT",
            Side = message.Side == Sides.Buy ? "BUY" : "SELL",
            InstrumentId = instrument?.InstrumentId
                .IsEmpty(message.SecurityId.SecurityCode),
            VenueId = bestEx ? null : message.SecurityId.BoardCode,
            Quantity = message.Volume.ToNativeAmount("XXX"),
            Limit = orderType == OrderTypes.Limit
                ? message.Price.ToNativeAmount(currency) : null,
            LimitExtension = message.TimeInForce.ToLimitExtension(),
            ValidityType = message.TillDate is null ? "GFD" : "GTD",
            Validity = message.TillDate?.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        var response = await Rest.CreateOrder(native, cancellationToken);
        var orderId = response?.OrderId.ThrowIfEmpty(
            "comdirect order identifier");
        _orderTransactions[orderId] = message.TransactionId;
        _trackedOrders.Add(orderId);
        CacheInstrument(response?.Instrument, native.InstrumentId);

        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = message.TransactionId,
            TransactionId = message.TransactionId,
            OrderStringId = orderId,
            PortfolioName = GetPortfolioName(depot),
            SecurityId = message.SecurityId,
            Side = message.Side,
            OrderType = orderType,
            OrderPrice = message.Price,
            OrderVolume = message.Volume,
            Balance = message.Volume,
            OrderState = OrderStates.Pending,
            TimeInForce = message.TimeInForce,
            ExpiryDate = message.TillDate,
            ServerTime = CurrentTime,
        }, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage message,
        CancellationToken cancellationToken)
    {
        var orderId = GetOrderId(message.OldOrderStringId,
            message.OldOrderId, message.OriginalTransactionId);
        var orderType = message.OrderType ?? OrderTypes.Limit;
        if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
            throw new NotSupportedException(
                "comdirect REST API supports market and limit orders.");
        if (orderType == OrderTypes.Limit && message.Price <= 0)
            throw new InvalidOperationException(
                "comdirect replacement limit must be positive.");

        var instrument = await GetInstrument(
            message.SecurityId.SecurityCode, cancellationToken);
        var currency = instrument?.StaticData?.Currency
            .IsEmpty(DefaultCurrency);
        var response = await Rest.UpdateOrder(orderId, new()
        {
            Limit = orderType == OrderTypes.Limit
                ? message.Price.ToNativeAmount(currency) : null,
            LimitExtension = message.TimeInForce.ToLimitExtension(),
            ValidityType = message.TillDate is null ? "GFD" : "GTD",
            Validity = message.TillDate?.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
        }, cancellationToken);

        _orderTransactions[orderId] = message.TransactionId;
        _trackedOrders.Add(orderId);

        if (response?.Quantity is not null)
        {
            await ProcessOrder(
                response, message.TransactionId, cancellationToken);
        }
        else
        {
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                OriginalTransactionId = message.TransactionId,
                TransactionId = message.TransactionId,
                OrderStringId = orderId,
                PortfolioName = message.PortfolioName,
                SecurityId = message.SecurityId,
                Side = message.Side,
                OrderType = orderType,
                OrderPrice = message.Price,
                OrderVolume = message.Volume,
                OrderState = OrderStates.Pending,
                TimeInForce = message.TimeInForce,
                ExpiryDate = message.TillDate,
                ServerTime = CurrentTime,
            }, cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage message,
        CancellationToken cancellationToken)
    {
        var orderId = GetOrderId(message.OrderStringId,
            message.OrderId, message.OriginalTransactionId);
        await Rest.DeleteOrder(orderId, cancellationToken);
        _orderTransactions[orderId] = message.TransactionId;
        _trackedOrders.Add(orderId);
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

        await SendPortfolioSnapshot(message.TransactionId,
            message.PortfolioName, cancellationToken);
        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId, cancellationToken);
        }
        else
        {
            _portfolioSubscriptionId = message.TransactionId;
            _portfolioNameFilter = message.PortfolioName;
            await SendSubscriptionResultAsync(message, cancellationToken);
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

        if (!message.OrderStringId.IsEmpty())
        {
            var order = await Rest.GetOrder(
                message.OrderStringId, cancellationToken);
            await ProcessOrder(
                order, message.TransactionId, cancellationToken);
            if (!message.IsHistoryOnly())
                _trackedOrders.Add(message.OrderStringId);
        }
        else
        {
            await SendOrderSnapshot(
                message.TransactionId, message, cancellationToken);
        }

        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId, cancellationToken);
        }
        else
        {
            _orderStatusSubscriptionId = message.TransactionId;
            _orderStatusFilter = (OrderStatusMessage)message.Clone();
            await SendSubscriptionResultAsync(message, cancellationToken);
        }
    }

    private async Task SendPortfolioSnapshot(long originalTransactionId,
        string portfolioName, CancellationToken cancellationToken)
    {
        await RefreshDepots(cancellationToken);
        var depots = ResolveDepots(portfolioName);
        var balances = await Rest.GetAccountBalances(cancellationToken);

        foreach (var depot in depots)
        {
            var name = GetPortfolioName(depot);
            await SendOutMessageAsync(new PortfolioMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = name,
                BoardCode = "COMDIRECT",
                Currency = CurrencyTypes.EUR,
            }, cancellationToken);

            foreach (var position in await Rest.GetPositions(
                depot.DepotId, cancellationToken))
            {
                var instrument = position.Instrument ??
                    await GetInstrument(position.Wkn, cancellationToken);
                CacheInstrument(instrument, position.Wkn);
                var securityId = instrument?.ToSecurityId() ??
                    position.Wkn.ToSecurityId("COMDIRECT");
                var quantity = position.Quantity.ToDecimal();
                var available = position.AvailableQuantity.ToDecimal();
                decimal? blocked = quantity is decimal current &&
                    available is decimal free
                        ? Math.Max(0m, current - free) : null;
                var serverTime =
                    position.CurrentPrice?.PriceDateTime.ParseTimestamp(
                        CurrentTime) ?? CurrentTime;
                var currency =
                    position.CurrentPrice?.Price?.Unit
                        .IsEmpty(instrument?.StaticData?.Currency)
                        .IsEmpty(DefaultCurrency);

                await SendOutMessageAsync(new PositionChangeMessage
                {
                    OriginalTransactionId = originalTransactionId,
                    PortfolioName = name,
                    SecurityId = securityId,
                    ServerTime = serverTime,
                }
                .TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
                .TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
                .TryAdd(PositionChangeTypes.AveragePrice,
                    position.PurchasePrice.ToDecimal(), true)
                .TryAdd(PositionChangeTypes.CurrentPrice,
                    position.CurrentPrice?.Price.ToDecimal(), true)
                .TryAdd(PositionChangeTypes.UnrealizedPnL,
                    position.ProfitLossPurchaseAbs.ToDecimal())
                .TryAdd(PositionChangeTypes.Currency,
                    currency.ToCurrency()), cancellationToken);
            }

            var settlementIds = (depot.SettlementAccountIds ?? [])
                .Append(depot.DefaultSettlementAccountId)
                .Where(id => !id.IsEmpty())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var balance in balances.Where(b =>
                settlementIds.Contains(b.AccountId) ||
                settlementIds.Contains(b.Account?.AccountId)))
            {
                var current = balance.Balance.ToDecimal();
                var available = balance.AvailableCashAmount.ToDecimal();
                decimal? blocked = current is decimal total &&
                    available is decimal free && total > free
                        ? total - free : null;
                var currency = balance.Account?.Currency
                    .IsEmpty(balance.Balance?.Unit)
                    .IsEmpty(DefaultCurrency);
                await SendOutMessageAsync(new PositionChangeMessage
                {
                    OriginalTransactionId = originalTransactionId,
                    PortfolioName = name,
                    SecurityId = SecurityId.Money,
                    ServerTime = CurrentTime,
                }
                .TryAdd(PositionChangeTypes.CurrentValue, current, true)
                .TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
                .TryAdd(PositionChangeTypes.Currency,
                    currency.ToCurrency()), cancellationToken);
            }
        }
    }

    private async Task SendOrderSnapshot(long originalTransactionId,
        OrderStatusMessage filter,
        CancellationToken cancellationToken)
    {
        if (filter is null)
            return;

        var orders = new List<ComdirectOrder>();
        foreach (var depot in ResolveDepots(filter.PortfolioName))
        {
            orders.AddRange(await Rest.GetOrders(
                depot.DepotId, cancellationToken));
        }

        var skip = Math.Max(0, filter.Skip ?? 0);
        var left = filter.Count ?? long.MaxValue;
        foreach (var order in orders.OrderBy(o =>
            o.CreationTimestamp.ParseTimestamp(DateTime.MinValue)))
        {
            if (!Matches(order, filter))
                continue;
            if (skip > 0)
            {
                skip--;
                continue;
            }
            await ProcessOrder(
                order, originalTransactionId, cancellationToken);
            if (--left <= 0)
                break;
        }
    }

    private bool Matches(ComdirectOrder order, OrderStatusMessage filter)
    {
        if (order is null)
            return false;
        if (!filter.OrderStringId.IsEmpty() &&
            !filter.OrderStringId.EqualsIgnoreCase(order.OrderId))
            return false;
        if (filter.Side is Sides side &&
            (side == Sides.Buy) != order.Side.EqualsIgnoreCase("BUY"))
            return false;
        if (filter.States?.Length > 0 &&
            !filter.States.Contains(order.OrderStatus.ToOrderState()))
            return false;

        var time = order.CreationTimestamp.ParseTimestamp(
            DateTime.MinValue);
        if (filter.From is DateTime from && time < from)
            return false;
        if (filter.To is DateTime to && time > to)
            return false;

        var instrument = order.Instrument;
        var securityId = instrument?.ToSecurityId(order.VenueId) ??
            order.InstrumentId.ToSecurityId(
                order.VenueId.IsEmpty("COMDIRECT"));
        var securityIds = filter.SecurityIds ?? [];
        if (filter.SecurityId != default)
            securityIds = securityIds.Append(filter.SecurityId).ToArray();
        return securityIds.Length == 0 ||
            securityIds.Any(id =>
                (id.SecurityCode.IsEmpty() ||
                 id.SecurityCode.EqualsIgnoreCase(
                     securityId.SecurityCode) ||
                 id.SecurityCode.EqualsIgnoreCase(
                     order.InstrumentId)) &&
                (id.BoardCode.IsEmpty() ||
                 id.BoardCode.EqualsIgnoreCase(
                     securityId.BoardCode)));
    }

    private async ValueTask ProcessOrder(ComdirectOrder order,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (order?.OrderId.IsEmpty() != false)
            return;

        var instrument = order.Instrument ??
            await GetInstrument(order.InstrumentId, cancellationToken);
        CacheInstrument(instrument, order.InstrumentId);
        var securityId = instrument?.ToSecurityId(order.VenueId) ??
            order.InstrumentId.ToSecurityId(
                order.VenueId.IsEmpty("COMDIRECT"));
        var depot = _depotsById.TryGetValue(
            order.DepotId, out var knownDepot) ? knownDepot : null;
        var portfolioName = depot is null
            ? order.DepotId : GetPortfolioName(depot);
        var state = order.OrderStatus.ToOrderState();
        var localTransactionId =
            _orderTransactions.TryGetValue(
                order.OrderId, out var transactionId)
                ? transactionId : 0;
        var effectiveOriginalId = originalTransactionId == 0
            ? localTransactionId : originalTransactionId;
        var executions = order.Executions ?? [];
        var fingerprint =
            $"{order.OrderStatus}:{order.OpenQuantity?.Value}:" +
            $"{order.ExecutedQuantity?.Value}:" +
            $"{executions.Length}:" +
            $"{executions.LastOrDefault()?.ExecutionTimestamp}";
        var fingerprintKey =
            $"{effectiveOriginalId}:{order.OrderId}";
        if (_orderFingerprints.TryGetValue(
            fingerprintKey, out var previous) &&
            previous == fingerprint)
            return;
        _orderFingerprints[fingerprintKey] = fingerprint;

        var serverTime = order.CreationTimestamp.ParseTimestamp(CurrentTime);
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = effectiveOriginalId,
            TransactionId = localTransactionId,
            OrderStringId = order.OrderId,
            PortfolioName = portfolioName,
            SecurityId = securityId,
            Side = order.Side.EqualsIgnoreCase("BUY")
                ? Sides.Buy : Sides.Sell,
            OrderType = order.OrderType.ToOrderType(),
            OrderPrice = order.Limit.ToDecimal() ?? 0m,
            OrderVolume = order.Quantity.ToDecimal(),
            Balance = order.OpenQuantity.ToDecimal(),
            AveragePrice = order.GetAveragePrice(),
            OrderState = state,
            TimeInForce = order.LimitExtension.ToTimeInForce(),
            ExpiryDate = order.Validity.ParseDate(),
            ServerTime = serverTime,
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    $"comdirect order state is {order.OrderStatus}.")
                : null,
        }, cancellationToken);

        foreach (var execution in executions)
        {
            if (execution?.ExecutionId.IsEmpty() != false)
                continue;
            var executionKey =
                $"{effectiveOriginalId}:{execution.ExecutionId}";
            if (_seenExecutions.Contains(executionKey))
                continue;
            _seenExecutions.Add(executionKey);

            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                OriginalTransactionId = effectiveOriginalId,
                OrderStringId = order.OrderId,
                TradeStringId = execution.ExecutionId,
                PortfolioName = portfolioName,
                SecurityId = securityId,
                Side = order.Side.EqualsIgnoreCase("BUY")
                    ? Sides.Buy : Sides.Sell,
                TradePrice = execution.ExecutionPrice.ToDecimal(),
                TradeVolume = execution.ExecutedQuantity.ToDecimal(),
                ServerTime = execution.ExecutionTimestamp
                    .ParseTimestamp(serverTime),
            }, cancellationToken);
        }

        foreach (var child in order.SubOrders ?? [])
            await ProcessOrder(
                child, originalTransactionId, cancellationToken);

        if (state is OrderStates.Done or OrderStates.Failed)
            _trackedOrders.Remove(order.OrderId);
        else
            _trackedOrders.Add(order.OrderId);
    }

    private static string GetOrderId(string stringId, long? numericId,
        long originalTransactionId)
    {
        if (!stringId.IsEmpty())
            return stringId;
        if (numericId is long value)
            return value.ToString(CultureInfo.InvariantCulture);
        throw new InvalidOperationException(
            LocalizedStrings.OrderNoExchangeId.Put(
                originalTransactionId));
    }
}
