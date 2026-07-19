namespace StockSharp.MercadoBitcoin;

public partial class MercadoBitcoinMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var account = GetAccount(regMsg.PortfolioName);
        var market = GetMarket(regMsg.SecurityId);
        var condition = regMsg.Condition as MercadoBitcoinOrderCondition ?? new();
        var volume = regMsg.Volume.Abs();
        var quoteCost = condition.QuoteCost;
        if (quoteCost is not null &&
            (regMsg.Side != Sides.Buy ||
            (regMsg.OrderType ?? OrderTypes.Limit) != OrderTypes.Market))
            throw new InvalidOperationException(
                "Mercado Bitcoin quote cost is valid only for market buy orders.");
        if (quoteCost is <= 0)
            throw new InvalidOperationException(
                "Mercado Bitcoin quote cost must be positive.");
        if (volume <= 0 && quoteCost is null)
            throw new InvalidOperationException(
                "Mercado Bitcoin order volume must be positive.");
        if (volume > 0)
        {
            ValidateRange(volume, market.MinimumVolume, market.MaximumVolume,
                "volume", market.Symbol);
            ValidateStep(volume, market.VolumeStep, "volume", market.Symbol);
        }
        if (quoteCost is decimal cost)
            ValidateRange(cost, market.MinimumCost, market.MaximumCost,
                "quote cost", market.Symbol);
        if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
            throw new NotSupportedException(
                "Mercado Bitcoin does not document iceberg orders.");
        if (regMsg.TillDate is not null)
            throw new NotSupportedException(
                "Mercado Bitcoin does not support GTD orders.");
        if (regMsg.TimeInForce is not null and not TimeInForce.PutInQueue)
            throw new NotSupportedException(
                "Mercado Bitcoin does not expose a time-in-force parameter.");

        var orderType = regMsg.OrderType ?? OrderTypes.Limit;
        MercadoBitcoinOrderTypes nativeType;
        decimal? limitPrice;
        decimal? stopPrice = null;
        switch (orderType)
        {
            case OrderTypes.Market:
                if (regMsg.PostOnly == true)
                    throw new InvalidOperationException(
                        "A market order cannot be post-only.");
                nativeType = MercadoBitcoinOrderTypes.Market;
                limitPrice = null;
                break;
            case OrderTypes.Limit:
                if (regMsg.Price <= 0)
                    throw new InvalidOperationException(
                        "Mercado Bitcoin limit orders require a positive price.");
                nativeType = regMsg.PostOnly == true
                    ? MercadoBitcoinOrderTypes.PostOnly
                    : MercadoBitcoinOrderTypes.Limit;
                limitPrice = regMsg.Price;
                break;
            case OrderTypes.Conditional:
                if (regMsg.PostOnly == true)
                    throw new InvalidOperationException(
                        "A stop-limit order cannot be post-only.");
                if (regMsg.Price <= 0 || condition.StopPrice is not > 0)
                    throw new InvalidOperationException(
                        "Mercado Bitcoin stop-limit orders require positive limit and trigger prices.");
                nativeType = MercadoBitcoinOrderTypes.StopLimit;
                limitPrice = regMsg.Price;
                stopPrice = condition.StopPrice;
                break;
            default:
                throw new NotSupportedException(
                    LocalizedStrings.OrderUnsupportedType.Put(orderType,
                        regMsg.TransactionId));
        }
        if (limitPrice is decimal price)
        {
            ValidateRange(price, market.MinimumPrice, market.MaximumPrice,
                "price", market.Symbol);
            ValidateStep(price, market.PriceStep, "price", market.Symbol);
        }
        if (stopPrice is decimal trigger)
        {
            ValidateRange(trigger, market.MinimumPrice, market.MaximumPrice,
                "trigger price", market.Symbol);
            ValidateStep(trigger, market.PriceStep, "trigger price",
                market.Symbol);
        }

        var result = await RestClient.PlaceOrderAsync(account.Id, market.Symbol,
            new()
            {
                IsAsync = false,
                Cost = quoteCost,
                ExternalId = regMsg.TransactionId.ToString(
                    CultureInfo.InvariantCulture),
                LimitPrice = limitPrice,
                Quantity = volume > 0 ? volume.ToWire() : null,
                Side = regMsg.Side.ToMercadoBitcoin(),
                StopPrice = stopPrice,
                Type = nativeType,
            }, cancellationToken);
        if (result?.OrderId.IsEmpty() != false)
            throw new InvalidDataException(
                "Mercado Bitcoin returned an empty order identifier.");

        var tracked = new TrackedOrder
        {
            TransactionId = regMsg.TransactionId,
            AccountId = account.Id,
            Symbol = market.Symbol,
            ExchangeOrderId = result.OrderId,
            Side = regMsg.Side,
            OrderType = orderType,
            Volume = volume,
            Price = limitPrice ?? 0m,
            IsPostOnly = nativeType == MercadoBitcoinOrderTypes.PostOnly,
            Condition = condition.Clone() as MercadoBitcoinOrderCondition,
        };
        TrackOrder(tracked, result.OrderId,
            regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));
        await SendTrackedOrderAsync(tracked, account, OrderStates.Active,
            volume, regMsg.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        var orderId = ResolveOrderIdentifier(cancelMsg.OrderId,
            cancelMsg.OrderStringId, "cancellation");
        var tracked = GetTrackedOrder(orderId);
        var account = GetAccount(cancelMsg.PortfolioName.IsEmpty()
            ? tracked?.AccountId
            : cancelMsg.PortfolioName);
        var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? tracked is null
                ? throw new InvalidOperationException(
                    "Mercado Bitcoin cancellation requires the order market when the order was not registered by this adapter.")
                : GetMarket(tracked.Symbol)
            : GetMarket(cancelMsg.SecurityId);
        _ = await RestClient.CancelOrderAsync(account.Id, market.Symbol, orderId,
            new() { IsAsync = false }, cancellationToken);
        await SendCanceledOrderAsync(account, market, orderId,
            cancelMsg.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
            throw new NotSupportedException(
                "Mercado Bitcoin spot accounts do not expose leveraged positions.");
        var accounts = GetSelectedAccounts(cancelMsg.PortfolioName);
        var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(cancelMsg.SecurityId).Symbol;

        foreach (var account in accounts)
        {
            var active = await RestClient.GetAllOrdersAsync(account.Id, new()
            {
                Symbol = symbol,
                Statuses =
                [
                    MercadoBitcoinOrderStatuses.Created,
                    MercadoBitcoinOrderStatuses.Working,
                ],
                Size = 1000,
            }, cancellationToken);
            var orders = (active?.Items ?? []).Where(order => order is not null &&
                (cancelMsg.Side is null ||
                    order.Side.ToStockSharp() == cancelMsg.Side) &&
                (cancelMsg.IsStop is null ||
                    (order.Type == MercadoBitcoinOrderTypes.StopLimit) ==
                    cancelMsg.IsStop.Value)).ToArray();

            if (cancelMsg.Side is null && cancelMsg.IsStop is null)
            {
                _ = await RestClient.CancelAllAsync(account.Id, new()
                {
                    Symbol = symbol,
                }, cancellationToken);
            }
            else
            {
                foreach (var order in orders)
                    _ = await RestClient.CancelOrderAsync(account.Id,
                        order.Instrument, order.Id,
                        new() { IsAsync = false }, cancellationToken);
            }

            foreach (var order in orders)
                await SendCanceledOrderAsync(account,
                    GetMarket(order.Instrument), order.Id,
                    cancelMsg.TransactionId, cancellationToken);
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
                _portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
            return;
        }

        var accounts = GetSelectedAccounts(lookupMsg.PortfolioName);
        foreach (var account in accounts)
        {
            await SendOutMessageAsync(new PortfolioMessage
            {
                PortfolioName = account.PortfolioName,
                BoardCode = BoardCodes.MercadoBitcoin,
                OriginalTransactionId = lookupMsg.TransactionId,
            }, cancellationToken);
            await SendBalanceSnapshotAsync(account, lookupMsg.TransactionId,
                true, cancellationToken);
        }
        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
        if (lookupMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
                cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _portfolioSubscriptions[lookupMsg.TransactionId] =
                [.. accounts.Select(static value => value.Id)];
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

        var accounts = GetSelectedAccounts(statusMsg.PortfolioName);
        var market = statusMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : GetMarket(statusMsg.SecurityId);
        string orderId = null;
        if (statusMsg.HasOrderId())
            orderId = ResolveOrderIdentifier(statusMsg.OrderId,
                statusMsg.OrderStringId, "lookup");
        var tracked = GetTrackedOrder(orderId);
        if (tracked is not null)
        {
            accounts = [GetAccount(tracked.AccountId)];
            market ??= GetMarket(tracked.Symbol);
        }
        var maximum = (statusMsg.Count ?? 500).Min(10000).Max(1).To<int>();

        foreach (var account in accounts)
        {
            if (!orderId.IsEmpty())
            {
                if (market is null)
                    throw new InvalidOperationException(
                        "Mercado Bitcoin order lookup requires the order market when the order was not registered by this adapter.");
                var order = await RestClient.GetOrderAsync(account.Id,
                    market.Symbol, orderId, cancellationToken);
                await SendOrderAsync(account, order, statusMsg.TransactionId,
                    true, cancellationToken);
            }
            else if (market is not null)
            {
                var from = statusMsg.From?.ToUniversalTime();
                var to = statusMsg.To?.ToUniversalTime();
                var orders = await RestClient.GetOrdersAsync(account.Id,
                    market.Symbol, new()
                    {
                        Side = statusMsg.Side?.ToMercadoBitcoin(),
                        CreatedAtFrom = from?.ToUnixSeconds(),
                        CreatedAtTo = to?.ToUnixSeconds(),
                    }, cancellationToken);
                foreach (var order in (orders ?? []).Take(maximum))
                    await SendOrderAsync(account, order,
                        statusMsg.TransactionId, true, cancellationToken);
            }
            else
            {
                var page = await RestClient.GetAllOrdersAsync(account.Id, new()
                {
                    Size = maximum,
                }, cancellationToken);
                foreach (var order in page?.Items ?? [])
                {
                    if (statusMsg.Side is not null &&
                        order.Side.ToStockSharp() != statusMsg.Side)
                        continue;
                    await SendOrderSummaryAsync(account, order,
                        statusMsg.TransactionId, true, cancellationToken);
                }
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
                AccountIds = [.. accounts.Select(static value => value.Id)],
                Symbol = market?.Symbol,
                OrderId = orderId,
                Side = statusMsg.Side,
            };
    }

    private AccountDefinition[] GetSelectedAccounts(string portfolioName)
    {
        if (!portfolioName.IsEmpty())
            return [GetAccount(portfolioName)];
        using (_sync.EnterScope())
        {
            if (!AccountId.IsEmpty() && _accounts.TryGetValue(AccountId,
                out var configured))
                return [configured];
            return [.. _accounts.Values.OrderBy(static value => value.Id,
                StringComparer.OrdinalIgnoreCase)];
        }
    }

    private static void ValidateRange(decimal value, decimal minimum,
        decimal maximum, string name, string symbol)
    {
        if (minimum > 0 && value < minimum)
            throw new InvalidOperationException(
                $"Mercado Bitcoin {name} must be at least {minimum} for '{symbol}'.");
        if (maximum > 0 && value > maximum)
            throw new InvalidOperationException(
                $"Mercado Bitcoin {name} must not exceed {maximum} for '{symbol}'.");
    }

    private static void ValidateStep(decimal value, decimal step, string name,
        string symbol)
    {
        if (step > 0 && value % step != 0)
            throw new InvalidOperationException(
                $"Mercado Bitcoin {name} must be aligned to step {step} for '{symbol}'.");
    }

    private async Task RunPrivatePollingAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                await PollPrivateSubscriptionsAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await SendOutErrorAsync(error, CancellationToken.None);
        }
    }

    private async ValueTask PollPrivateSubscriptionsAsync(
        CancellationToken cancellationToken)
    {
        KeyValuePair<long, string[]>[] portfolioSubscriptions;
        KeyValuePair<long, OrderSubscription>[] orderSubscriptions;
        using (_sync.EnterScope())
        {
            portfolioSubscriptions = [.. _portfolioSubscriptions];
            orderSubscriptions = [.. _orderSubscriptions];
        }

        foreach (var subscription in portfolioSubscriptions)
            foreach (var accountId in subscription.Value)
            {
                try
                {
                    await SendBalanceSnapshotAsync(GetAccount(accountId),
                        subscription.Key, false, cancellationToken);
                }
                catch (Exception error)
                {
                    await SendOutErrorAsync(error, cancellationToken);
                }
            }

        foreach (var subscription in orderSubscriptions)
            foreach (var accountId in subscription.Value.AccountIds)
            {
                try
                {
                    await PollOrderSubscriptionAsync(GetAccount(accountId),
                        subscription.Key, subscription.Value,
                        cancellationToken);
                }
                catch (Exception error)
                {
                    await SendOutErrorAsync(error, cancellationToken);
                }
            }
    }

    private async ValueTask PollOrderSubscriptionAsync(
        AccountDefinition account, long originalTransactionId,
        OrderSubscription subscription, CancellationToken cancellationToken)
    {
        if (!subscription.OrderId.IsEmpty())
        {
            if (subscription.Symbol.IsEmpty())
                return;
            var order = await RestClient.GetOrderAsync(account.Id,
                subscription.Symbol, subscription.OrderId, cancellationToken);
            await SendOrderAsync(account, order, originalTransactionId, false,
                cancellationToken);
            return;
        }

        if (!subscription.Symbol.IsEmpty())
        {
            var orders = await RestClient.GetOrdersAsync(account.Id,
                subscription.Symbol, new()
                {
                    Side = subscription.Side?.ToMercadoBitcoin(),
                }, cancellationToken);
            foreach (var order in orders ?? [])
                await SendOrderAsync(account, order, originalTransactionId, false,
                    cancellationToken);
            return;
        }

        var page = await RestClient.GetAllOrdersAsync(account.Id, new()
        {
            Size = 500,
        }, cancellationToken);
        foreach (var order in page?.Items ?? [])
        {
            if (subscription.Side is not null &&
                order.Side.ToStockSharp() != subscription.Side)
                continue;
            await SendOrderSummaryAsync(account, order, originalTransactionId,
                false, cancellationToken);
        }
    }

    private async ValueTask SendBalanceSnapshotAsync(AccountDefinition account,
        long originalTransactionId, bool force,
        CancellationToken cancellationToken)
    {
        var balances = await RestClient.GetBalancesAsync(account.Id,
            cancellationToken);
        foreach (var balance in balances ?? [])
            await SendBalanceAsync(account, balance, originalTransactionId,
                force, cancellationToken);
    }

    private ValueTask SendBalanceAsync(AccountDefinition account,
        MercadoBitcoinBalance balance, long originalTransactionId, bool force,
        CancellationToken cancellationToken)
    {
        if (balance?.Symbol.IsEmpty() != false)
            return default;
        var fingerprint = new BalanceFingerprint(balance.Available,
            balance.OnHold, balance.Total);
        var key = $"{account.Id}:{balance.Symbol}:{originalTransactionId}";
        using (_sync.EnterScope())
        {
            if (!force && _balanceFingerprints.TryGetValue(key, out var previous) &&
                previous == fingerprint)
                return default;
            _balanceFingerprints[key] = fingerprint;
        }
        return SendOutMessageAsync(new PositionChangeMessage
        {
            PortfolioName = account.PortfolioName,
            SecurityId = new SecurityId
            {
                SecurityCode = balance.Symbol.ToUpperInvariant(),
                BoardCode = BoardCodes.MercadoBitcoin,
            },
            ServerTime = CurrentTime,
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(PositionChangeTypes.CurrentValue, balance.Available, true)
        .TryAdd(PositionChangeTypes.BlockedValue, balance.OnHold, true),
            cancellationToken);
    }

    private ValueTask SendTrackedOrderAsync(TrackedOrder tracked,
        AccountDefinition account, OrderStates state, decimal balance,
        long originalTransactionId, CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = tracked.Symbol.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = account.PortfolioName,
            Side = tracked.Side,
            OrderVolume = tracked.Volume,
            Balance = balance,
            OrderPrice = tracked.Price,
            OrderType = tracked.OrderType,
            OrderState = state,
            OrderStringId = tracked.ExchangeOrderId,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            PostOnly = tracked.IsPostOnly,
            Condition = tracked.Condition,
        }, cancellationToken);

    private async ValueTask SendOrderAsync(AccountDefinition account,
        MercadoBitcoinOrder order, long originalTransactionId, bool force,
        CancellationToken cancellationToken)
    {
        if (order?.Id.IsEmpty() != false || order.Instrument.IsEmpty())
            return;
        var updatedAt = order.UpdatedAtMicroseconds > 0
            ? order.UpdatedAtMicroseconds
            : order.UpdatedAt;
        var fingerprint = new OrderFingerprint(order.Status,
            order.FilledQuantity, updatedAt);
        if (!ShouldSendOrder(account.Id, order.Id, originalTransactionId,
            fingerprint, force))
        {
            foreach (var execution in order.Executions ?? [])
                await SendAccountTradeAsync(account, order, execution,
                    originalTransactionId, cancellationToken);
            return;
        }

        var externalId = order.ExternalId.IsEmpty(order.ExternalIdLegacy);
        var tracked = GetTrackedOrder(order.Id) ?? CreateTrackedOrder(account,
            order.Id, externalId, order.Instrument, order.Side, order.Type,
            order.Quantity, order.LimitPrice, order.StopPrice);
        tracked.ExchangeOrderId = order.Id;
        TrackOrder(tracked, order.Id, externalId);
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.Instrument.ToStockSharp(),
            ServerTime = updatedAt.FromMercadoBitcoinTimestamp(CurrentTime),
            PortfolioName = account.PortfolioName,
            Side = order.Side.ToStockSharp(),
            OrderVolume = order.Quantity,
            Balance = (order.Quantity - order.FilledQuantity).Max(0m),
            OrderPrice = order.LimitPrice,
            AveragePrice = order.AveragePrice > 0 ? order.AveragePrice : null,
            OrderType = order.Type.ToStockSharp(),
            OrderState = order.Status.ToStockSharp(),
            OrderStringId = order.Id,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            PostOnly = order.Type == MercadoBitcoinOrderTypes.PostOnly,
            Condition = CreateCondition(order.Type, order.StopPrice),
            Commission = order.Fee,
        }, cancellationToken);

        foreach (var execution in order.Executions ?? [])
            await SendAccountTradeAsync(account, order, execution,
                originalTransactionId, cancellationToken);
    }

    private async ValueTask SendOrderSummaryAsync(AccountDefinition account,
        MercadoBitcoinOrderSummary order, long originalTransactionId, bool force,
        CancellationToken cancellationToken)
    {
        if (order?.Id.IsEmpty() != false || order.Instrument.IsEmpty())
            return;
        var updatedAt = order.UpdatedAtMicroseconds > 0
            ? order.UpdatedAtMicroseconds
            : order.UpdatedAt;
        var fingerprint = new OrderFingerprint(order.Status,
            order.FilledQuantity, updatedAt);
        if (!ShouldSendOrder(account.Id, order.Id, originalTransactionId,
            fingerprint, force))
            return;
        var externalId = order.ExternalId.IsEmpty(order.ExternalIdCurrent);
        var tracked = GetTrackedOrder(order.Id) ?? CreateTrackedOrder(account,
            order.Id, externalId, order.Instrument, order.Side, order.Type,
            order.Quantity, order.LimitPrice, order.StopPrice);
        TrackOrder(tracked, order.Id, externalId);
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = order.Instrument.ToStockSharp(),
            ServerTime = updatedAt.FromMercadoBitcoinTimestamp(CurrentTime),
            PortfolioName = account.PortfolioName,
            Side = order.Side.ToStockSharp(),
            OrderVolume = order.Quantity,
            Balance = (order.Quantity - order.FilledQuantity).Max(0m),
            OrderPrice = order.LimitPrice,
            OrderType = order.Type.ToStockSharp(),
            OrderState = order.Status.ToStockSharp(),
            OrderStringId = order.Id,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = originalTransactionId,
            PostOnly = order.Type == MercadoBitcoinOrderTypes.PostOnly,
            Condition = CreateCondition(order.Type, order.StopPrice),
        }, cancellationToken);
    }

    private bool ShouldSendOrder(string accountId, string orderId,
        long originalTransactionId, OrderFingerprint fingerprint, bool force)
    {
        var key = $"{accountId}:{orderId}:{originalTransactionId}";
        using (_sync.EnterScope())
        {
            if (!force && _orderFingerprints.TryGetValue(key, out var previous) &&
                previous == fingerprint)
                return false;
            _orderFingerprints[key] = fingerprint;
            return true;
        }
    }

    private TrackedOrder CreateTrackedOrder(AccountDefinition account,
        string orderId, string externalId, string symbol,
        MercadoBitcoinOrderSides side, MercadoBitcoinOrderTypes type,
        decimal volume, decimal price, decimal stopPrice)
    {
        var transactionId = long.TryParse(externalId, NumberStyles.None,
            CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0L;
        return new TrackedOrder
        {
            TransactionId = transactionId,
            AccountId = account.Id,
            Symbol = symbol.NormalizeSymbol(),
            ExchangeOrderId = orderId,
            Side = side.ToStockSharp(),
            OrderType = type.ToStockSharp(),
            Volume = volume,
            Price = price,
            IsPostOnly = type == MercadoBitcoinOrderTypes.PostOnly,
            Condition = CreateCondition(type, stopPrice),
        };
    }

    private async ValueTask SendCanceledOrderAsync(AccountDefinition account,
        MarketDefinition market, string orderId, long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var tracked = GetTrackedOrder(orderId);
        if (tracked is not null)
        {
            await SendTrackedOrderAsync(tracked, account, OrderStates.Done, 0m,
                originalTransactionId, cancellationToken);
            return;
        }
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = market.Symbol.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = account.PortfolioName,
            OrderStringId = orderId,
            OrderState = OrderStates.Done,
            Balance = 0m,
            OriginalTransactionId = originalTransactionId,
        }, cancellationToken);
    }

    private ValueTask SendAccountTradeAsync(AccountDefinition account,
        MercadoBitcoinOrder order, MercadoBitcoinExecution execution,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        if (execution?.Id.IsEmpty() != false || execution.Price <= 0 ||
            execution.Quantity <= 0 ||
            !AddAccountTrade(account.Id, execution.Id, originalTransactionId))
            return default;
        var tracked = GetTrackedOrder(order.Id);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = execution.Instrument.IsEmpty()
                ? order.Instrument.ToStockSharp()
                : execution.Instrument.ToStockSharp(),
            ServerTime = execution.ExecutedAt.FromMercadoBitcoinTimestamp(
                CurrentTime),
            PortfolioName = account.PortfolioName,
            Side = execution.Side.ToStockSharp(),
            OrderStringId = order.Id,
            TradeStringId = execution.Id,
            TradePrice = execution.Price,
            TradeVolume = execution.Quantity,
            Commission = execution.FeeRate > 0
                ? execution.Price * execution.Quantity * execution.FeeRate / 100m
                : null,
            IsMarketMaker = execution.Liquidity switch
            {
                MercadoBitcoinLiquidityTypes.Maker => true,
                MercadoBitcoinLiquidityTypes.Taker => false,
                _ => null,
            },
            TransactionId = tracked?.TransactionId ?? 0,
            OriginalTransactionId = originalTransactionId,
        }, cancellationToken);
    }

    private static MercadoBitcoinOrderCondition CreateCondition(
        MercadoBitcoinOrderTypes type, decimal stopPrice)
        => type == MercadoBitcoinOrderTypes.StopLimit || stopPrice > 0
            ? new() { StopPrice = stopPrice > 0 ? stopPrice : null }
            : null;

    private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
