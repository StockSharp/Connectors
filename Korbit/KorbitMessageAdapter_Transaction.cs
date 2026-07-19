namespace StockSharp.Korbit;

public partial class KorbitMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var market = GetMarket(regMsg.SecurityId);
        var request = await CreateOrderRequestAsync(market, regMsg,
            cancellationToken);
        KorbitPlaceOrderResult result;
        KorbitOrder reconciled = null;
        try
        {
            result = await RestClient.PlaceOrderAsync(request,
                cancellationToken);
        }
        catch (KorbitApiException error) when (
            error.ErrorCode.EqualsIgnoreCase("DUPLICATE_CLIENT_ORDER_ID"))
        {
            reconciled = await RestClient.GetOrderAsync(new()
            {
                Symbol = market.Symbol,
                AccountSequence = AccountSequence,
                ClientOrderId = request.ClientOrderId,
            }, cancellationToken);
            result = new() { OrderId = reconciled.OrderId };
        }
        catch (HttpRequestException error)
        {
            try
            {
                reconciled = await RestClient.GetOrderAsync(new()
                {
                    Symbol = market.Symbol,
                    AccountSequence = AccountSequence,
                    ClientOrderId = request.ClientOrderId,
                }, cancellationToken);
                result = new() { OrderId = reconciled.OrderId };
            }
            catch (Exception reconciliationError)
            {
                throw new InvalidOperationException(
                    $"Korbit order placement had an ambiguous network result. " +
                    $"Reconciliation by clientOrderId '{request.ClientOrderId}' failed.",
                    new AggregateException(error, reconciliationError));
            }
        }

        if (result?.OrderId <= 0)
            throw new InvalidDataException(
                "Korbit accepted an order without returning its identifier.");
        var condition = regMsg.Condition as KorbitOrderCondition;
        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            Symbol = market.Symbol,
            ExchangeOrderId = result.OrderId,
            ClientOrderId = request.ClientOrderId,
            Side = regMsg.Side,
            OrderType = regMsg.OrderType ?? OrderTypes.Limit,
            Volume = regMsg.Volume.Abs(),
            Price = regMsg.Price,
            TimeInForce = request.TimeInForce.ToStockSharp(),
            IsPostOnly = request.TimeInForce == KorbitTimeInForces.PostOnly,
            Condition = condition?.Clone() as KorbitOrderCondition,
        };
        TrackOrder(tracked, result.OrderId.ToString(CultureInfo.InvariantCulture),
            request.ClientOrderId);
        if (reconciled is not null)
            await SendOrderAsync(reconciled, market.Symbol,
                regMsg.TransactionId, cancellationToken);
        else
            await SendTrackedOrderAsync(tracked, OrderStates.Active,
                tracked.Volume, regMsg.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var identifier = ResolveOrderIdentifier(cancelMsg.OrderId,
            cancelMsg.OrderStringId, "cancellation");
        var tracked = GetTrackedOrder(identifier);
        var market = tracked is not null
            ? GetMarket(tracked.Symbol)
            : cancelMsg.SecurityId.SecurityCode.IsEmpty()
                ? throw new InvalidOperationException(
                    "Korbit cancellation requires the order market when the order was not registered by this adapter.")
                : GetMarket(cancelMsg.SecurityId);
        await RestClient.CancelOrderAsync(CreateOrderQuery(market.Symbol,
            identifier), cancellationToken);
        if (tracked is not null)
            await SendTrackedOrderAsync(tracked, OrderStates.Done, 0m,
                cancelMsg.TransactionId, cancellationToken);
        else
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                HasOrderInfo = true,
                SecurityId = market.Symbol.ToStockSharp(),
                ServerTime = CurrentTime,
                PortfolioName = GetPortfolioName(),
                OrderId = long.TryParse(identifier, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var orderId)
                        ? orderId
                        : null,
                UserOrderId = orderId > 0 ? null : identifier,
                OrderState = OrderStates.Done,
                OriginalTransactionId = cancelMsg.TransactionId,
            }, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
            throw new NotSupportedException(
                "Korbit spot bulk cancellation cannot close positions.");
        var markets = GetSelectedMarkets(cancelMsg.SecurityId);
        foreach (var market in markets)
        {
            var orders = await RestClient.GetOpenOrdersAsync(new()
            {
                Symbol = market.Symbol,
                AccountSequence = AccountSequence,
                Limit = 1000,
            }, cancellationToken) ?? [];
            foreach (var order in orders.Where(order => order is not null &&
                (cancelMsg.Side is null ||
                    order.Side.ToStockSharp() == cancelMsg.Side)))
            {
                await RestClient.CancelOrderAsync(new()
                {
                    Symbol = market.Symbol,
                    AccountSequence = AccountSequence,
                    OrderId = order.OrderId,
                }, cancellationToken);
                await SendOrderAsync(order, market.Symbol,
                    cancelMsg.TransactionId, cancellationToken,
                    OrderStates.Done, 0m);
            }
        }
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(
        PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
            cancellationToken);
        EnsurePrivateReady();
        if (!lookupMsg.IsSubscribe)
        {
            using (_sync.EnterScope())
                _portfolioSubscriptions.Remove(
                    lookupMsg.OriginalTransactionId);
            return;
        }

        var portfolio = GetPortfolioName();
        if (lookupMsg.PortfolioName.IsEmpty() ||
            lookupMsg.PortfolioName.EqualsIgnoreCase(portfolio))
        {
            await SendOutMessageAsync(new PortfolioMessage
            {
                PortfolioName = portfolio,
                BoardCode = BoardCodes.Korbit,
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
    protected override async ValueTask OrderStatusAsync(
        OrderStatusMessage statusMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(statusMsg.TransactionId,
            cancellationToken);
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

        var identifier = statusMsg.HasOrderId()
            ? ResolveOrderIdentifier(statusMsg.OrderId,
                statusMsg.OrderStringId, "lookup")
            : null;
        var tracked = GetTrackedOrder(identifier);
        MarketDefinition market = null;
        if (!statusMsg.SecurityId.SecurityCode.IsEmpty())
            market = GetMarket(statusMsg.SecurityId);
        else if (tracked is not null)
            market = GetMarket(tracked.Symbol);

        if (!identifier.IsEmpty())
        {
            if (market is null)
                throw new InvalidOperationException(
                    "Korbit order lookup requires the order market when the order was not registered by this adapter.");
            var order = await RestClient.GetOrderAsync(CreateOrderQuery(
                market.Symbol, identifier), cancellationToken);
            await SendOrderAsync(order, market.Symbol, statusMsg.TransactionId,
                cancellationToken);
        }
        else
        {
            var left = (statusMsg.Count ?? 500).Min(10000).Max(1).To<int>();
            foreach (var selectedMarket in market is null
                ? GetSelectedMarkets(default)
                : [market])
            {
                if (left <= 0)
                    break;
                left -= await SendOrderHistoryAsync(selectedMarket, statusMsg,
                    left, cancellationToken);
            }
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
                OrderIdentifier = identifier,
                Side = statusMsg.Side,
            };
    }

    private async ValueTask<KorbitOrderRequest> CreateOrderRequestAsync(
        MarketDefinition market, OrderRegisterMessage message,
        CancellationToken cancellationToken)
    {
        if (market.Status != KorbitPairStatuses.Launched)
            throw new InvalidOperationException(
                $"Korbit market '{market.Symbol}' is not available for trading.");
        if (message.VisibleVolume is > 0 &&
            message.VisibleVolume != message.Volume.Abs())
            throw new NotSupportedException(
                "Korbit does not document iceberg orders.");
        if (message.TillDate is not null)
            throw new NotSupportedException("Korbit does not support GTD orders.");

        var publicType = message.OrderType ?? OrderTypes.Limit;
        if (publicType is not (OrderTypes.Limit or OrderTypes.Market))
            throw new NotSupportedException(
                LocalizedStrings.OrderUnsupportedType.Put(publicType,
                    message.TransactionId));
        var condition = message.Condition as KorbitOrderCondition;
        var nativeType = condition?.IsBest == true
            ? KorbitOrderTypes.Best
            : publicType == OrderTypes.Market
                ? KorbitOrderTypes.Market
                : KorbitOrderTypes.Limit;
        if (nativeType == KorbitOrderTypes.Best &&
            condition?.BestLevel is not (>= 1 and <= 5))
            throw new InvalidOperationException(
                "Korbit best-price orders require BestLevel from 1 through 5.");
        if (condition?.PriceProtectionPercent is not null and
            not (>= 1 and <= 100))
            throw new InvalidOperationException(
                "Korbit price-protection percent must be from 1 through 100.");
        if (condition?.PriceProtectionPercent is not null &&
            condition.IsPriceProtection != true)
            throw new InvalidOperationException(
                "Enable price protection before setting its threshold.");

        string price = null;
        string quantity = null;
        string amount = null;
        if (nativeType == KorbitOrderTypes.Limit)
        {
            if (message.Price <= 0 || message.Volume == 0)
                throw new InvalidOperationException(
                    "Korbit limit orders require positive price and volume.");
            var volume = message.Volume.Abs();
            ValidateNotional(message.Price * volume, market.Symbol);
            var policy = await GetTickSizePolicyAsync(market,
                cancellationToken);
            ValidateTickSize(message.Price, policy, market.Symbol);
            price = message.Price.ToWire();
            quantity = volume.ToWire();
        }
        else if (message.Side == Sides.Buy)
        {
            var quoteAmount = condition?.QuoteAmount;
            if (quoteAmount is not > 0)
                throw new InvalidOperationException(
                    "Korbit market and best-price buy orders require a positive KorbitOrderCondition.QuoteAmount.");
            ValidateNotional(quoteAmount.Value, market.Symbol);
            amount = quoteAmount.Value.ToWire();
        }
        else
        {
            if (message.Volume == 0)
                throw new InvalidOperationException(
                    "Korbit market and best-price sell orders require positive volume.");
            quantity = message.Volume.Abs().ToWire();
        }

        var timeInForce = message.TimeInForce.ToKorbit(
            message.PostOnly == true, nativeType);
        return new()
        {
            Symbol = market.Symbol,
            AccountSequence = AccountSequence,
            Side = message.Side.ToKorbit(),
            Price = price,
            Quantity = quantity,
            Amount = amount,
            OrderType = nativeType,
            BestLevel = nativeType == KorbitOrderTypes.Best
                ? condition.BestLevel
                : null,
            TimeInForce = timeInForce,
            ClientOrderId = GetClientOrderId(message.TransactionId,
                message.UserOrderId),
            IsPriceProtection = condition?.IsPriceProtection == true,
            PriceProtectionPercent = condition?.PriceProtectionPercent,
        };
    }

    private static void ValidateNotional(decimal notional, string symbol)
    {
        if (notional < 5000m)
            throw new InvalidOperationException(
                $"Korbit order amount must be at least 5,000 KRW for '{symbol}'.");
        if (notional > 1_000_000_000m)
            throw new InvalidOperationException(
                $"Korbit order amount must not exceed 1 billion KRW for '{symbol}'.");
    }

    private static void ValidateTickSize(decimal price,
        KorbitTickSizePolicy policy, string symbol)
    {
        var tier = (policy?.Tiers ?? [])
            .Where(value => value is not null && value.TickSize > 0 &&
                price >= value.PriceGreaterThanOrEqual)
            .OrderByDescending(static value => value.PriceGreaterThanOrEqual)
            .FirstOrDefault() ?? throw new InvalidDataException(
                $"Korbit tick-size policy is invalid for '{symbol}'.");
        if (price % tier.TickSize != 0)
            throw new InvalidOperationException(
                $"Korbit price must be aligned to tick size {tier.TickSize} for '{symbol}'.");
    }

    private KorbitOrderQuery CreateOrderQuery(string symbol, string identifier)
    {
        var request = new KorbitOrderQuery
        {
            Symbol = symbol,
            AccountSequence = AccountSequence,
        };
        if (long.TryParse(identifier, NumberStyles.None,
            CultureInfo.InvariantCulture, out var orderId))
            return new()
            {
                Symbol = request.Symbol,
                AccountSequence = request.AccountSequence,
                OrderId = orderId,
            };
        return new()
        {
            Symbol = request.Symbol,
            AccountSequence = request.AccountSequence,
            ClientOrderId = identifier,
        };
    }

    private MarketDefinition[] GetSelectedMarkets(SecurityId securityId)
    {
        if (!securityId.SecurityCode.IsEmpty())
            return [GetMarket(securityId)];
        using (_sync.EnterScope())
            return [.. _markets.Values.Where(static market =>
                market.Status == KorbitPairStatuses.Launched)
                .OrderBy(static market => market.Symbol,
                    StringComparer.OrdinalIgnoreCase)];
    }

    private async ValueTask<int> SendOrderHistoryAsync(MarketDefinition market,
        OrderStatusMessage message, int maximum,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var to = (message.To ?? now).ToUniversalTime();
        if (to > now)
            to = now;
        var from = message.From?.ToUniversalTime() ??
            to - TimeSpan.FromHours(36);
        if (to - from > TimeSpan.FromHours(36))
            from = to - TimeSpan.FromHours(36);
        var limit = maximum.Min(1000).Max(1);
        var request = new KorbitOrdersQuery
        {
            Symbol = market.Symbol,
            AccountSequence = AccountSequence,
            StartTime = new DateTimeOffset(from).ToUnixTimeMilliseconds(),
            EndTime = new DateTimeOffset(to).ToUnixTimeMilliseconds(),
            Limit = limit,
        };
        var historical = await RestClient.GetAllOrdersAsync(request,
            cancellationToken) ?? [];
        var open = await RestClient.GetOpenOrdersAsync(new()
        {
            Symbol = market.Symbol,
            AccountSequence = AccountSequence,
            Limit = limit,
        }, cancellationToken) ?? [];
        var orders = open.Concat(historical)
            .Where(order => order is not null &&
                (message.Side is null ||
                    order.Side.ToStockSharp() == message.Side))
            .GroupBy(static order => order.OrderId)
            .Select(static group => group.First())
            .OrderByDescending(static order => order.CreatedAt)
            .Take(maximum).ToArray();
        foreach (var order in orders)
            await SendOrderAsync(order, market.Symbol, message.TransactionId,
                cancellationToken);

        var tradeLimit = (maximum - orders.Length).Max(0).Min(1000);
        if (tradeLimit <= 0)
            return orders.Length;
        var trades = await RestClient.GetAccountTradesAsync(new()
        {
            Symbol = market.Symbol,
            AccountSequence = AccountSequence,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Limit = tradeLimit,
        }, cancellationToken) ?? [];
        var sentTrades = 0;
        foreach (var trade in trades.Where(trade => trade is not null &&
            (message.Side is null ||
                trade.Side.ToStockSharp() == message.Side)).Take(tradeLimit))
        {
            await SendAccountTradeAsync(trade.Symbol.IsEmpty()
                ? market.Symbol
                : trade.Symbol, trade.TradeId, trade.OrderId, trade.Side,
                trade.Price, trade.Quantity, trade.TradedAt,
                trade.FeeQuantity, trade.FeeCurrency, trade.IsTaker,
                message.TransactionId, cancellationToken);
            sentTrades++;
        }
        return orders.Length + sentTrades;
    }

    private async ValueTask SendPortfolioSnapshotAsync(
        long originalTransactionId, CancellationToken cancellationToken)
    {
        var balances = await RestClient.GetBalancesAsync(new()
        {
            AccountSequence = AccountSequence,
        }, cancellationToken);
        foreach (var balance in balances ?? [])
            await SendBalanceAsync(balance.Currency, balance.Available,
                balance.TradeInUse + balance.WithdrawalInUse,
                balance.AveragePrice, originalTransactionId, CurrentTime,
                cancellationToken);
    }

    private ValueTask SendBalanceAsync(string currency, decimal available,
        decimal blocked, decimal? averagePrice, long originalTransactionId,
        DateTime serverTime, CancellationToken cancellationToken)
    {
        if (currency.IsEmpty())
            return default;
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = GetPortfolioName(),
            SecurityId = new SecurityId
            {
                SecurityCode = currency.NormalizeCurrency(),
                BoardCode = BoardCodes.Korbit,
            },
            ServerTime = serverTime,
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, available, true)
        .TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
        .TryAdd(PositionChangeTypes.AveragePrice,
            averagePrice is > 0 ? averagePrice : null, true),
            cancellationToken);
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
            UserOrderId = tracked.ClientOrderId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            TimeInForce = tracked.TimeInForce,
            PostOnly = tracked.IsPostOnly,
            Condition = tracked.Condition,
        }, cancellationToken);

    private async ValueTask SendOrderAsync(KorbitOrder order,
        string symbolOverride, long originalTransactionId,
        CancellationToken cancellationToken, OrderStates? state = null,
        decimal? balance = null)
    {
        if (order is null || order.OrderId <= 0)
            return;
        var symbol = order.Symbol.IsEmpty()
            ? symbolOverride.NormalizeSymbol()
            : order.Symbol.NormalizeSymbol();
        _ = GetMarket(symbol);
        var id = order.OrderId.ToString(CultureInfo.InvariantCulture);
        var tracked = GetTrackedOrder(id) ??
            GetTrackedOrder(order.ClientOrderId) ?? new TrackedOrder
            {
                TransactionId = ParseTransactionId(order.ClientOrderId),
                Symbol = symbol,
                ExchangeOrderId = order.OrderId,
                ClientOrderId = order.ClientOrderId,
                Side = order.Side.ToStockSharp(),
                OrderType = order.OrderType.ToStockSharp(),
                Volume = order.Quantity ?? 0m,
                Price = order.Price ?? 0m,
                TimeInForce = order.TimeInForce?.ToStockSharp() ??
                    TimeInForce.PutInQueue,
                IsPostOnly = order.TimeInForce ==
                    KorbitTimeInForces.PostOnly,
                Condition = CreateCondition(order.OrderType, order.Amount),
            };
        tracked.ExchangeOrderId = order.OrderId;
        TrackOrder(tracked, id, order.ClientOrderId);
        var orderState = state ?? order.Status.ToStockSharp();
        var volume = order.Quantity ?? tracked.Volume;
        if (volume <= 0 && order.FilledQuantity > 0)
            volume = order.FilledQuantity;
        var remaining = balance ?? (orderState == OrderStates.Active
            ? (volume - order.FilledQuantity).Max(0m)
            : 0m);
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = symbol.ToStockSharp(),
            ServerTime = (order.LastFilledAt ?? order.CreatedAt)
                .FromKorbitTimestamp(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = order.Side.ToStockSharp(),
            OrderVolume = volume,
            Balance = remaining,
            OrderPrice = order.Price ?? tracked.Price,
            AveragePrice = order.AveragePrice is > 0
                ? order.AveragePrice
                : order.FilledQuantity > 0
                    ? order.FilledAmount / order.FilledQuantity
                    : null,
            OrderType = order.OrderType.ToStockSharp(),
            OrderState = orderState,
            OrderId = order.OrderId,
            UserOrderId = order.ClientOrderId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            TimeInForce = order.TimeInForce?.ToStockSharp() ??
                tracked.TimeInForce,
            PostOnly = order.TimeInForce == KorbitTimeInForces.PostOnly,
            Condition = tracked.Condition ?? CreateCondition(order.OrderType,
                order.Amount),
            Error = orderState == OrderStates.Failed
                ? new InvalidOperationException(
                    "Korbit order expired before it became active.")
                : null,
        }, cancellationToken);
    }

    private ValueTask SendAccountTradeAsync(string symbol, long tradeId,
        long orderId, KorbitOrderSides side, decimal price, decimal quantity,
        long timestamp, decimal? fee, string feeCurrency, bool isTaker,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        if (tradeId <= 0 || orderId <= 0 || price <= 0 || quantity <= 0)
            return default;
        var tracked = GetTrackedOrder(orderId.ToString(
            CultureInfo.InvariantCulture));
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = symbol.ToStockSharp(),
            ServerTime = timestamp.FromKorbitTimestamp(CurrentTime),
            PortfolioName = GetPortfolioName(),
            Side = side.ToStockSharp(),
            OrderId = orderId,
            TradeId = tradeId,
            TradePrice = price,
            TradeVolume = quantity,
            Commission = fee is > 0 ? fee : null,
            CommissionCurrency = feeCurrency,
            IsMarketMaker = !isTaker,
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = originalTransactionId,
        }, cancellationToken);
    }

    private async ValueTask OnMyOrderAsync(KorbitSocketOrderMessage message,
        CancellationToken cancellationToken)
    {
        if (message?.Symbol.IsEmpty() != false || message.Order is null ||
            message.Order.AccountSequence is int account &&
                account != AccountSequence)
            return;
        var symbol = GetMarket(message.Symbol).Symbol;
        foreach (var order in message.Order.Orders ?? [])
        {
            if (order is null || order.OrderId <= 0)
                continue;
            var id = order.OrderId.ToString(CultureInfo.InvariantCulture);
            var tracked = GetTrackedOrder(id) ??
                GetTrackedOrder(order.ClientOrderId) ?? new TrackedOrder
                {
                    TransactionId = ParseTransactionId(order.ClientOrderId),
                    Symbol = symbol,
                    ExchangeOrderId = order.OrderId,
                    ClientOrderId = order.ClientOrderId,
                    Side = order.Side.ToStockSharp(),
                    OrderType = order.OrderType.ToStockSharp(),
                    Volume = order.Quantity ?? 0m,
                    Price = order.Price ?? 0m,
                    TimeInForce = order.TimeInForce?.ToStockSharp() ??
                        TimeInForce.PutInQueue,
                    IsPostOnly = order.TimeInForce ==
                        KorbitTimeInForces.PostOnly,
                    Condition = CreateCondition(order.OrderType, order.Amount),
                };
            tracked.ExchangeOrderId = order.OrderId;
            TrackOrder(tracked, id, order.ClientOrderId);
            var targets = GetOrderTargets(symbol, id, order.ClientOrderId,
                order.Side.ToStockSharp(), tracked.TransactionId);
            var state = order.Status.ToStockSharp();
            var volume = order.Quantity ?? tracked.Volume;
            if (volume <= 0 && order.FilledQuantity > 0)
                volume = order.FilledQuantity;
            foreach (var target in targets)
                await SendOutMessageAsync(new ExecutionMessage
                {
                    DataTypeEx = DataType.Transactions,
                    HasOrderInfo = true,
                    SecurityId = symbol.ToStockSharp(),
                    ServerTime = (order.LastFilledAt ?? order.CreatedAt)
                        .FromKorbitTimestamp(message.Timestamp
                            .FromKorbitTimestamp(CurrentTime)),
                    PortfolioName = GetPortfolioName(),
                    Side = order.Side.ToStockSharp(),
                    OrderVolume = volume,
                    Balance = state == OrderStates.Active
                        ? (volume - order.FilledQuantity).Max(0m)
                        : 0m,
                    OrderPrice = order.Price ?? tracked.Price,
                    AveragePrice = order.AveragePrice is > 0
                        ? order.AveragePrice
                        : order.FilledQuantity > 0
                            ? order.FilledAmount / order.FilledQuantity
                            : null,
                    OrderType = order.OrderType.ToStockSharp(),
                    OrderState = state,
                    OrderId = order.OrderId,
                    UserOrderId = order.ClientOrderId,
                    TransactionId = tracked.TransactionId,
                    OriginalTransactionId = target,
                    TimeInForce = order.TimeInForce?.ToStockSharp() ??
                        tracked.TimeInForce,
                    PostOnly = order.TimeInForce ==
                        KorbitTimeInForces.PostOnly,
                    Condition = tracked.Condition ?? CreateCondition(
                        order.OrderType, order.Amount),
                    Error = state == OrderStates.Failed
                        ? new InvalidOperationException(
                            "Korbit order expired before it became active.")
                        : null,
                }, cancellationToken);
        }
    }

    private async ValueTask OnMyTradeAsync(
        KorbitSocketAccountTradeMessage message,
        CancellationToken cancellationToken)
    {
        if (message?.Symbol.IsEmpty() != false || message.Trade is null ||
            message.Trade.AccountSequence is int account &&
                account != AccountSequence)
            return;
        var symbol = GetMarket(message.Symbol).Symbol;
        foreach (var trade in message.Trade.Trades ?? [])
        {
            if (trade is null || !AddAccountTrade(symbol, trade.TradeId))
                continue;
            var tracked = GetTrackedOrder(trade.OrderId.ToString(
                CultureInfo.InvariantCulture));
            var targets = GetOrderTargets(symbol,
                trade.OrderId.ToString(CultureInfo.InvariantCulture), null,
                trade.Side.ToStockSharp(), tracked?.TransactionId ?? 0);
            foreach (var target in targets)
                await SendAccountTradeAsync(symbol, trade.TradeId,
                    trade.OrderId, trade.Side, trade.Price, trade.Quantity,
                    trade.FilledAt, trade.Fee, trade.FeeCurrency,
                    trade.IsTaker, target, cancellationToken);
        }
    }

    private async ValueTask OnMyAssetAsync(KorbitSocketAssetMessage message,
        CancellationToken cancellationToken)
    {
        if (message?.Asset is null ||
            message.Asset.AccountSequence is int account &&
                account != AccountSequence)
            return;
        long[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _portfolioSubscriptions];
        foreach (var subscriptionId in subscriptions)
            foreach (var asset in message.Asset.Assets ?? [])
                await SendBalanceAsync(asset.Currency, asset.Available,
                    asset.TradeInUse + asset.WithdrawalInUse,
                    asset.AveragePrice, subscriptionId,
                    (asset.UpdatedAt > 0 ? asset.UpdatedAt : message.Timestamp)
                        .FromKorbitTimestamp(CurrentTime), cancellationToken);
    }

    private long[] GetOrderTargets(string symbol, string orderId,
        string clientOrderId, Sides side, long transactionId)
    {
        var targets = new HashSet<long>();
        if (transactionId > 0)
            targets.Add(transactionId);
        using (_sync.EnterScope())
            foreach (var pair in _orderSubscriptions)
                if (MatchesOrderSubscription(pair.Value, symbol, orderId,
                    clientOrderId, side))
                    targets.Add(pair.Key);
        return [.. targets];
    }

    private static KorbitOrderCondition CreateCondition(
        KorbitOrderTypes type, decimal? quoteAmount)
        => type == KorbitOrderTypes.Best || quoteAmount is > 0
            ? new()
            {
                IsBest = type == KorbitOrderTypes.Best,
                QuoteAmount = quoteAmount,
            }
            : null;

    private static bool MatchesOrderSubscription(
        OrderSubscription subscription, string symbol, string orderId,
        string clientOrderId, Sides side)
        => (subscription.Symbol.IsEmpty() ||
            subscription.Symbol.EqualsIgnoreCase(symbol)) &&
            (subscription.OrderIdentifier.IsEmpty() ||
            subscription.OrderIdentifier.EqualsIgnoreCase(orderId) ||
            subscription.OrderIdentifier.EqualsIgnoreCase(clientOrderId)) &&
            (subscription.Side is null || subscription.Side == side);

    private static long ParseTransactionId(string clientOrderId)
    {
        if (clientOrderId?.StartsWith("ss-",
            StringComparison.OrdinalIgnoreCase) != true)
            return 0;
        var end = clientOrderId.IndexOf('-', 3);
        var value = end < 0 ? clientOrderId.AsSpan(3) :
            clientOrderId.AsSpan(3, end - 3);
        return long.TryParse(value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var transactionId)
                ? transactionId
                : 0;
    }

    private async ValueTask RefreshOrderSnapshotsAsync(
        CancellationToken cancellationToken)
    {
        KeyValuePair<long, OrderSubscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _orderSubscriptions];
        foreach (var pair in subscriptions)
        {
            var markets = pair.Value.Symbol.IsEmpty()
                ? GetSelectedMarkets(default)
                : [GetMarket(pair.Value.Symbol)];
            foreach (var market in markets)
            {
                var orders = await RestClient.GetOpenOrdersAsync(new()
                {
                    Symbol = market.Symbol,
                    AccountSequence = AccountSequence,
                    Limit = 1000,
                }, cancellationToken) ?? [];
                foreach (var order in orders.Where(order => order is not null &&
                    MatchesOrderSubscription(pair.Value, market.Symbol,
                        order.OrderId.ToString(CultureInfo.InvariantCulture),
                        order.ClientOrderId, order.Side.ToStockSharp())))
                    await SendOrderAsync(order, market.Symbol, pair.Key,
                        cancellationToken);
            }
        }
    }

    private async ValueTask CompleteOrderStatusAsync(
        OrderStatusMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
