namespace StockSharp.Exante;

public partial class ExanteMessageAdapter
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
                "EXANTE requires a positive order quantity.");
        }

        var orderType = message.OrderType ?? OrderTypes.Limit;
        if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
        {
            throw new NotSupportedException(
                "EXANTE standard order registration supports " +
                "market and limit orders.");
        }
        if (orderType == OrderTypes.Limit && message.Price <= 0)
        {
            throw new InvalidOperationException(
                "EXANTE limit order price must be positive.");
        }

        var account = ResolveAccount(message.PortfolioName);
        var symbol = await GetSymbol(
            message.SecurityId, cancellationToken);
        await EnsurePrivateStreams(cancellationToken);
        var response = await Rest.PlaceOrder(new()
        {
            AccountId = account.AccountId,
            SymbolId = symbol.SymbolId,
            Side = message.Side == Sides.Buy ? "buy" : "sell",
            Quantity = ExanteExtensions.FormatDecimal(message.Volume),
            OrderType = orderType.ToNativeOrderType(),
            LimitPrice = orderType == OrderTypes.Limit
                ? ExanteExtensions.FormatDecimal(message.Price)
                : null,
            Duration = message.TimeInForce.ToNativeDuration(
                message.TillDate),
            GttExpiration = message.TillDate?.ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
            ClientTag = message.TransactionId.ToString(
                CultureInfo.InvariantCulture),
        }, cancellationToken);

        var orderId = response.GetId().ThrowIfEmpty(
            "EXANTE order identifier");
        _orderTransactions[orderId] = message.TransactionId;
        if (!response.CurrentModificationId.IsEmpty())
        {
            _orderTransactions[response.CurrentModificationId] =
                message.TransactionId;
        }
        await ProcessOrder(response, message.TransactionId,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage message,
        CancellationToken cancellationToken)
    {
        var orderId = GetOrderId(message.OldOrderStringId,
            message.OldOrderId, message.OriginalTransactionId);
        var orderType = message.OrderType ?? OrderTypes.Limit;
        if (message.Volume <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message.Volume), message.Volume,
                "EXANTE replacement quantity must be positive.");
        }
        if (orderType == OrderTypes.Limit && message.Price <= 0)
        {
            throw new InvalidOperationException(
                "EXANTE replacement limit price must be positive.");
        }

        await EnsurePrivateStreams(cancellationToken);
        var response = await Rest.ModifyOrder(orderId, new()
        {
            Action = "replace",
            Parameters = new()
            {
                Quantity =
                    ExanteExtensions.FormatDecimal(message.Volume),
                LimitPrice = orderType == OrderTypes.Limit
                    ? ExanteExtensions.FormatDecimal(message.Price)
                    : null,
            },
        }, cancellationToken);

        var responseId = response.GetId().IsEmpty(orderId);
        _orderTransactions[orderId] = message.TransactionId;
        _orderTransactions[responseId] = message.TransactionId;
        if (!response.CurrentModificationId.IsEmpty())
        {
            _orderTransactions[response.CurrentModificationId] =
                message.TransactionId;
        }
        await ProcessOrder(response, message.TransactionId,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage message,
        CancellationToken cancellationToken)
    {
        var orderId = GetOrderId(message.OrderStringId,
            message.OrderId, message.OriginalTransactionId);
        await EnsurePrivateStreams(cancellationToken);
        var response = await Rest.ModifyOrder(orderId, new()
        {
            Action = "cancel",
        }, cancellationToken);
        _orderTransactions[orderId] = message.TransactionId;
        if (response is not null)
        {
            var responseId = response.GetId();
            if (!responseId.IsEmpty())
                _orderTransactions[responseId] = message.TransactionId;
            await ProcessOrder(response, message.TransactionId,
                cancellationToken);
        }
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

        _accounts = await Rest.GetAccounts(cancellationToken);
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

        await EnsurePrivateStreams(cancellationToken);
        if (!message.OrderStringId.IsEmpty())
        {
            var order = await Rest.GetOrder(
                message.OrderStringId, cancellationToken);
            await ProcessOrder(order, message.TransactionId,
                cancellationToken);
        }
        else
        {
            await SendOrderSnapshot(message.TransactionId,
                message, cancellationToken);
        }

        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId, cancellationToken);
        }
        else
        {
            _orderStatusSubscriptionId = message.TransactionId;
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
        foreach (var account in ResolveAccounts(portfolioName))
        {
            var summary = await Rest.GetSummary(
                account.AccountId, SummaryCurrency,
                cancellationToken);
            if (summary is null)
                continue;

            var serverTime = summary.Timestamp > 0
                ? summary.Timestamp.FromUnixMilliseconds()
                : CurrentTime;
            var currency = summary.Currency
                .IsEmpty(SummaryCurrency).ToCurrency();
            await SendOutMessageAsync(new PortfolioMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = account.AccountId,
                BoardCode = "EXANTE",
                Currency = currency,
            }, cancellationToken);

            var netAssetValue =
                summary.NetAssetValue.ToDecimal();
            var freeMoney = summary.FreeMoney.ToDecimal();
            decimal? blocked = null;
            if (netAssetValue is decimal total &&
                freeMoney is decimal available)
            {
                blocked = Math.Max(0m, total - available);
            }
            blocked ??=
                summary.MoneyUsedForMargin.ToDecimal();
            await SendOutMessageAsync(new PositionChangeMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = account.AccountId,
                SecurityId = SecurityId.Money,
                ServerTime = serverTime,
            }
            .TryAdd(PositionChangeTypes.CurrentValue,
                netAssetValue, true)
            .TryAdd(PositionChangeTypes.BlockedValue,
                blocked, true)
            .TryAdd(PositionChangeTypes.Currency,
                currency), cancellationToken);

            foreach (var position in summary.Positions ?? [])
            {
                if (position?.SymbolId.IsEmpty() != false)
                    continue;

                var securityId =
                    _symbols.TryGetValue(position.SymbolId,
                        out var symbol)
                        ? symbol.ToSecurityId()
                        : position.SymbolId.ToSecurityId();
                await SendOutMessageAsync(
                    new PositionChangeMessage
                    {
                        OriginalTransactionId =
                            originalTransactionId,
                        PortfolioName = account.AccountId,
                        SecurityId = securityId,
                        ServerTime = serverTime,
                    }
                    .TryAdd(PositionChangeTypes.CurrentValue,
                        position.Quantity.ToDecimal(), true)
                    .TryAdd(PositionChangeTypes.AveragePrice,
                        position.AveragePrice.ToDecimal(), true)
                    .TryAdd(PositionChangeTypes.CurrentPrice,
                        position.Price.ToDecimal(), true)
                    .TryAdd(PositionChangeTypes.UnrealizedPnL,
                        position.Pnl.ToDecimal())
                    .TryAdd(PositionChangeTypes.Currency,
                        position.Currency.ToCurrency()),
                    cancellationToken);
            }
        }
    }

    private async Task SendOrderSnapshot(
        long originalTransactionId, OrderStatusMessage filter,
        CancellationToken cancellationToken)
    {
        var limit = (int)Math.Clamp(
            (filter.Count ?? 1000) +
            Math.Max(0, filter.Skip ?? 0),
            1, 1000);
        var orders = new Dictionary<string, ExanteOrder>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var account in ResolveAccounts(
            filter.PortfolioName))
        {
            var history = await Rest.GetHistoricalOrders(
                account.AccountId,
                filter.From?.ToUniversalTime(),
                filter.To?.ToUniversalTime(),
                limit, cancellationToken);
            var active = await Rest.GetActiveOrders(
                account.AccountId, limit, cancellationToken);

            foreach (var order in history.Concat(active))
            {
                var id = order.GetId();
                if (!id.IsEmpty())
                    orders[id] = order;
            }
        }

        var skip = Math.Max(0, filter.Skip ?? 0);
        var left = filter.Count ?? long.MaxValue;

        foreach (var order in orders.Values
            .OrderBy(GetOrderTime))
        {
            if (!Matches(order, filter))
                continue;
            if (skip > 0)
            {
                skip--;
                continue;
            }

            await ProcessOrder(order, originalTransactionId,
                cancellationToken);
            if (--left <= 0)
                break;
        }
    }

    private bool Matches(ExanteOrder order,
        OrderStatusMessage filter)
    {
        if (order is null || filter is null)
            return false;

        var id = order.GetId();
        if (!filter.OrderStringId.IsEmpty() &&
            !filter.OrderStringId.EqualsIgnoreCase(id))
            return false;
        if (!filter.PortfolioName.IsEmpty() &&
            !filter.PortfolioName.EqualsIgnoreCase(
                order.AccountId))
            return false;

        var parameters = order.OrderParameters;
        if (filter.Side is Sides side &&
            (side == Sides.Buy) !=
            parameters?.Side.EqualsIgnoreCase("buy"))
            return false;
        if (filter.States?.Length > 0 &&
            !filter.States.Contains(
                (order.OrderState?.Status).ToOrderState()))
            return false;

        var time = GetOrderTime(order);
        if (filter.From is DateTime from &&
            time < from.ToUniversalTime())
            return false;
        if (filter.To is DateTime to &&
            time > to.ToUniversalTime())
            return false;

        var symbolId = GetOrderSymbol(order);
        var securityIds = filter.SecurityIds ?? [];
        if (filter.SecurityId != default)
        {
            securityIds =
                securityIds.Append(filter.SecurityId).ToArray();
        }
        return securityIds.Length == 0 ||
            securityIds.Any(security =>
            {
                var native = security.Native as string;
                return (!native.IsEmpty() &&
                        native.EqualsIgnoreCase(symbolId)) ||
                    security.SecurityCode.IsEmpty() ||
                    security.SecurityCode.EqualsIgnoreCase(symbolId) ||
                    symbolId.StartsWith(
                        security.SecurityCode + ".",
                        StringComparison.OrdinalIgnoreCase);
            });
    }

    private async ValueTask ProcessOrderStream(ExanteOrder order,
        CancellationToken cancellationToken)
    {
        var id = order.GetId();
        if (id.IsEmpty())
            return;

        var hasLocalTransaction =
            _orderTransactions.ContainsKey(id) ||
            (!order.CurrentModificationId.IsEmpty() &&
             _orderTransactions.ContainsKey(
                 order.CurrentModificationId));
        var originalTransactionId =
            _orderStatusSubscriptionId != 0 &&
            Matches(order, _orderStatusFilter)
                ? _orderStatusSubscriptionId
                : 0;
        if (!hasLocalTransaction &&
            originalTransactionId == 0)
            return;

        await ProcessOrder(order, originalTransactionId,
            cancellationToken);
    }

    private async ValueTask ProcessPrivateTradeStream(
        ExantePrivateTrade trade,
        CancellationToken cancellationToken)
    {
        if (trade?.OrderId.IsEmpty() != false)
            return;

        if (!_orders.TryGetValue(trade.OrderId, out var order))
        {
            var hasLocal =
                _orderTransactions.ContainsKey(trade.OrderId);
            if (!hasLocal &&
                _orderStatusSubscriptionId == 0)
                return;
            order = await Rest.GetOrder(
                trade.OrderId, cancellationToken);
        }

        var originalTransactionId =
            _orderStatusSubscriptionId != 0 &&
            Matches(order, _orderStatusFilter)
                ? _orderStatusSubscriptionId
                : 0;
        await ProcessPrivateTrade(trade, order,
            originalTransactionId, cancellationToken);
    }

    private async ValueTask ProcessOrder(ExanteOrder order,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var orderId = order.GetId();
        if (orderId.IsEmpty())
            return;

        _orders[orderId] = order;
        var localTransactionId =
            _orderTransactions.TryGetValue(
                orderId, out var transactionId)
                ? transactionId
                : !order.CurrentModificationId.IsEmpty() &&
                  _orderTransactions.TryGetValue(
                      order.CurrentModificationId,
                      out transactionId)
                    ? transactionId
                    : 0;
        var effectiveOriginalId = originalTransactionId == 0
            ? localTransactionId : originalTransactionId;
        var parameters = order.OrderParameters;
        var state = order.OrderState?.Status.ToOrderState() ??
            OrderStates.Pending;
        var executedVolume = order.GetExecutedVolume() ?? 0m;
        var orderVolume = parameters?.Quantity.ToDecimal();
        decimal? balance = orderVolume is decimal volume
            ? Math.Max(0m, volume - executedVolume)
            : null;
        var serverTime = order.OrderState?.LastUpdate.ParseTimestamp(
            GetOrderTime(order)) ?? GetOrderTime(order);
        var fills = order.OrderState?.Fills ?? [];
        var fingerprint =
            $"{order.OrderState?.Status}:" +
            $"{parameters?.Quantity}:{fills.Length}:" +
            $"{fills.LastOrDefault()?.Position}:" +
            $"{fills.LastOrDefault()?.Timestamp}:" +
            $"{order.OrderState?.Reason}";
        var fingerprintKey =
            $"{effectiveOriginalId}:{orderId}";
        var isNewState =
            !_orderFingerprints.TryGetValue(
                fingerprintKey, out var previous) ||
            previous != fingerprint;
        _orderFingerprints[fingerprintKey] = fingerprint;

        var symbolId = GetOrderSymbol(order);
        var securityId = _symbols.TryGetValue(
            symbolId, out var symbol)
                ? symbol.ToSecurityId()
                : symbolId.ToSecurityId();
        if (isNewState)
        {
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                OriginalTransactionId = effectiveOriginalId,
                TransactionId = localTransactionId,
                OrderStringId = orderId,
                PortfolioName = order.AccountId,
                SecurityId = securityId,
                Side = parameters?.Side.EqualsIgnoreCase("buy") ==
                    true ? Sides.Buy : Sides.Sell,
                OrderType = parameters?.OrderType.ToOrderType() ??
                    OrderTypes.Limit,
                OrderPrice =
                    parameters?.LimitPrice.ToDecimal() ?? 0m,
                OrderVolume = orderVolume,
                Balance = balance,
                AveragePrice = order.GetAveragePrice(),
                OrderState = state,
                TimeInForce =
                    parameters?.Duration.ToTimeInForce(),
                ExpiryDate =
                    parameters?.GttExpiration.ParseTimestamp(),
                ServerTime = serverTime,
                Error = state == OrderStates.Failed
                    ? new InvalidOperationException(
                        order.OrderState?.Reason.IsEmpty(
                            "EXANTE rejected the order."))
                    : null,
            }, cancellationToken);
        }

        foreach (var fill in fills)
        {
            await ProcessFill(fill, order, securityId,
                effectiveOriginalId, cancellationToken);
        }
    }

    private ValueTask ProcessFill(ExanteOrderFill fill,
        ExanteOrder order, SecurityId securityId,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (fill is null)
            return default;

        var orderId = order.GetId();
        var fillId = GetFillId(orderId, fill.Position,
            fill.Timestamp.IsEmpty(fill.Time),
            fill.Quantity, fill.Price);
        var seenKey =
            $"{originalTransactionId}:{fillId}";
        if (_seenTrades.Contains(seenKey))
            return default;
        _seenTrades.Add(seenKey);

        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            OriginalTransactionId = originalTransactionId,
            OrderStringId = orderId,
            TradeStringId = fillId,
            PortfolioName = order.AccountId,
            SecurityId = securityId,
            Side = order.OrderParameters?.Side
                .EqualsIgnoreCase("buy") == true
                    ? Sides.Buy : Sides.Sell,
            TradePrice = fill.Price.ToDecimal(),
            TradeVolume = fill.Quantity.ToDecimal(),
            ServerTime = fill.Timestamp
                .IsEmpty(fill.Time)
                .ParseTimestamp(GetOrderTime(order)),
        }, cancellationToken);
    }

    private ValueTask ProcessPrivateTrade(
        ExantePrivateTrade trade, ExanteOrder order,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var fillId = GetFillId(
            trade.OrderId, trade.Position,
            trade.Timestamp.IsEmpty(trade.Time),
            trade.Quantity, trade.Price);
        var seenKey =
            $"{originalTransactionId}:{fillId}";
        if (_seenTrades.Contains(seenKey))
            return default;
        _seenTrades.Add(seenKey);

        var symbolId = GetOrderSymbol(order);
        var securityId = _symbols.TryGetValue(
            symbolId, out var symbol)
                ? symbol.ToSecurityId()
                : symbolId.ToSecurityId();
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            OriginalTransactionId = originalTransactionId,
            OrderStringId = trade.OrderId,
            TradeStringId = fillId,
            PortfolioName = order?.AccountId,
            SecurityId = securityId,
            Side = order?.OrderParameters?.Side
                .EqualsIgnoreCase("buy") == true
                    ? Sides.Buy : Sides.Sell,
            TradePrice = trade.Price.ToDecimal(),
            TradeVolume = trade.Quantity.ToDecimal(),
            ServerTime = trade.Timestamp
                .IsEmpty(trade.Time)
                .ParseTimestamp(CurrentTime),
        }, cancellationToken);
    }

    private ExanteAccount ResolveAccount(string portfolioName)
    {
        var accounts = ResolveAccounts(portfolioName);
        if (accounts.Length > 0)
            return accounts[0];
        throw new InvalidOperationException(
            portfolioName.IsEmpty()
                ? "EXANTE returned no accessible accounts."
                : $"EXANTE account '{portfolioName}' was not found.");
    }

    private ExanteAccount[] ResolveAccounts(string portfolioName)
    {
        var accounts = _accounts
            .Where(account =>
                account?.AccountId.IsEmpty() == false)
            .ToArray();
        if (portfolioName.IsEmpty())
            return accounts;
        return accounts.Where(account =>
            account.AccountId.EqualsIgnoreCase(portfolioName))
            .ToArray();
    }

    private static string GetOrderSymbol(ExanteOrder order)
        => order?.OrderParameters?.SymbolId
            .IsEmpty(order?.OrderParameters?.Instrument);

    private static DateTime GetOrderTime(ExanteOrder order)
        => order?.PlaceTime.ParseTimestamp(DateTime.MinValue) ??
            DateTime.MinValue;

    private static string GetFillId(string orderId, int position,
        string timestamp, string quantity, string price)
        => $"{orderId}:{position}:{timestamp}:{quantity}:{price}";

    private static string GetOrderId(string stringId,
        long? numericId, long originalTransactionId)
    {
        if (!stringId.IsEmpty())
            return stringId;
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
