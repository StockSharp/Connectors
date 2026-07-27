namespace StockSharp.Dnse;

public partial class DnseMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage message,
        CancellationToken cancellationToken)
    {
        if (message.OrderType is not (
            null or OrderTypes.Limit or OrderTypes.Market))
        {
            throw new NotSupportedException(
                "DNSE supports limit, auction, post-limit, and market orders.");
        }

        var condition = message.Condition as DnseOrderCondition;
        var nativeOrderType = (
            condition?.NativeOrderType ?? DnseOrderTypes.Auto)
            .ToNative(message.OrderType, message.TimeInForce);
        if (nativeOrderType == "LO" && message.Price <= 0)
        {
            throw new InvalidOperationException(
                "DNSE limit-order price must be positive.");
        }
        var loanPackageId =
            condition?.LoanPackageId ?? DefaultLoanPackageId;
        if (loanPackageId <= 0)
        {
            throw new InvalidOperationException(
                "DNSE requires a loan package ID. Set the adapter default or DnseOrderCondition.LoanPackageId.");
        }

        var accountNo = ResolveAccount(message.PortfolioName);
        var quantity = ToQuantity(message.Volume);
        CacheSecurity(
            message.SecurityId.ToDnseNative(DefaultBoardId));
        var order = await _rest.CreateOrder(
            new
            {
                accountNo,
                symbol = message.SecurityId.SecurityCode
                    .ThrowIfEmpty(nameof(message.SecurityId.SecurityCode))
                    .ToUpperInvariant(),
                side = message.Side.ToNative(),
                orderType = nativeOrderType,
                price = message.Price,
                quantity,
                loanPackageId,
            },
            RequireTradingToken(),
            cancellationToken);
        if (order?.Id is not > 0)
        {
            throw new InvalidDataException(
                "DNSE accepted an order without returning its ID.");
        }

        FillOrderDefaults(
            order,
            accountNo,
            message.SecurityId.SecurityCode,
            message.Side,
            nativeOrderType,
            message.Price,
            quantity,
            loanPackageId);
        var id = order.Id.ToString(CultureInfo.InvariantCulture);
        _orderTransactions[id] = message.TransactionId;
        _trackedOrders[order.Id] = accountNo;
        await ProcessOrder(
            order, message.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(
        OrderReplaceMessage message,
        CancellationToken cancellationToken)
    {
        var orderId = GetOldOrderId(message);
        var accountNo = ResolveAccount(message.PortfolioName);
        var quantity = ToQuantity(message.Volume);
        CacheSecurity(
            message.SecurityId.ToDnseNative(DefaultBoardId));
        if (message.Price <= 0)
        {
            throw new InvalidOperationException(
                "DNSE replacement price must be positive.");
        }

        var order = await _rest.ReplaceOrder(
            accountNo,
            orderId,
            new
            {
                price = message.Price,
                quantity,
            },
            RequireTradingToken(),
            cancellationToken);
        order ??= new();
        if (order.Id <= 0)
            order.Id = orderId;
        FillOrderDefaults(
            order,
            accountNo,
            message.SecurityId.SecurityCode,
            message.Side,
            "LO",
            message.Price,
            quantity,
            DefaultLoanPackageId);
        var id = order.Id.ToString(CultureInfo.InvariantCulture);
        _orderTransactions[id] = message.TransactionId;
        _trackedOrders[order.Id] = accountNo;
        await ProcessOrder(
            order, message.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage message,
        CancellationToken cancellationToken)
    {
        var orderId = GetOrderId(message);
        var accountNo = ResolveAccount(message.PortfolioName);
        var order = await _rest.CancelOrder(
            accountNo,
            orderId,
            RequireTradingToken(),
            cancellationToken);
        if (order is not null)
        {
            if (order.Id <= 0)
                order.Id = orderId;
            await ProcessOrder(
                order, message.TransactionId, cancellationToken);
        }
        _trackedOrders.Remove(orderId);
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
                _portfolioFilter = null;
            }
            return;
        }

        await SendPortfolioSnapshot(
            message.TransactionId,
            message.PortfolioName,
            cancellationToken);
        if (!message.IsHistoryOnly())
        {
            _portfolioSubscriptionId = message.TransactionId;
            _portfolioFilter = message.PortfolioName;
        }
        await SendSubscriptionResultAsync(
            message, cancellationToken);
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

        if (!message.OrderStringId.IsEmpty() ||
            message.OrderId is not null)
        {
            var orderId = GetOrderId(message);
            var accountNo = ResolveOrderAccount(
                orderId, message.PortfolioName);
            var order = await _rest.GetOrder(
                accountNo, orderId, cancellationToken);
            await ProcessOrder(
                order, message.TransactionId, cancellationToken);
            if (order?.FillQuantity > 0)
            {
                await ProcessExecutions(
                    await _rest.GetExecutions(
                        accountNo, orderId, cancellationToken),
                    message.TransactionId,
                    cancellationToken);
            }
        }
        else
        {
            _orderStatusFilter =
                (OrderStatusMessage)message.Clone();
            await SendOrderSnapshot(
                message.TransactionId, cancellationToken);
        }

        if (!message.IsHistoryOnly())
            _orderStatusSubscriptionId = message.TransactionId;
        await SendSubscriptionResultAsync(
            message, cancellationToken);
    }

    private async ValueTask SendOrderSnapshot(
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var filter = _orderStatusFilter;
        var accounts = SelectAccounts(filter?.PortfolioName);
        var skip = Math.Max(0, filter?.Skip ?? 0);
        var remaining = filter?.Count ?? long.MaxValue;
        foreach (var account in accounts)
        {
            foreach (var order in await _rest.GetOrders(
                account.Id, cancellationToken))
            {
                if (!MatchesOrderFilter(order, filter))
                    continue;
                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                await ProcessOrder(
                    order,
                    originalTransactionId,
                    cancellationToken);
                if (order.FillQuantity > 0)
                {
                    await ProcessExecutions(
                        await _rest.GetExecutions(
                            account.Id,
                            order.Id,
                            cancellationToken),
                        originalTransactionId,
                        cancellationToken);
                }
                if (--remaining <= 0)
                    return;
            }
        }
    }

    private async ValueTask SendPortfolioSnapshot(
        long originalTransactionId,
        string portfolioFilter,
        CancellationToken cancellationToken)
    {
        foreach (var account in SelectAccounts(portfolioFilter))
        {
            await SendOutMessageAsync(
                new PortfolioMessage
                {
                    OriginalTransactionId = originalTransactionId,
                    PortfolioName = account.Id,
                    BoardCode = "HOSE",
                    Currency = CurrencyTypes.VND,
                },
                cancellationToken);

            var balance = await _rest.GetBalances(
                account.Id, cancellationToken);
            if (balance?.Stock is { } stock)
            {
                var current =
                    stock.TotalCash ?? stock.AvailableCash;
                var blocked = stock.OrderSecured ??
                    (stock.TotalCash is decimal total &&
                        stock.AvailableCash is decimal available
                            ? Math.Max(0, total - available)
                            : null);
                await SendOutMessageAsync(
                    new PositionChangeMessage
                    {
                        OriginalTransactionId =
                            originalTransactionId,
                        PortfolioName = account.Id,
                        SecurityId = SecurityId.Money,
                        ServerTime = DateTime.UtcNow,
                    }
                    .TryAdd(
                        PositionChangeTypes.CurrentValue,
                        current,
                        true)
                    .TryAdd(
                        PositionChangeTypes.BlockedValue,
                        blocked,
                        true)
                    .TryAdd(
                        PositionChangeTypes.BuyOrdersMargin,
                        stock.AvailableCash,
                        true)
                    .TryAdd(
                        PositionChangeTypes.Currency,
                        CurrencyTypes.VND),
                    cancellationToken);
            }

            foreach (var position in await _rest.GetPositions(
                account.Id, cancellationToken))
            {
                await ProcessPosition(
                    position,
                    originalTransactionId,
                    cancellationToken,
                    false);
            }
        }
    }

    private async ValueTask ProcessOrder(
        DnseOrder order,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (order?.Id is not > 0)
            return;
        var id = order.Id.ToString(CultureInfo.InvariantCulture);
        var state = order.OrderStatus.ToOrderState();
        var signature =
            $"{order.ModifiedDate}:{order.OrderStatus}:" +
            $"{order.FillQuantity}:{order.LeaveQuantity}:" +
            $"{order.CanceledQuantity}";
        var isChanged =
            !_orderSignatures.TryGetValue(id, out var previous) ||
            previous != signature;
        if (isChanged)
        {
            _orderSignatures[id] = signature;
            if (state is OrderStates.Active or OrderStates.Pending)
                _trackedOrders[order.Id] = order.AccountNo;
            else
                _trackedOrders.Remove(order.Id);

            _orderTransactions.TryGetValue(
                id, out var transactionId);
            var native = await ResolveSecurityAsync(
                order.Symbol, cancellationToken);
            var error = state == OrderStates.Failed
                ? new InvalidOperationException(
                    order.Error.IsEmpty(
                        "DNSE rejected the order."))
                : null;
            await SendOutMessageAsync(
                new ExecutionMessage
                {
                    DataTypeEx = DataType.Transactions,
                    HasOrderInfo = true,
                    OriginalTransactionId =
                        originalTransactionId == 0
                            ? transactionId
                            : originalTransactionId,
                    TransactionId = transactionId,
                    OrderId = order.Id,
                    OrderStringId = id,
                    PortfolioName =
                        ResolvePortfolio(order.AccountNo),
                    SecurityId = native.ToSecurityId(),
                    Side = order.Side.ToSide(),
                    OrderType = order.OrderType.ToOrderType(),
                    TimeInForce =
                        order.OrderType.ToTimeInForce(),
                    OrderPrice = order.Price,
                    OrderVolume = order.Quantity,
                    Balance = order.LeaveQuantity,
                    AveragePrice = order.AveragePrice,
                    OrderState = state,
                    ServerTime = order.ModifiedDate.ToDnseTime(
                        order.CreatedDate.ToDnseTime()),
                    Condition = new DnseOrderCondition
                    {
                        LoanPackageId = order.LoanPackageId,
                        NativeOrderType =
                            ToNativeOrderType(order.OrderType),
                    },
                    Error = error,
                },
                cancellationToken);
        }

        if (order.Reports?.Length > 0 ||
            order.LastQuantity > 0)
        {
            await ProcessExecutions(
                order,
                originalTransactionId,
                cancellationToken);
        }
    }

    private async ValueTask ProcessExecutions(
        DnseOrder details,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (details is null)
            return;
        var reports = details.Reports?.Length > 0
            ? details.Reports
            : details.LastQuantity > 0
                ? [details]
                : [];
        foreach (var report in reports)
        {
            if (report.LastQuantity <= 0)
                continue;
            var orderId = report.Id > 0
                ? report.Id
                : details.Id;
            var accountNo =
                report.AccountNo.IsEmpty(details.AccountNo);
            var symbol =
                report.Symbol.IsEmpty(details.Symbol);
            var modified =
                report.ModifiedDate.IsEmpty(details.ModifiedDate);
            var tradeId =
                $"{orderId}:{modified}:{report.FillQuantity}:" +
                $"{report.LastQuantity}:{report.LastPrice}";
            if (!_seenTrades.TryAdd($"own:{tradeId}"))
                continue;

            var orderStringId =
                orderId.ToString(CultureInfo.InvariantCulture);
            _orderTransactions.TryGetValue(
                orderStringId, out var transactionId);
            var price =
                report.LastPrice ??
                report.AveragePrice ??
                report.Price;
            decimal? commission = report.FeeRate is decimal fee
                ? price * report.LastQuantity * fee
                : null;
            var native = await ResolveSecurityAsync(
                symbol, cancellationToken);
            await SendOutMessageAsync(
                new ExecutionMessage
                {
                    DataTypeEx = DataType.Transactions,
                    OriginalTransactionId =
                        originalTransactionId == 0
                            ? transactionId
                            : originalTransactionId,
                    OrderId = orderId,
                    OrderStringId = orderStringId,
                    TradeStringId = tradeId,
                    PortfolioName =
                        ResolvePortfolio(accountNo),
                    SecurityId = native.ToSecurityId(),
                    Side = report.Side.IsEmpty(details.Side)
                        .ToSide(),
                    TradePrice = price,
                    TradeVolume = report.LastQuantity,
                    Commission = commission,
                    CommissionCurrency =
                        commission is null ? null : "VND",
                    ServerTime = modified.ToDnseTime(),
                },
                cancellationToken);
        }
    }

    private async ValueTask ProcessPosition(
        DnsePosition position,
        long originalTransactionId,
        CancellationToken cancellationToken,
        bool suppressDuplicates = true)
    {
        if (position?.Symbol.IsEmpty() != false)
            return;
        if (!_portfolioFilter.IsEmpty() &&
            !_portfolioFilter.EqualsIgnoreCase(position.AccountNo))
        {
            return;
        }

        var signature =
            $"{position.ModifiedDate}:{position.Status}:" +
            $"{position.OpenQuantity}:{position.TradeQuantity}:" +
            $"{position.MarketPrice}";
        var key =
            $"{position.AccountNo}:{position.Id}:{position.Symbol}";
        if (suppressDuplicates &&
            _positionSignatures.TryGetValue(key, out var previous) &&
            previous == signature)
        {
            return;
        }
        _positionSignatures[key] = signature;

        var quantity = position.OpenQuantity != 0
            ? position.OpenQuantity
            : position.TradeQuantity != 0
                ? position.TradeQuantity
                : position.AccumulateQuantity -
                    position.ClosedQuantity;
        if (position.Side.ToSide() == Sides.Sell)
            quantity = -quantity;
        decimal? unrealized =
            position.MarketPrice is decimal current &&
            position.CostPrice is decimal average
                ? (current - average) * quantity
                : null;

        var native = await ResolveSecurityAsync(
            position.Symbol, cancellationToken);
        await SendOutMessageAsync(
            new PositionChangeMessage
            {
                OriginalTransactionId =
                    originalTransactionId,
                PortfolioName =
                    ResolvePortfolio(position.AccountNo),
                SecurityId = native.ToSecurityId(),
                ServerTime =
                    position.ModifiedDate.ToDnseTime(),
            }
            .TryAdd(
                PositionChangeTypes.CurrentValue,
                quantity,
                true)
            .TryAdd(
                PositionChangeTypes.AveragePrice,
                position.CostPrice,
                true)
            .TryAdd(
                PositionChangeTypes.CurrentPrice,
                position.MarketPrice,
                true)
            .TryAdd(
                PositionChangeTypes.UnrealizedPnL,
                unrealized)
            .TryAdd(
                PositionChangeTypes.Currency,
                CurrencyTypes.VND),
            cancellationToken);
    }

    private ValueTask ProcessAccount(
        DnseAccountUpdate account,
        long originalTransactionId,
        CancellationToken cancellationToken)
    {
        if (account is null)
            return default;
        return SendOutMessageAsync(
            new PositionChangeMessage
            {
                OriginalTransactionId =
                    originalTransactionId,
                PortfolioName =
                    ResolvePortfolio(_selectedAccount),
                SecurityId = SecurityId.Money,
                ServerTime =
                    account.Timestamp.ToDnseTime(),
            }
            .TryAdd(
                PositionChangeTypes.CurrentValue,
                account.Cash,
                true)
            .TryAdd(
                PositionChangeTypes.BuyOrdersMargin,
                account.BuyingPower,
                true)
            .TryAdd(
                PositionChangeTypes.CurrentPrice,
                account.PortfolioValue,
                true)
            .TryAdd(
                PositionChangeTypes.VariationMargin,
                account.Equity,
                true)
            .TryAdd(
                PositionChangeTypes.Currency,
                CurrencyTypes.VND),
            cancellationToken);
    }

    private DnseAccount[] SelectAccounts(string portfolioName)
    {
        if (portfolioName.IsEmpty())
        {
            var stock = _accounts
                .Where(account => account.DealAccount)
                .ToArray();
            return stock.Length > 0 ? stock : _accounts;
        }
        var selected = _accounts
            .Where(account =>
                account.Id.EqualsIgnoreCase(portfolioName))
            .ToArray();
        if (selected.Length == 0)
        {
            throw new InvalidOperationException(
                $"DNSE account '{portfolioName}' is not available.");
        }
        return selected;
    }

    private string ResolveOrderAccount(
        long orderId,
        string portfolioName)
    {
        if (!portfolioName.IsEmpty())
            return ResolveAccount(portfolioName);
        if (_trackedOrders.TryGetValue(orderId, out var accountNo) &&
            !accountNo.IsEmpty())
        {
            return accountNo;
        }
        return ResolveAccount(null);
    }

    private static bool MatchesOrderFilter(
        DnseOrder order,
        OrderStatusMessage filter)
    {
        if (filter is null)
            return true;
        var time = order.ModifiedDate.ToDnseTime(
            order.CreatedDate.ToDnseTime());
        if (filter.From is DateTime from &&
            time < NormalizeOrderTime(from))
        {
            return false;
        }
        if (filter.To is DateTime to &&
            time > NormalizeOrderTime(to))
        {
            return false;
        }
        if (filter.Side is Sides side &&
            order.Side.ToSide() != side)
        {
            return false;
        }
        if (filter.States?.Length > 0 &&
            !filter.States.Contains(
                order.OrderStatus.ToOrderState()))
        {
            return false;
        }
        if (!filter.SecurityId.SecurityCode.IsEmpty() &&
            !filter.SecurityId.SecurityCode.EqualsIgnoreCase(
                order.Symbol))
        {
            return false;
        }
        if (filter.SecurityIds?.Length > 0 &&
            !filter.SecurityIds.Any(id =>
                id.SecurityCode.EqualsIgnoreCase(order.Symbol)))
        {
            return false;
        }
        return filter.PortfolioName.IsEmpty() ||
            filter.PortfolioName.EqualsIgnoreCase(order.AccountNo);
    }

    private SecureString RequireTradingToken()
        => TradingToken.ThrowIfEmpty(nameof(TradingToken));

    private static int ToQuantity(decimal volume)
    {
        if (volume <= 0 ||
            volume != decimal.Truncate(volume) ||
            volume > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(volume),
                volume,
                "DNSE order quantity must be a positive Int32 whole number.");
        }
        return decimal.ToInt32(volume);
    }

    private static long GetOldOrderId(OrderReplaceMessage message)
    {
        if (!message.OldOrderStringId.IsEmpty() &&
            long.TryParse(
                message.OldOrderStringId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var stringId) &&
            stringId > 0)
        {
            return stringId;
        }
        if (message.OldOrderId is > 0)
            return message.OldOrderId.Value;
        throw new InvalidOperationException(
            LocalizedStrings.OrderNoExchangeId.Put(
                message.OriginalTransactionId));
    }

    private static long GetOrderId(OrderCancelMessage message)
    {
        if (!message.OrderStringId.IsEmpty() &&
            long.TryParse(
                message.OrderStringId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var stringId) &&
            stringId > 0)
        {
            return stringId;
        }
        if (message.OrderId is > 0)
            return message.OrderId.Value;
        throw new InvalidOperationException(
            LocalizedStrings.OrderNoExchangeId.Put(
                message.OriginalTransactionId));
    }

    private static long GetOrderId(OrderStatusMessage message)
    {
        if (!message.OrderStringId.IsEmpty() &&
            long.TryParse(
                message.OrderStringId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var stringId) &&
            stringId > 0)
        {
            return stringId;
        }
        if (message.OrderId is > 0)
            return message.OrderId.Value;
        throw new InvalidOperationException(
            LocalizedStrings.OrderNoExchangeId.Put(
                message.OriginalTransactionId));
    }

    private static void FillOrderDefaults(
        DnseOrder order,
        string accountNo,
        string symbol,
        Sides side,
        string nativeOrderType,
        decimal price,
        int quantity,
        int loanPackageId)
    {
        order.AccountNo = order.AccountNo.IsEmpty(accountNo);
        order.Symbol = order.Symbol.IsEmpty(symbol);
        order.Side = order.Side.IsEmpty(side.ToNative());
        order.OrderType =
            order.OrderType.IsEmpty(nativeOrderType);
        order.OrderStatus =
            order.OrderStatus.IsEmpty("Pending");
        if (order.Price == 0)
            order.Price = price;
        if (order.Quantity == 0)
            order.Quantity = quantity;
        if (order.LeaveQuantity == 0 &&
            order.FillQuantity == 0 &&
            order.CanceledQuantity == 0)
        {
            order.LeaveQuantity = quantity;
        }
        if (order.LoanPackageId == 0)
            order.LoanPackageId = loanPackageId;
        order.MarketType =
            order.MarketType.IsEmpty("STOCK");
        order.OrderCategory =
            order.OrderCategory.IsEmpty("NORMAL");
        order.ModifiedDate = order.ModifiedDate.IsEmpty(
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private static DnseOrderTypes ToNativeOrderType(string value)
        => value?.ToUpperInvariant() switch
        {
            "LO" => DnseOrderTypes.Limit,
            "MOK" => DnseOrderTypes.MatchOrKill,
            "MAK" => DnseOrderTypes.MatchAndKill,
            "MTL" => DnseOrderTypes.MarketToLimit,
            "ATO" => DnseOrderTypes.AtOpen,
            "ATC" => DnseOrderTypes.AtClose,
            "PLO" => DnseOrderTypes.PostLimit,
            _ => DnseOrderTypes.Auto,
        };

    private static DateTime NormalizeOrderTime(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
