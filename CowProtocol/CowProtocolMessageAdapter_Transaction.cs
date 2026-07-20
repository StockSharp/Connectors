namespace StockSharp.CowProtocol;

public partial class CowProtocolMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsureTradingReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var market = GetMarket(regMsg.SecurityId);
        var orderType = regMsg.OrderType ?? OrderTypes.Limit;
        if (orderType is not (OrderTypes.Market or OrderTypes.Limit))
            throw new NotSupportedException(
                "CoW Protocol supports market and limit orders only.");
        if (regMsg.Side is not (Sides.Buy or Sides.Sell))
            throw new ArgumentOutOfRangeException(nameof(regMsg.Side));
        if (regMsg.Condition is not null)
            throw new NotSupportedException(
                "CoW Protocol conditional orders require app-specific logic and " +
                "are not represented as native protocol orders.");
        if (regMsg.PostOnly == true)
            throw new NotSupportedException(
                "Post-only is not defined for CoW Protocol batch auctions.");
        if (regMsg.TimeInForce is not null)
            throw new NotSupportedException(
                "Use TillDate to set the CoW Protocol order expiry.");
        if (!regMsg.UserOrderId.IsEmpty())
            throw new NotSupportedException(
                "CoW Protocol identifies orders by their signed UID and does not " +
                "store a client order identifier.");

        var volume = regMsg.Volume.Abs();
        if (volume <= 0)
            throw new InvalidOperationException(
                "CoW Protocol order volume must be positive.");
        var baseUnits = volume.ToBaseUnits(market.BaseToken.Decimals);
        if (baseUnits <= 0)
            throw new InvalidOperationException(
                "CoW Protocol order volume rounds to zero base units.");

        CowProtocolOrderData orderData;
        long? quoteId = null;
        decimal price;
        if (orderType == OrderTypes.Market)
        {
            var tradeType = regMsg.Side == Sides.Sell
                ? CowProtocolTradeTypes.ExactInput
                : CowProtocolTradeTypes.ExactOutput;
            var quote = await GetQuoteAsync(market, tradeType, baseUnits,
                cancellationToken);
            var sellToken = regMsg.Side == Sides.Sell
                ? market.BaseToken
                : market.QuoteToken;
            var wasApproved = await EnsureApprovalAsync(sellToken,
                RpcClient.GetSpender(), quote.InputAmount, cancellationToken);
            if (wasApproved)
                quote = await GetQuoteAsync(market, tradeType, baseUnits,
                    cancellationToken);
            _ = await EnsureApprovalAsync(sellToken, RpcClient.GetSpender(),
                quote.InputAmount, cancellationToken);
            orderData = CreateOrderData(quote.Parameters);
            quoteId = quote.QuoteId;
            price = GetQuotePrice(market, regMsg.Side, volume, quote);
        }
        else
        {
            if (regMsg.Price <= 0)
                throw new InvalidOperationException(
                    "CoW Protocol limit order price must be positive.");
            orderData = CreateLimitOrderData(market, regMsg.Side, volume,
                regMsg.Price, GetValidTo(regMsg.TillDate));
            var sellToken = regMsg.Side == Sides.Sell
                ? market.BaseToken
                : market.QuoteToken;
            _ = await EnsureApprovalAsync(sellToken, RpcClient.GetSpender(),
                orderData.SellAmount, cancellationToken);
            price = regMsg.Price;
        }

        var signed = RpcClient.SignOrder(orderData);
        var request = CreateOrderCreation(orderData, signed, quoteId);
        var uid = await HttpClient.CreateOrderAsync(request,
            cancellationToken);
        if (!uid.EqualsIgnoreCase(signed.Uid))
            throw new InvalidDataException(
                "CoW Protocol API returned an order UID that does not match " +
                "the signed EIP-712 order.");
        var order = await HttpClient.GetOrderAsync(uid, cancellationToken) ??
            throw new InvalidDataException(
                $"CoW Protocol accepted order '{uid}' but did not return it.");
        var tracked = TrackOrder(order, regMsg.TransactionId, orderType,
            volume, price) ?? throw new InvalidDataException(
                "CoW Protocol returned an order outside the configured market.");
        await SendTrackedSnapshotAsync(tracked, regMsg.TransactionId, true,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
    {
        _ = replaceMsg;
        _ = cancellationToken;
        throw new NotSupportedException(
            "A CoW Protocol order is immutable because its fields form the " +
            "EIP-712 signature. Cancel it and submit a new order.");
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        EnsureTradingReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        if (cancelMsg.OrderId is not null)
            throw new NotSupportedException(
                "CoW Protocol orders use signed UIDs, not numeric identifiers.");
        var uid = cancelMsg.OrderStringId.IsEmpty()
            ? throw new InvalidOperationException(
                "A CoW Protocol order UID is required for cancellation.")
            : cancelMsg.OrderStringId.NormalizeOrderUid();
        var tracked = await CancelUidAsync(uid, cancellationToken);
        await SendTrackedSnapshotAsync(tracked, cancelMsg.TransactionId, true,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsureTradingReady();
        ValidatePortfolio(cancelMsg.PortfolioName);
        if (!cancelMsg.Mode.HasFlag(OrderGroupCancelModes.CancelOrders))
            throw new NotSupportedException(
                "CoW Protocol group cancellation supports open orders only.");
        var orders = await LoadAccountOrdersAsync(10_000,
            cancellationToken);
        var errors = new List<Exception>();
        foreach (var order in orders)
        {
            var tracked = TrackOrder(order);
            if (tracked is null || ToOrderState(order.Status) !=
                OrderStates.Active)
                continue;
            if (cancelMsg.SecurityId != default &&
                cancelMsg.SecurityId.SecurityCode.IsEmpty() == false &&
                !cancelMsg.SecurityId.SecurityCode.EqualsIgnoreCase(
                    tracked.Market.SecurityCode))
                continue;
            if (cancelMsg.Side is Sides side && tracked.Side != side)
                continue;
            try
            {
                _ = await CancelUidAsync(tracked.Uid, cancellationToken);
            }
            catch (Exception error) when (
                !cancellationToken.IsCancellationRequested)
            {
                errors.Add(error);
            }
        }
        if (errors.Count > 0)
            throw errors.Count == 1
                ? errors[0]
                : new AggregateException(
                    "Some CoW Protocol orders could not be cancelled.", errors);
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(
        PortfolioLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        if (!lookupMsg.IsSubscribe)
        {
            using (_sync.EnterScope())
            {
                _portfolioSubscriptions.Remove(
                    lookupMsg.OriginalTransactionId);
                RemoveFingerprintPrefix(_balanceFingerprints,
                    lookupMsg.OriginalTransactionId);
            }
            return;
        }
        ValidatePortfolio(lookupMsg.PortfolioName);
        await SendOutMessageAsync(new PortfolioMessage
        {
            PortfolioName = GetPortfolioName(),
            BoardCode = BoardCodes.CowProtocol,
            OriginalTransactionId = lookupMsg.TransactionId,
        }, cancellationToken);
        await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true,
            cancellationToken);
        if (lookupMsg.IsHistoryOnly())
        {
            await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
                cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _portfolioSubscriptions.Add(lookupMsg.TransactionId);
        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OrderStatusAsync(
        OrderStatusMessage statusMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(statusMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        if (!statusMsg.IsSubscribe)
        {
            RemoveOrderSubscription(statusMsg.OriginalTransactionId);
            return;
        }
        if (statusMsg.Count is <= 0)
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        ValidatePortfolio(statusMsg.PortfolioName);
        if (statusMsg.OrderId is not null)
            throw new NotSupportedException(
                "CoW Protocol orders use signed UIDs, not numeric identifiers.");
        if (!statusMsg.UserId.IsEmpty())
            throw new NotSupportedException(
                "CoW Protocol has no exchange-side user identifier.");
        if (statusMsg.SecurityIds.Length > 0)
            throw new NotSupportedException(
                "Use the primary security filter for CoW Protocol order status.");
        var subscription = new OrderSubscription
        {
            Uid = statusMsg.OrderStringId.IsEmpty()
                ? null
                : statusMsg.OrderStringId.NormalizeOrderUid(),
            SecurityId = statusMsg.SecurityId,
            Side = statusMsg.Side,
            Volume = statusMsg.Volume,
            States = statusMsg.States,
            From = statusMsg.From?.ToUniversalTime(),
            To = statusMsg.To?.ToUniversalTime(),
            Skip = Math.Max(0, statusMsg.Skip ?? 0).Min(int.MaxValue).To<int>(),
            Maximum = (statusMsg.Count ?? 1000).Min(10000).Max(1).To<int>(),
        };
        await SendOrderSnapshotAsync(subscription, statusMsg.TransactionId,
            true, cancellationToken);
        if (statusMsg.IsHistoryOnly())
        {
            await CompleteOrderStatusAsync(statusMsg, cancellationToken);
            return;
        }
        using (_sync.EnterScope())
            _orderSubscriptions[statusMsg.TransactionId] = subscription;
        await SendSubscriptionResultAsync(statusMsg, cancellationToken);
    }

    private async ValueTask<bool> EnsureApprovalAsync(CowProtocolToken token,
        string spender, BigInteger amount,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
            throw new InvalidOperationException(
                "CoW Protocol approval amount must be positive.");
        var allowance = await RpcClient.GetAllowanceAsync(token, spender,
            cancellationToken);
        if (allowance >= amount)
            return false;
        if (!IsAutoApprove)
            throw new InvalidOperationException(
                $"Token '{token.Symbol}' allowance for the official CoW " +
                "Protocol VaultRelayer is insufficient. Approve it manually or " +
                "enable automatic approval.");
        if (allowance > 0)
            await BroadcastAndConfirmAsync(
                RpcClient.CreateApprovalTransaction(token, spender,
                    BigInteger.Zero), "approval reset", cancellationToken);
        await BroadcastAndConfirmAsync(
            RpcClient.CreateApprovalTransaction(token, spender, amount),
            "token approval", cancellationToken);
        return true;
    }

    private async ValueTask BroadcastAndConfirmAsync(
        CowProtocolTransaction transaction, string operation,
        CancellationToken cancellationToken)
    {
        var hash = await RpcClient.SendTransactionAsync(transaction,
            cancellationToken);
        var receipt = await RpcClient.WaitForReceiptAsync(hash,
            TimeSpan.FromMinutes(3), cancellationToken);
        if (!IsSuccessful(receipt))
            throw new InvalidOperationException(
                $"CoW Protocol {operation} transaction '{hash}' reverted.");
    }

    private async ValueTask<TrackedOrder> CancelUidAsync(string uid,
        CancellationToken cancellationToken)
    {
        uid = uid.NormalizeOrderUid();
        var order = await HttpClient.GetOrderAsync(uid, cancellationToken) ??
            throw new InvalidOperationException(
                $"CoW Protocol order '{uid}' was not found.");
        ValidateApiOrder(order);
        if (!order.Owner.NormalizeAddress().EqualsIgnoreCase(
            RpcClient.WalletAddress))
            throw new InvalidOperationException(
                $"CoW Protocol order '{uid}' belongs to another wallet.");
        if (ToOrderState(order.Status) != OrderStates.Active)
            throw new InvalidOperationException(
                $"CoW Protocol order '{uid}' is not open.");
        var signature = RpcClient.SignCancellation(uid);
        await HttpClient.CancelOrderAsync(uid, signature, cancellationToken);
        order = await HttpClient.GetOrderAsync(uid, cancellationToken) ??
            throw new InvalidDataException(
                $"Cancelled CoW Protocol order '{uid}' disappeared.");
        var tracked = TrackOrder(order) ?? throw new InvalidDataException(
            "Cancelled CoW Protocol order is outside the configured market.");
        return tracked;
    }

    private async ValueTask PollPrivateAsync(
        CancellationToken cancellationToken)
    {
        long[] portfolioTargets;
        KeyValuePair<long, OrderSubscription>[] orderTargets;
        TrackedOrder[] active;
        using (_sync.EnterScope())
        {
            portfolioTargets = [.. _portfolioSubscriptions];
            orderTargets = [.. _orderSubscriptions];
            active = [.. _trackedOrders.Values.Where(static order =>
                ToOrderState(order.Order.Status) == OrderStates.Active)];
        }
        if (portfolioTargets.Length > 0)
        {
            var balances = await LoadBalancesAsync(cancellationToken);
            foreach (var target in portfolioTargets)
                await SendPortfolioSnapshotAsync(target, false, balances,
                    cancellationToken);
        }
        foreach (var tracked in active)
        {
            var order = await HttpClient.GetOrderAsync(tracked.Uid,
                cancellationToken);
            if (order is null)
                continue;
            var refreshed = TrackOrder(order) ?? tracked;
            await SendTrackedSnapshotAsync(refreshed,
                refreshed.TransactionId, false, cancellationToken);
        }
        foreach (var target in orderTargets)
            await SendOrderSnapshotAsync(target.Value, target.Key, false,
                cancellationToken);
    }

    private async ValueTask<
        (CowProtocolToken Token, BigInteger Amount)[]> LoadBalancesAsync(
        CancellationToken cancellationToken)
    {
        CowProtocolToken[] tokens;
        using (_sync.EnterScope())
            tokens = [.. _tokens.Values.GroupBy(static token => token.Address,
                    StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())];
        var result = new List<(CowProtocolToken, BigInteger)>();
        foreach (var token in tokens)
            result.Add((token, await RpcClient.GetBalanceAsync(token,
                cancellationToken)));
        return [.. result];
    }

    private async ValueTask SendPortfolioSnapshotAsync(long target,
        bool isForced, CancellationToken cancellationToken)
        => await SendPortfolioSnapshotAsync(target, isForced,
            await LoadBalancesAsync(cancellationToken), cancellationToken);

    private async ValueTask SendPortfolioSnapshotAsync(long target,
        bool isForced,
        (CowProtocolToken Token, BigInteger Amount)[] balances,
        CancellationToken cancellationToken)
    {
        foreach (var item in balances)
        {
            var current = item.Amount.FromBaseUnits(item.Token.Decimals);
            var fingerprint = new BalanceFingerprint(current, 0m);
            var key = $"{target}:{item.Token.Address}";
            using (_sync.EnterScope())
            {
                if (!isForced && _balanceFingerprints.TryGetValue(key,
                    out var previous) && previous == fingerprint)
                    continue;
                _balanceFingerprints[key] = fingerprint;
            }
            await SendOutMessageAsync(new PositionChangeMessage
            {
                PortfolioName = GetPortfolioName(),
                SecurityId = new()
                {
                    SecurityCode = item.Token.Symbol,
                    BoardCode = BoardCodes.CowProtocol,
                },
                ServerTime = CurrentTime,
                OriginalTransactionId = target,
            }
            .TryAdd(PositionChangeTypes.CurrentValue, current, true)
            .TryAdd(PositionChangeTypes.BlockedValue, 0m, true),
                cancellationToken);
        }
    }

    private async ValueTask SendOrderSnapshotAsync(
        OrderSubscription subscription, long target, bool isForced,
        CancellationToken cancellationToken)
    {
        CowProtocolOrder[] orders;
        if (!subscription.Uid.IsEmpty())
        {
            var order = await HttpClient.GetOrderAsync(subscription.Uid,
                cancellationToken);
            orders = order is null ? [] : [order];
        }
        else
        {
            orders = await LoadAccountOrdersAsync(10_000,
                cancellationToken);
        }
        var trackedOrders = orders.Select(order => TrackOrder(order))
            .Where(static order => order is not null)
            .Where(order => Matches(subscription, order))
            .OrderBy(static order => order.SubmittedTime)
            .ToArray();
        var skipped = 0;
        var delivered = 0;
        foreach (var tracked in trackedOrders)
        {
            var state = ToOrderState(tracked.Order.Status);
            if (subscription.States is { Length: > 0 } states &&
                !states.Contains(state))
                continue;
            if (skipped++ < subscription.Skip)
                continue;
            if (delivered++ >= subscription.Maximum)
                break;
            await SendTrackedSnapshotAsync(tracked, target, isForced,
                cancellationToken);
        }
    }

    private async ValueTask<CowProtocolOrder[]> LoadAccountOrdersAsync(
        int maximum, CancellationToken cancellationToken)
    {
        if (maximum is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        var result = new List<CowProtocolOrder>();
        const int pageSize = 1000;
        for (var offset = 0; result.Count < maximum; offset += pageSize)
        {
            var page = await HttpClient.GetOrdersAsync(RpcClient.WalletAddress,
                offset, pageSize, cancellationToken) ?? [];
            result.AddRange(page);
            if (page.Length < pageSize)
                break;
        }
        return [.. result.Take(maximum)];
    }

    private TrackedOrder TrackOrder(CowProtocolOrder order,
        long transactionId = 0, OrderTypes? orderType = null,
        decimal? volume = null, decimal? price = null)
    {
        ValidateApiOrder(order);
        var market = GetMarketByTokens(order.SellToken, order.BuyToken);
        if (market is null)
            return null;
        var isSell = order.SellToken.NormalizeAddress().EqualsIgnoreCase(
            market.BaseToken.Address);
        var side = isSell ? Sides.Sell : Sides.Buy;
        var sell = order.SellAmount.ParseInteger();
        var buy = order.BuyAmount.ParseInteger();
        var calculatedVolume = isSell
            ? sell.FromBaseUnits(market.BaseToken.Decimals)
            : buy.FromBaseUnits(market.BaseToken.Decimals);
        var calculatedQuote = isSell
            ? buy.FromBaseUnits(market.QuoteToken.Decimals)
            : sell.FromBaseUnits(market.QuoteToken.Decimals);
        if (calculatedVolume <= 0 || calculatedQuote <= 0)
            throw new InvalidDataException(
                $"CoW Protocol order '{order.Uid}' has non-positive amounts.");
        var submitted = order.CreationDate.ParseApiTime(
            "order creation time");
        TrackedOrder existing;
        using (_sync.EnterScope())
            _trackedOrders.TryGetValue(order.Uid, out existing);
        var tracked = new TrackedOrder
        {
            TransactionId = transactionId != 0
                ? transactionId
                : existing?.TransactionId ?? 0,
            Uid = order.Uid.NormalizeOrderUid(),
            Market = market,
            Side = side,
            Volume = volume ?? existing?.Volume ?? calculatedVolume,
            Price = price ?? existing?.Price ??
                calculatedQuote / calculatedVolume,
            OrderType = orderType ?? existing?.OrderType ??
                (order.OrderClass.EqualsIgnoreCase("market")
                    ? OrderTypes.Market
                    : OrderTypes.Limit),
            SubmittedTime = existing?.SubmittedTime ?? submitted,
            Order = order,
        };
        using (_sync.EnterScope())
            _trackedOrders[tracked.Uid] = tracked;
        return tracked;
    }

    private async ValueTask SendTrackedSnapshotAsync(TrackedOrder tracked,
        long target, bool isForced, CancellationToken cancellationToken)
    {
        var state = ToOrderState(tracked.Order.Status);
        var filled = GetFilledVolume(tracked);
        var balance = state == OrderStates.Active
            ? (tracked.Volume - filled).Max(0m)
            : 0m;
        var fingerprint = new OrderFingerprint(state, balance, filled);
        var key = $"{target}:{tracked.Uid}";
        var isChanged = false;
        using (_sync.EnterScope())
        {
            isChanged = isForced || !_orderFingerprints.TryGetValue(key,
                out var previous) || previous != fingerprint;
            if (isChanged)
                _orderFingerprints[key] = fingerprint;
        }
        if (isChanged)
            await SendOrderAsync(tracked, target, state, balance,
                cancellationToken);
        await SendOrderTradesAsync(tracked, target, cancellationToken);
    }

    private ValueTask SendOrderAsync(TrackedOrder tracked, long target,
        OrderStates state, decimal balance,
        CancellationToken cancellationToken)
    {
        var commission = GetOrderCommission(tracked,
            out var commissionCurrency);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = tracked.Market.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = tracked.Side,
            OrderVolume = tracked.Volume,
            Balance = balance,
            OrderPrice = tracked.Price,
            OrderType = tracked.OrderType,
            OrderState = state,
            OrderStringId = tracked.Uid,
            TransactionId = tracked.TransactionId,
            OriginalTransactionId = target,
            Commission = commission,
            CommissionCurrency = commissionCurrency,
        }, cancellationToken);
    }

    private async ValueTask SendOrderTradesAsync(TrackedOrder tracked,
        long target, CancellationToken cancellationToken)
    {
        var trades = await HttpClient.GetTradesAsync(tracked.Uid,
            cancellationToken) ?? [];
        foreach (var trade in trades.OrderBy(static item => item.BlockNumber)
            .ThenBy(static item => item.LogIndex))
        {
            var execution = await ToOrderTradeAsync(tracked, trade,
                cancellationToken);
            var key = $"{target}:{execution.Id}";
            using (_sync.EnterScope())
                if (!_sentOrderTrades.Add(key))
                    continue;
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                SecurityId = tracked.Market.ToStockSharp(),
                ServerTime = execution.Time,
                PortfolioName = GetPortfolioName(),
                Side = tracked.Side,
                OrderStringId = tracked.Uid,
                TradeStringId = execution.Id,
                TradePrice = execution.Price,
                TradeVolume = execution.Volume,
                TransactionId = tracked.TransactionId,
                OriginalTransactionId = target,
                Commission = execution.Commission,
                CommissionCurrency = execution.CommissionCurrency,
            }, cancellationToken);
        }
    }

    private async ValueTask<(string Id, DateTime Time, decimal Price,
        decimal Volume, decimal Commission, string CommissionCurrency)>
        ToOrderTradeAsync(TrackedOrder tracked, CowProtocolApiTrade trade,
        CancellationToken cancellationToken)
    {
        if (trade is null || trade.BlockNumber < 0 || trade.LogIndex < 0 ||
            !trade.OrderUid.NormalizeOrderUid().EqualsIgnoreCase(tracked.Uid) ||
            !trade.Owner.NormalizeAddress().EqualsIgnoreCase(
                RpcClient.WalletAddress) ||
            !trade.SellToken.NormalizeAddress().EqualsIgnoreCase(
                tracked.Order.SellToken) ||
            !trade.BuyToken.NormalizeAddress().EqualsIgnoreCase(
                tracked.Order.BuyToken))
            throw new InvalidDataException(
                $"CoW Protocol returned invalid trade data for '{tracked.Uid}'.");
        var sell = trade.SellAmount.ParseInteger();
        var beforeFees = trade.SellAmountBeforeFees.ParseInteger();
        var buy = trade.BuyAmount.ParseInteger();
        if (sell <= 0 || beforeFees <= 0 || buy <= 0 || sell < beforeFees)
            throw new InvalidDataException(
                $"CoW Protocol trade for '{tracked.Uid}' has invalid amounts.");
        decimal volume;
        decimal quote;
        if (tracked.Side == Sides.Sell)
        {
            volume = beforeFees.FromBaseUnits(
                tracked.Market.BaseToken.Decimals);
            quote = buy.FromBaseUnits(tracked.Market.QuoteToken.Decimals);
        }
        else
        {
            volume = buy.FromBaseUnits(tracked.Market.BaseToken.Decimals);
            quote = beforeFees.FromBaseUnits(
                tracked.Market.QuoteToken.Decimals);
        }
        if (volume <= 0 || quote <= 0)
            throw new InvalidDataException(
                $"CoW Protocol trade for '{tracked.Uid}' is non-positive.");
        var hash = trade.TransactionHash.IsEmpty()
            ? null
            : trade.TransactionHash.NormalizeHash();
        var id = hash.IsEmpty()
            ? $"{tracked.Uid}:{trade.BlockNumber}:{trade.LogIndex}"
            : $"{hash}:{trade.LogIndex}";
        return (id,
            await GetBlockTimeAsync(new BigInteger(trade.BlockNumber),
                cancellationToken), quote / volume, volume,
            (sell - beforeFees).FromBaseUnits(
                tracked.Side == Sides.Sell
                    ? tracked.Market.BaseToken.Decimals
                    : tracked.Market.QuoteToken.Decimals),
            tracked.Side == Sides.Sell
                ? tracked.Market.BaseToken.Symbol
                : tracked.Market.QuoteToken.Symbol);
    }

    private static CowProtocolOrderData CreateOrderData(
        CowProtocolOrderParameters quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        return new()
        {
            SellToken = quote.SellToken.NormalizeAddress(),
            BuyToken = quote.BuyToken.NormalizeAddress(),
            Receiver = quote.Receiver.NormalizeAddress(),
            SellAmount = quote.SellAmount.ParseInteger(),
            BuyAmount = quote.BuyAmount.ParseInteger(),
            ValidTo = quote.ValidTo,
            AppDataHash = quote.AppDataHash.NormalizeBytes32(),
            FeeAmount = BigInteger.Zero,
            Kind = quote.Kind,
            IsPartiallyFillable = false,
            SellTokenBalance = CowProtocolTokenBalances.Erc20,
            BuyTokenBalance = CowProtocolTokenBalances.Erc20,
        };
    }

    private static CowProtocolOrderData CreateLimitOrderData(
        CowProtocolMarket market, Sides side, decimal volume, decimal price,
        uint validTo)
    {
        var baseAmount = volume.ToBaseUnits(market.BaseToken.Decimals);
        var quoteAmount = checked(volume * price).ToBaseUnits(
            market.QuoteToken.Decimals);
        if (baseAmount <= 0 || quoteAmount <= 0)
            throw new InvalidOperationException(
                "CoW Protocol limit order amount rounds to zero token units.");
        return new()
        {
            SellToken = side == Sides.Sell
                ? market.BaseToken.Address
                : market.QuoteToken.Address,
            BuyToken = side == Sides.Sell
                ? market.QuoteToken.Address
                : market.BaseToken.Address,
            Receiver = CowProtocolExtensions.NativeTokenAddress,
            SellAmount = side == Sides.Sell ? baseAmount : quoteAmount,
            BuyAmount = side == Sides.Sell ? quoteAmount : baseAmount,
            ValidTo = validTo,
            AppDataHash = CowProtocolExtensions.EmptyAppDataHash,
            FeeAmount = BigInteger.Zero,
            Kind = side == Sides.Sell
                ? CowProtocolOrderKinds.Sell
                : CowProtocolOrderKinds.Buy,
            IsPartiallyFillable = false,
            SellTokenBalance = CowProtocolTokenBalances.Erc20,
            BuyTokenBalance = CowProtocolTokenBalances.Erc20,
        };
    }

    private CowProtocolOrderCreation CreateOrderCreation(
        CowProtocolOrderData order, CowProtocolSignedOrder signed,
        long? quoteId)
        => new()
        {
            SellToken = order.SellToken,
            BuyToken = order.BuyToken,
            Receiver = order.Receiver,
            SellAmount = order.SellAmount.ToString(CultureInfo.InvariantCulture),
            BuyAmount = order.BuyAmount.ToString(CultureInfo.InvariantCulture),
            ValidTo = order.ValidTo,
            AppData = CowProtocolExtensions.EmptyAppData,
            AppDataHash = order.AppDataHash,
            FeeAmount = "0",
            Kind = order.Kind,
            IsPartiallyFillable = false,
            SellTokenBalance = CowProtocolTokenBalances.Erc20,
            BuyTokenBalance = CowProtocolTokenBalances.Erc20,
            SigningScheme = CowProtocolSigningSchemes.Eip712,
            Signature = signed.Signature,
            From = RpcClient.WalletAddress,
            QuoteId = quoteId,
            IsFullBalanceCheck = true,
        };

    private uint GetValidTo(DateTime? tillDate)
    {
        var now = DateTime.UtcNow;
        var expiry = tillDate?.ToUniversalTime() ?? now + OrderValidity;
        var lifetime = expiry - now;
        if (lifetime < TimeSpan.FromMinutes(1) ||
            lifetime > TimeSpan.FromDays(1))
            throw new InvalidOperationException(
                "CoW Protocol order expiry must be between one minute and one " +
                "day from now.");
        var seconds = expiry.ToUnixSeconds();
        if (seconds <= 0 || seconds > uint.MaxValue)
            throw new InvalidOperationException(
                "CoW Protocol order expiry is outside the uint32 Unix range.");
        return (uint)seconds;
    }

    private static decimal GetQuotePrice(CowProtocolMarket market,
        Sides side, decimal volume, CowProtocolQuote quote)
    {
        var quoteAmount = side == Sides.Sell
            ? quote.OutputAmount.FromBaseUnits(market.QuoteToken.Decimals)
            : quote.InputAmount.FromBaseUnits(market.QuoteToken.Decimals);
        var price = quoteAmount / volume;
        if (price <= 0)
            throw new InvalidDataException(
                "CoW Protocol returned a non-positive quote price.");
        return price;
    }

    private static decimal GetFilledVolume(TrackedOrder tracked)
    {
        var value = tracked.Side == Sides.Sell
            ? tracked.Order.ExecutedSellAmountBeforeFees.ParseInteger()
                .FromBaseUnits(tracked.Market.BaseToken.Decimals)
            : tracked.Order.ExecutedBuyAmount.ParseInteger()
                .FromBaseUnits(tracked.Market.BaseToken.Decimals);
        return value.Max(0m);
    }

    private static decimal? GetOrderCommission(TrackedOrder tracked,
        out string currency)
    {
        var order = tracked.Order;
        var executed = order.ExecutedSellAmount.ParseInteger();
        var beforeFees = order.ExecutedSellAmountBeforeFees.ParseInteger();
        if (executed < beforeFees)
            throw new InvalidDataException(
                $"CoW Protocol order '{order.Uid}' has invalid fee amounts.");
        var token = tracked.Side == Sides.Sell
            ? tracked.Market.BaseToken
            : tracked.Market.QuoteToken;
        currency = token.Symbol;
        return (executed - beforeFees).FromBaseUnits(token.Decimals);
    }

    private void ValidateApiOrder(CowProtocolOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var uid = order.Uid.NormalizeOrderUid();
        var owner = order.Owner.NormalizeAddress();
        var sellToken = order.SellToken.NormalizeAddress();
        var buyToken = order.BuyToken.NormalizeAddress();
        if (!order.Receiver.IsEmpty())
            _ = order.Receiver.NormalizeAddress();
        _ = GetAppDataHash(order);
        if (!owner.EqualsIgnoreCase(RpcClient.WalletAddress) ||
            !order.SettlementContract.NormalizeAddress().EqualsIgnoreCase(
                CowProtocolExtensions.SettlementAddress) ||
            sellToken.EqualsIgnoreCase(buyToken) ||
            order.SellAmount.ParseInteger() <= 0 ||
            order.BuyAmount.ParseInteger() <= 0 ||
            order.FeeAmount.ParseInteger() < 0 ||
            order.ExecutedSellAmount.ParseInteger() < 0 ||
            order.ExecutedSellAmountBeforeFees.ParseInteger() < 0 ||
            order.ExecutedBuyAmount.ParseInteger() < 0 ||
            !System.Enum.IsDefined(order.Kind) ||
            !System.Enum.IsDefined(order.Status) ||
            !System.Enum.IsDefined(order.SigningScheme) ||
            !System.Enum.IsDefined(order.SellTokenBalance) ||
            !System.Enum.IsDefined(order.BuyTokenBalance))
            throw new InvalidDataException(
                $"CoW Protocol order '{uid}' has invalid protocol fields.");
        var bytes = uid[2..].HexToByteArray();
        if (!bytes.AsSpan(32, 20).SequenceEqual(
            owner[2..].HexToByteArray()) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(52, 4)) !=
            order.ValidTo)
            throw new InvalidDataException(
                $"CoW Protocol order '{uid}' UID metadata does not match the " +
                "order owner and expiry.");
    }

    private static string GetAppDataHash(CowProtocolOrder order)
    {
        if (!order.AppDataHash.IsEmpty())
            return order.AppDataHash.NormalizeBytes32();
        if (order.AppData == CowProtocolExtensions.EmptyAppData)
            return CowProtocolExtensions.EmptyAppDataHash;
        if (!order.AppData.IsEmpty() && order.AppData.Length == 66 &&
            order.AppData.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return order.AppData.NormalizeBytes32();
        throw new InvalidDataException(
            $"CoW Protocol order '{order.Uid}' has no valid app data hash.");
    }

    private static OrderStates ToOrderState(CowProtocolOrderStatuses status)
        => status switch
        {
            CowProtocolOrderStatuses.PresignaturePending or
                CowProtocolOrderStatuses.Open => OrderStates.Active,
            CowProtocolOrderStatuses.Fulfilled or
                CowProtocolOrderStatuses.Cancelled or
                CowProtocolOrderStatuses.Expired => OrderStates.Done,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status,
                "Unsupported CoW Protocol order status."),
        };

    private static bool Matches(OrderSubscription subscription,
        TrackedOrder order)
    {
        if (!subscription.Uid.IsEmpty() &&
            !subscription.Uid.EqualsIgnoreCase(order.Uid))
            return false;
        if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
            !subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
                order.Market.SecurityCode))
            return false;
        if (subscription.Side is Sides side && order.Side != side)
            return false;
        if (subscription.Volume is decimal volume && order.Volume != volume)
            return false;
        return (subscription.From is null ||
                order.SubmittedTime >= subscription.From) &&
            (subscription.To is null || order.SubmittedTime <=
                subscription.To);
    }

    private static bool IsSuccessful(CowProtocolRpcReceipt receipt)
        => receipt?.Status.IsEmpty() == false &&
            receipt.Status.ParseInteger() == BigInteger.One;

    private void RemoveOrderSubscription(long target)
    {
        using (_sync.EnterScope())
        {
            _orderSubscriptions.Remove(target);
            RemoveFingerprintPrefix(_orderFingerprints, target);
            var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
            _sentOrderTrades.RemoveWhere(key => key.StartsWith(prefix,
                StringComparison.Ordinal));
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
