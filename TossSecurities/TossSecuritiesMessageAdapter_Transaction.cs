namespace StockSharp.TossSecurities;

public partial class TossSecuritiesMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage message,
        CancellationToken cancellationToken)
    {
        var accountSequence = ResolveAccountSequence(
            message.PortfolioName);
        var condition = message.Condition as
            TossSecuritiesOrderCondition;

        if (message.OrderType == OrderTypes.Conditional)
        {
            var body = CreateConditionalBody(message, condition);
            var response =
                await _restClient.CreateConditionalOrder(
                    accountSequence,
                    body,
                    cancellationToken);
            var orderId = response?.GetOrderId();
            if (orderId.IsEmpty())
            {
                throw new InvalidDataException(
                    "Toss Securities did not return a conditional-order identifier.");
            }

            RememberOrder(
                orderId,
                message.TransactionId,
                accountSequence,
                true);
            _conditionalSides[orderId] = message.Side;
            await SendOutMessageAsync(
                CreateRegistrationExecution(
                    message,
                    orderId,
                    accountSequence,
                    OrderTypes.Conditional),
                cancellationToken);
            return;
        }

        var orderType = message.OrderType ?? OrderTypes.Limit;
        if (orderType is not (
            OrderTypes.Limit or OrderTypes.Market))
        {
            throw new NotSupportedException(
                "Toss Securities supports market, limit, and conditional orders.");
        }
        if (orderType == OrderTypes.Limit && message.Price <= 0)
        {
            throw new InvalidOperationException(
                "Toss Securities limit-order price must be positive.");
        }
        if (message.TimeInForce is not (
            null or TimeInForce.PutInQueue))
        {
            throw new NotSupportedException(
                "Toss Securities does not support IOC or FOK orders.");
        }

        var orderAmount = condition?.OrderAmount;
        if (orderAmount is not > 0 && message.Volume <= 0)
        {
            throw new InvalidOperationException(
                "Toss Securities order quantity must be positive.");
        }
        if (orderAmount is > 0 && orderType != OrderTypes.Market)
        {
            throw new InvalidOperationException(
                "Toss Securities amount-based US orders must be market orders.");
        }

        var request = new Dictionary<string, object>
        {
            ["clientOrderId"] =
                message.TransactionId.ToString(
                    CultureInfo.InvariantCulture),
            ["symbol"] = RequireSymbol(message.SecurityId),
            ["side"] = message.Side.ToNative(),
            ["orderType"] = orderType == OrderTypes.Market
                ? "MARKET" : "LIMIT",
            ["confirmHighValueOrder"] =
                condition?.ConfirmHighValueOrder == true,
        };
        if (orderAmount is > 0)
            request["orderAmount"] = orderAmount.Value.ToNative();
        else
        {
            request["quantity"] = message.Volume.ToNative();
            request["timeInForce"] = condition?.AtClose == true
                ? "CLS" : "DAY";
        }
        if (orderType == OrderTypes.Limit)
            request["price"] = message.Price.ToNative();

        var result = await _restClient.CreateOrder(
            accountSequence, request, cancellationToken);
        var regularOrderId = result?.GetOrderId();
        if (regularOrderId.IsEmpty())
        {
            throw new InvalidDataException(
                "Toss Securities did not return an order identifier.");
        }

        RememberOrder(
            regularOrderId,
            message.TransactionId,
            accountSequence,
            false);
        await SendOutMessageAsync(
            CreateRegistrationExecution(
                message,
                regularOrderId,
                accountSequence,
                orderType),
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage message,
        CancellationToken cancellationToken)
    {
        var oldOrderId = message.OldOrderStringId;
        if (oldOrderId.IsEmpty() &&
            message.OldOrderId is long numericId)
        {
            oldOrderId = numericId.ToString(
                CultureInfo.InvariantCulture);
        }
        if (oldOrderId.IsEmpty())
        {
            throw new InvalidOperationException(
                LocalizedStrings.OrderNoExchangeId.Put(
                    message.OriginalTransactionId));
        }

        var accountSequence = ResolveAccountSequence(
            message.PortfolioName);
        _trackedOrders.TryGetValue(oldOrderId, out var tracked);
        var isConditional =
            message.OrderType == OrderTypes.Conditional ||
            tracked?.IsConditional == true;
        string orderId;

        if (isConditional)
        {
            var condition = message.Condition as
                TossSecuritiesOrderCondition;
            var response =
                await _restClient.ModifyConditionalOrder(
                    accountSequence,
                    oldOrderId,
                    CreateConditionalBody(message, condition),
                    cancellationToken);
            orderId = response?.GetOrderId().IsEmpty(oldOrderId);
            _conditionalSides[orderId] = message.Side;
        }
        else
        {
            var orderType = message.OrderType ?? OrderTypes.Limit;
            if (orderType is not (
                OrderTypes.Limit or OrderTypes.Market))
            {
                throw new NotSupportedException(
                    "Toss Securities can replace only market or limit orders.");
            }
            if (orderType == OrderTypes.Limit &&
                message.Price <= 0)
            {
                throw new InvalidOperationException(
                    "Toss Securities limit-order price must be positive.");
            }

            var current = await _restClient.GetOrder(
                accountSequence, oldOrderId, cancellationToken);
            var body = new Dictionary<string, object>
            {
                ["orderType"] =
                    orderType == OrderTypes.Market
                        ? "MARKET" : "LIMIT",
                ["confirmHighValueOrder"] =
                    message.Condition is
                        TossSecuritiesOrderCondition { ConfirmHighValueOrder: true },
            };
            if (orderType == OrderTypes.Limit)
                body["price"] = message.Price.ToNative();
            if (!current?.Currency.EqualsIgnoreCase("USD") == true)
            {
                if (message.Volume <= 0)
                {
                    throw new InvalidOperationException(
                        "Toss Securities replacement quantity must be positive.");
                }
                body["quantity"] = message.Volume.ToNative();
            }

            var response = await _restClient.ModifyOrder(
                accountSequence,
                oldOrderId,
                body,
                cancellationToken);
            orderId = response?.GetOrderId().IsEmpty(oldOrderId);
        }

        RememberOrder(
            orderId,
            message.TransactionId,
            accountSequence,
            isConditional);
        if (!orderId.EqualsIgnoreCase(oldOrderId))
            _trackedOrders.Remove(oldOrderId);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage message,
        CancellationToken cancellationToken)
    {
        var orderId = message.OrderStringId;
        if (orderId.IsEmpty() &&
            message.OrderId is long numericId)
        {
            orderId = numericId.ToString(
                CultureInfo.InvariantCulture);
        }
        if (orderId.IsEmpty())
        {
            throw new InvalidOperationException(
                LocalizedStrings.OrderNoExchangeId.Put(
                    message.OriginalTransactionId));
        }

        var accountSequence = ResolveAccountSequence(
            message.PortfolioName);
        _trackedOrders.TryGetValue(orderId, out var tracked);
        if (tracked?.IsConditional == true)
        {
            await _restClient.CancelConditionalOrder(
                accountSequence, orderId, cancellationToken);
            return;
        }

        try
        {
            await _restClient.CancelOrder(
                accountSequence, orderId, cancellationToken);
        }
        catch (HttpRequestException error) when (
            error.StatusCode == HttpStatusCode.NotFound)
        {
            await _restClient.CancelConditionalOrder(
                accountSequence, orderId, cancellationToken);
            _trackedOrders[orderId] = new()
            {
                AccountSequence = accountSequence,
                IsConditional = true,
            };
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
            await SendSpecificOrder(
                message,
                cancellationToken);
        }
        else
        {
            _orderStatusFilter =
                (OrderStatusMessage)message.Clone();
            await SendOrderSnapshot(
                message.TransactionId,
                false,
                cancellationToken);
        }

        if (!message.IsHistoryOnly())
            _orderStatusSubscriptionId = message.TransactionId;
        await SendSubscriptionResultAsync(message, cancellationToken);
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
            }
            return;
        }

        await SendPortfolioSnapshot(
            message.TransactionId, cancellationToken);
        if (!message.IsHistoryOnly())
            _portfolioSubscriptionId = message.TransactionId;
        await SendSubscriptionResultAsync(message, cancellationToken);
    }

    private async ValueTask SendSpecificOrder(
        OrderStatusMessage message,
        CancellationToken cancellationToken)
    {
        var accountSequence = ResolveAccountSequence(
            message.PortfolioName);
        try
        {
            var order = await _restClient.GetOrder(
                accountSequence,
                message.OrderStringId,
                cancellationToken);
            await ProcessOrder(
                order,
                accountSequence,
                message.TransactionId,
                true,
                cancellationToken);
        }
        catch (HttpRequestException error) when (
            error.StatusCode == HttpStatusCode.NotFound)
        {
            var order =
                await _restClient.GetConditionalOrder(
                    accountSequence,
                    message.OrderStringId,
                    cancellationToken);
            await ProcessConditionalOrder(
                order,
                accountSequence,
                message.TransactionId,
                true,
                cancellationToken);
        }
    }

    private async ValueTask SendOrderSnapshot(
        long originalTransactionId,
        bool incremental,
        CancellationToken cancellationToken)
    {
        var filter = _orderStatusFilter;
        if (filter is null)
            return;

        var accountSequences = GetFilteredAccounts(
            filter.PortfolioName);
        var symbols = filter.SecurityIds
            .Select(id => id.SecurityCode)
            .Append(filter.SecurityId.SecurityCode)
            .Where(symbol => !symbol.IsEmpty())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (symbols.Length == 0)
            symbols = [null];

        var states = filter.States ?? [];
        var includeOpen = states.Length == 0 ||
            states.Any(state => state is
                OrderStates.Pending or OrderStates.Active);
        var includeClosed = states.Length == 0 ||
            states.Any(state => state is
                OrderStates.Done or OrderStates.Failed);
        var from = filter.From;
        if (incremental && _lastOrderRefresh != default)
        {
            var refreshDate =
                _lastOrderRefresh.UtcDateTime.Date.AddDays(-1);
            if (from is null || from < refreshDate)
                from = refreshDate;
        }

        var regular = new List<(TossOrder order, long account)>();
        var conditional = new List<(
            TossConditionalOrder order, long account)>();

        foreach (var accountSequence in accountSequences)
        {
            foreach (var symbol in symbols)
            {
                if (includeOpen)
                {
                    await LoadOrders(
                        regular,
                        accountSequence,
                        "OPEN",
                        symbol,
                        from,
                        filter.To,
                        cancellationToken);
                    await LoadConditionalOrders(
                        conditional,
                        accountSequence,
                        "OPEN",
                        symbol,
                        cancellationToken);
                }
                if (includeClosed)
                {
                    await LoadOrders(
                        regular,
                        accountSequence,
                        "CLOSED",
                        symbol,
                        from,
                        filter.To,
                        cancellationToken);
                    await LoadConditionalOrders(
                        conditional,
                        accountSequence,
                        "CLOSED",
                        symbol,
                        cancellationToken);
                }
            }
        }

        var selected = regular
            .Select(item => (
                time: item.order.OrderedAt,
                regular: item.order,
                conditional: (TossConditionalOrder)null,
                item.account))
            .Concat(conditional.Select(item => (
                time: item.order.CreatedAt,
                regular: (TossOrder)null,
                conditional: item.order,
                item.account)))
            .Where(item =>
            {
                var side = item.regular is not null
                    ? item.regular.Side.ToSide()
                    : ResolveConditionalSide(item.conditional);
                var volume = item.regular is not null
                    ? item.regular.Quantity.ToDecimalValue()
                    : item.conditional.Quantity.ToDecimalValue();
                return (filter.Side is null ||
                        side == filter.Side) &&
                    (filter.Volume is null ||
                        volume == filter.Volume);
            });
        var skip = Math.Max(0, filter.Skip ?? 0);
        var count = filter.Count ?? long.MaxValue;
        selected = selected
            .OrderBy(item => item.time)
            .Skip((int)Math.Min(skip, int.MaxValue))
            .Take((int)Math.Min(count, int.MaxValue));

        foreach (var item in selected)
        {
            if (item.regular is not null)
            {
                await ProcessOrder(
                    item.regular,
                    item.account,
                    originalTransactionId,
                    !incremental,
                    cancellationToken);
            }
            else
            {
                await ProcessConditionalOrder(
                    item.conditional,
                    item.account,
                    originalTransactionId,
                    !incremental,
                    cancellationToken);
            }
        }

        _lastOrderRefresh = DateTimeOffset.UtcNow;
    }

    private async Task LoadOrders(
        List<(TossOrder order, long account)> destination,
        long accountSequence,
        string status,
        string symbol,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        string cursor = null;

        do
        {
            var page = await _restClient.GetOrders(
                accountSequence,
                status,
                symbol,
                from,
                to,
                cursor,
                100,
                cancellationToken);
            destination.AddRange(
                (page?.Orders ?? []).Select(
                    order => (order, accountSequence)));
            if (page?.HasNext != true ||
                page.NextCursor.IsEmpty() ||
                page.NextCursor == cursor)
                break;
            cursor = page.NextCursor;
        }
        while (true);
    }

    private async Task LoadConditionalOrders(
        List<(TossConditionalOrder order, long account)> destination,
        long accountSequence,
        string status,
        string symbol,
        CancellationToken cancellationToken)
    {
        string cursor = null;

        do
        {
            var page =
                await _restClient.GetConditionalOrders(
                    accountSequence,
                    status,
                    symbol,
                    cursor,
                    100,
                    cancellationToken);
            destination.AddRange(
                (page?.Orders ?? []).Select(
                    order => (order, accountSequence)));
            if (page?.HasNext != true ||
                page.NextCursor.IsEmpty() ||
                page.NextCursor == cursor)
                break;
            cursor = page.NextCursor;
        }
        while (true);
    }

    private async ValueTask PollTrackedOrders(
        CancellationToken cancellationToken)
    {
        foreach (var pair in _trackedOrders.ToArray())
        {
            try
            {
                if (pair.Value.IsConditional)
                {
                    await ProcessConditionalOrder(
                        await _restClient.GetConditionalOrder(
                            pair.Value.AccountSequence,
                            pair.Key,
                            cancellationToken),
                        pair.Value.AccountSequence,
                        0,
                        false,
                        cancellationToken);
                }
                else
                {
                    await ProcessOrder(
                        await _restClient.GetOrder(
                            pair.Value.AccountSequence,
                            pair.Key,
                            cancellationToken),
                        pair.Value.AccountSequence,
                        0,
                        false,
                        cancellationToken);
                }
            }
            catch (HttpRequestException error) when (
                error.StatusCode == HttpStatusCode.NotFound)
            {
                _trackedOrders.Remove(pair.Key);
            }
        }
    }

    private async ValueTask ProcessOrder(
        TossOrder order,
        long accountSequence,
        long originalTransactionId,
        bool force,
        CancellationToken cancellationToken)
    {
        if (order?.OrderId.IsEmpty() != false)
            return;

        var filled = order.Execution?.FilledQuantity
            .ToDecimalValue() ?? 0;
        var average = order.Execution?.AverageFilledPrice
            .ToDecimalValue() ?? 0;
        var signature =
            $"{order.Status}:{filled}:{average}:{order.CanceledAt:O}";
        if (!force &&
            _orderSignatures.TryGetValue(
                order.OrderId, out var previousSignature) &&
            previousSignature == signature)
            return;
        _orderSignatures[order.OrderId] = signature;

        var transactionId =
            _orderTransactions.TryGetValue2(order.OrderId) ?? 0;
        var state = order.Status.ToOrderState();
        var quantity = order.Quantity.ToDecimalValue() ?? 0;
        var securityId = new SecurityId
        {
            SecurityCode = order.Symbol,
            BoardCode = ((string)null).ToBoard(order.Currency),
        };
        var condition = new TossSecuritiesOrderCondition
        {
            AtClose = order.TimeInForce.EqualsIgnoreCase("CLS"),
            OrderAmount = order.OrderAmount.ToDecimalValue(),
        };
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = originalTransactionId == 0
                ? transactionId : originalTransactionId,
            TransactionId = transactionId,
            OrderStringId = order.OrderId,
            PortfolioName = ResolvePortfolioName(
                accountSequence),
            SecurityId = securityId,
            Side = order.Side.ToSide(),
            OrderType = order.OrderType.EqualsIgnoreCase("MARKET")
                ? OrderTypes.Market : OrderTypes.Limit,
            TimeInForce = TimeInForce.PutInQueue,
            OrderPrice = order.Price.ToDecimalValue() ?? 0,
            OrderVolume = quantity,
            Balance = Math.Max(0, quantity - filled),
            AveragePrice = average > 0 ? average : null,
            OrderState = state,
            ServerTime = (order.CanceledAt ??
                order.Execution?.FilledAt ??
                order.OrderedAt).UtcDateTime,
            Commission =
                (order.Execution?.Commission.ToDecimalValue() ?? 0) +
                (order.Execution?.Tax.ToDecimalValue() ?? 0),
            Condition = condition,
            Error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    "Toss Securities rejected the order.")
                : null,
        }, cancellationToken);

        var previousFill =
            _orderFills.TryGetValue2(order.OrderId) ?? default;
        if (filled > previousFill.filled)
        {
            var delta = filled - previousFill.filled;
            var deltaPrice = average;
            if (delta > 0 && average > 0 &&
                previousFill.filled > 0)
            {
                deltaPrice =
                    (average * filled -
                        previousFill.average *
                        previousFill.filled) / delta;
            }
            var totalCommission =
                (order.Execution?.Commission.ToDecimalValue() ?? 0) +
                (order.Execution?.Tax.ToDecimalValue() ?? 0);
            _orderFills[order.OrderId] = (
                filled,
                average,
                totalCommission);
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                OriginalTransactionId =
                    originalTransactionId == 0
                        ? transactionId
                        : originalTransactionId,
                OrderStringId = order.OrderId,
                TradeStringId =
                    $"{order.OrderId}:{filled.ToString(CultureInfo.InvariantCulture)}",
                PortfolioName = ResolvePortfolioName(
                    accountSequence),
                SecurityId = securityId,
                Side = order.Side.ToSide(),
                TradePrice = deltaPrice,
                TradeVolume = delta,
                Commission = Math.Max(
                    0,
                    totalCommission -
                        previousFill.commission),
                ServerTime = (order.Execution?.FilledAt ??
                    order.OrderedAt).UtcDateTime,
            }, cancellationToken);
        }

        if (state is OrderStates.Done or OrderStates.Failed)
            _trackedOrders.Remove(order.OrderId);
        else
        {
            _trackedOrders[order.OrderId] = new()
            {
                AccountSequence = accountSequence,
                IsConditional = false,
            };
        }
    }

    private async ValueTask ProcessConditionalOrder(
        TossConditionalOrder order,
        long accountSequence,
        long originalTransactionId,
        bool force,
        CancellationToken cancellationToken)
    {
        if (order?.OrderId.IsEmpty() != false)
            return;

        var signature =
            $"{order.Status}:{order.First?.Status}:{order.Second?.Status}:" +
            $"{order.First?.TriggeredOrderId}:{order.Second?.TriggeredOrderId}";
        if (!force &&
            _orderSignatures.TryGetValue(
                order.OrderId, out var previousSignature) &&
            previousSignature == signature)
            return;
        _orderSignatures[order.OrderId] = signature;

        var transactionId =
            _orderTransactions.TryGetValue2(order.OrderId) ?? 0;
        var state = order.Status.ToConditionalOrderState();
        var side = ResolveConditionalSide(order);
        var securityId = new SecurityId
        {
            SecurityCode = order.Symbol,
            BoardCode = order.Market.ToBoard(),
        };
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = originalTransactionId == 0
                ? transactionId : originalTransactionId,
            TransactionId = transactionId,
            OrderStringId = order.OrderId,
            PortfolioName = ResolvePortfolioName(
                accountSequence),
            SecurityId = securityId,
            Side = side,
            OrderType = OrderTypes.Conditional,
            OrderPrice =
                order.First?.OrderPrice.ToDecimalValue() ?? 0,
            OrderVolume = order.Quantity.ToDecimalValue() ?? 0,
            Balance = order.Quantity.ToDecimalValue() ?? 0,
            OrderState = state,
            ServerTime = order.CreatedAt.UtcDateTime,
            ExpiryDate = order.ExpireDate,
            Condition = new TossSecuritiesOrderCondition
            {
                ConditionalType =
                    order.Type.ToConditionalType(),
                TriggerPrice =
                    order.First?.TriggerPrice.ToDecimalValue(),
                SecondSide = order.Second?.OrderSide.IsEmpty() == false
                    ? order.Second.OrderSide.ToSide()
                    : null,
                SecondTriggerPrice =
                    order.Second?.TriggerPrice.ToDecimalValue(),
                SecondOrderPrice =
                    order.Second?.OrderPrice.ToDecimalValue(),
            },
        }, cancellationToken);

        if (state is OrderStates.Done or OrderStates.Failed)
            _trackedOrders.Remove(order.OrderId);
        else
        {
            _trackedOrders[order.OrderId] = new()
            {
                AccountSequence = accountSequence,
                IsConditional = true,
            };
        }
    }

    private async ValueTask SendPortfolioSnapshot(
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        foreach (var accountSequence in GetFilteredAccounts(null))
        {
            var portfolioName =
                ResolvePortfolioName(accountSequence);
            await SendOutMessageAsync(new PortfolioMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = portfolioName,
                BoardCode = "KRX",
            }, cancellationToken);

            var holdings = await _restClient.GetHoldings(
                accountSequence, null, cancellationToken);

            foreach (var holding in holdings?.Items ?? [])
            {
                await SendOutMessageAsync(
                    new PositionChangeMessage
                    {
                        OriginalTransactionId =
                            originalTransactionId,
                        PortfolioName = portfolioName,
                        SecurityId = new()
                        {
                            SecurityCode = holding.Symbol,
                            BoardCode =
                                holding.MarketCountry.ToBoard(
                                    holding.Currency),
                        },
                        ServerTime = CurrentTime,
                    }
                    .TryAdd(
                        PositionChangeTypes.CurrentValue,
                        holding.Quantity.ToDecimalValue(),
                        true)
                    .TryAdd(
                        PositionChangeTypes.AveragePrice,
                        holding.AveragePurchasePrice
                            .ToDecimalValue(),
                        true)
                    .TryAdd(
                        PositionChangeTypes.CurrentPrice,
                        holding.LastPrice.ToDecimalValue(),
                        true)
                    .TryAdd(
                        PositionChangeTypes.UnrealizedPnL,
                        holding.ProfitLoss?.AmountAfterCost
                            .ToDecimalValue() ??
                        holding.ProfitLoss?.Amount
                            .ToDecimalValue())
                    .TryAdd(
                        PositionChangeTypes.Currency,
                        holding.Currency.ToCurrency()),
                    cancellationToken);
            }

            foreach (var currency in new[] { "KRW", "USD" })
            {
                try
                {
                    var buyingPower =
                        await _restClient.GetBuyingPower(
                            accountSequence,
                            currency,
                            cancellationToken);
                    if (buyingPower is null)
                        continue;
                    await SendOutMessageAsync(
                        new PositionChangeMessage
                        {
                            OriginalTransactionId =
                                originalTransactionId,
                            PortfolioName = portfolioName,
                            SecurityId = SecurityId.Money,
                            ServerTime = CurrentTime,
                        }
                        .TryAdd(
                            PositionChangeTypes.CurrentValue,
                            buyingPower.CashBuyingPower
                                .ToDecimalValue(),
                            true)
                        .TryAdd(
                            PositionChangeTypes.UnrealizedPnL,
                            GetCurrencyAmount(
                                holdings?.ProfitLoss
                                    ?.AmountAfterCost,
                                currency))
                        .TryAdd(
                            PositionChangeTypes.Currency,
                            currency.ToCurrency()),
                        cancellationToken);
                }
                catch (HttpRequestException error) when (
                    error.StatusCode is HttpStatusCode.BadRequest or
                        HttpStatusCode.NotFound)
                {
                    // The account may not have a balance in this currency.
                }
            }
        }
    }

    private long[] GetFilteredAccounts(string portfolioName)
    {
        if (!portfolioName.IsEmpty())
            return [ResolveAccountSequence(portfolioName)];
        var accounts = _accounts
            .Select(account => account.AccountSequence)
            .Where(sequence => sequence > 0)
            .Distinct()
            .ToArray();
        if (accounts.Length > 0)
            return accounts;
        return _resolvedAccountSequence > 0
            ? [_resolvedAccountSequence]
            : [];
    }

    private static decimal? GetCurrencyAmount(
        TossCurrencyAmounts amounts,
        string currency)
        => currency.EqualsIgnoreCase("KRW")
            ? amounts?.Krw.ToDecimalValue()
            : amounts?.Usd.ToDecimalValue();

    private Sides ResolveConditionalSide(
        TossConditionalOrder order)
    {
        if (!order?.First?.OrderSide.IsEmpty() == true)
            return order.First.OrderSide.ToSide();
        if (order is not null &&
            _conditionalSides.TryGetValue(
                order.OrderId, out var side))
            return side;
        return Sides.Sell;
    }

    private void RememberOrder(
        string orderId,
        long transactionId,
        long accountSequence,
        bool conditional)
    {
        _orderTransactions[orderId] = transactionId;
        _trackedOrders[orderId] = new()
        {
            AccountSequence = accountSequence,
            IsConditional = conditional,
        };
    }

    private ExecutionMessage CreateRegistrationExecution(
        OrderRegisterMessage message,
        string orderId,
        long accountSequence,
        OrderTypes orderType)
        => new()
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = message.TransactionId,
            TransactionId = message.TransactionId,
            OrderStringId = orderId,
            PortfolioName = ResolvePortfolioName(
                accountSequence),
            SecurityId = message.SecurityId,
            Side = message.Side,
            OrderType = orderType,
            OrderPrice = message.Price,
            OrderVolume = message.Volume,
            Balance = message.Volume,
            OrderState = OrderStates.Pending,
            ServerTime = CurrentTime,
            ExpiryDate = message.TillDate,
            Condition = message.Condition,
        };

    private static Dictionary<string, object>
        CreateConditionalBody(
            OrderRegisterMessage message,
            TossSecuritiesOrderCondition condition)
    {
        if (message.Volume <= 0)
        {
            throw new InvalidOperationException(
                "Toss Securities conditional-order quantity must be positive.");
        }
        if (condition?.TriggerPrice is not > 0)
        {
            throw new InvalidOperationException(
                "Toss Securities conditional orders require a positive trigger price.");
        }
        if (message.TillDate is null)
        {
            throw new InvalidOperationException(
                "Toss Securities conditional orders require an expiry date.");
        }

        var type = condition.ConditionalType;
        var nativeOrderType = message.Price > 0
            ? "LIMIT" : "MARKET";
        if (type is not TossConditionalOrderTypes.Single &&
            nativeOrderType != "LIMIT")
        {
            throw new InvalidOperationException(
                "Toss Securities OCO and OTO orders must be limit orders.");
        }

        var expiry = message.TillDate.Value.IsToday()
            ? DateTime.Today
            : message.TillDate.Value;
        var body = new Dictionary<string, object>
        {
            ["symbol"] = RequireSymbol(message.SecurityId),
            ["type"] = type.ToNative(),
            ["quantity"] = message.Volume.ToNative(),
            ["orderType"] = nativeOrderType,
            ["expireDate"] = expiry.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["first"] = CreateConditionalLeg(
                message.Side,
                condition.TriggerPrice.Value,
                message.Price),
            ["confirmHighValueOrder"] =
                condition.ConfirmHighValueOrder,
        };

        if (type is not TossConditionalOrderTypes.Single)
        {
            if (condition.SecondTriggerPrice is not > 0 ||
                condition.SecondOrderPrice is not > 0)
            {
                throw new InvalidOperationException(
                    "Toss Securities OCO and OTO orders require positive second trigger and order prices.");
            }
            var secondSide = condition.SecondSide ??
                (type == TossConditionalOrderTypes.Oto
                    ? message.Side.Invert()
                    : message.Side);
            body["second"] = CreateConditionalLeg(
                secondSide,
                condition.SecondTriggerPrice.Value,
                condition.SecondOrderPrice.Value);
        }
        return body;
    }

    private static Dictionary<string, object>
        CreateConditionalLeg(
            Sides side,
            decimal triggerPrice,
            decimal orderPrice)
    {
        var leg = new Dictionary<string, object>
        {
            ["orderSide"] = side.ToNative(),
            ["triggerPrice"] = triggerPrice.ToNative(),
        };
        if (orderPrice > 0)
            leg["orderPrice"] = orderPrice.ToNative();
        return leg;
    }
}
