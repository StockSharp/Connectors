namespace StockSharp.Aerodrome;

public partial class AerodromeMessageAdapter
{

    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(
        OrderRegisterMessage regMsg, CancellationToken cancellationToken)
    {
        EnsureTradingReady();
        ValidatePortfolio(regMsg.PortfolioName);
        var market = GetMarket(regMsg.SecurityId);
        if (regMsg.OrderType is not (null or OrderTypes.Market))
            throw new NotSupportedException(
                "Aerodrome swaps are immediate AMM market orders.");
        if (regMsg.Condition is not null)
            throw new NotSupportedException(
                "Aerodrome pool contracts do not expose conditional orders.");
        if (regMsg.PostOnly == true)
            throw new NotSupportedException(
                "Post-only is not applicable to AMM swaps.");
        if (regMsg.TimeInForce is not null)
            throw new NotSupportedException(
                "Time-in-force is not applicable to AMM swaps.");
        if (!regMsg.UserOrderId.IsEmpty())
            throw new NotSupportedException(
                "An on-chain swap identifier is its transaction hash; a " +
                "client-order ID cannot be embedded in a pool swap.");
        var volume = regMsg.Volume.Abs();
        if (volume <= 0)
            throw new InvalidOperationException(
                "Aerodrome swap volume must be positive.");
        var baseUnits = volume.ToBaseUnits(market.BaseToken.Decimals);
        if (baseUnits <= 0)
            throw new InvalidOperationException(
                "Aerodrome swap volume rounds to zero base units.");

        var tradeType = regMsg.Side == Sides.Sell
            ? AerodromeTradeTypes.ExactInput
            : AerodromeTradeTypes.ExactOutput;
        var preliminary = await RpcClient.GetQuoteAsync(market, tradeType,
            baseUnits, cancellationToken);
        var inputToken = regMsg.Side == Sides.Sell
            ? market.BaseToken
            : market.QuoteToken;
        var slippageBps = new BigInteger(SlippageTolerance * 100m);
        var approvalAmount = tradeType == AerodromeTradeTypes.ExactInput
            ? preliminary.InputAmount
            : (preliminary.InputAmount * (10_000 + slippageBps) + 9_999) /
                10_000;
        var spender = RpcClient.GetSpender(market);
        if (!inputToken.Address.IsNativeToken())
            await EnsureApprovalAsync(inputToken, spender, approvalAmount,
                cancellationToken);

        var quote = await RpcClient.GetQuoteAsync(market, tradeType,
            baseUnits, cancellationToken);
        var finalApprovalAmount = tradeType ==
            AerodromeTradeTypes.ExactInput
                ? quote.InputAmount
                : (quote.InputAmount * (10_000 + slippageBps) + 9_999) /
                    10_000;
        if (!inputToken.Address.IsNativeToken() &&
            finalApprovalAmount > approvalAmount)
            await EnsureApprovalAsync(inputToken, spender,
                finalApprovalAmount, cancellationToken);
        var transaction = RpcClient.CreateSwapTransaction(market, tradeType,
            baseUnits, quote, SlippageTolerance,
            DateTime.UtcNow.AddMinutes(2));
        var hash = await RpcClient.SendTransactionAsync(transaction,
            cancellationToken);
        var price = GetSwapPrice(market, regMsg.Side, volume,
            quote.InputAmount, quote.OutputAmount);
        var tracked = new TrackedSwap
        {
            TransactionId = regMsg.TransactionId,
            TransactionHash = hash,
            Market = market,
            Side = regMsg.Side,
            Volume = volume,
            Price = price,
            SubmittedTime = CurrentTime,
            State = OrderStates.Active,
        };
        using (_sync.EnterScope())
            _trackedSwaps[hash] = tracked;
        await SendSwapOrderAsync(tracked, regMsg.TransactionId, null,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask ReplaceOrderAsync(
        OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
    {
        _ = replaceMsg;
        _ = cancellationToken;
        throw new NotSupportedException(
            "A broadcast Aerodrome transaction cannot be replaced through " +
            "the protocol API.");
    }

    /// <inheritdoc />
    protected override ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        _ = cancelMsg;
        _ = cancellationToken;
        throw new NotSupportedException(
            "Aerodrome has no cancellable order book. Pending EVM nonce " +
            "replacement is a wallet operation and is not emulated as a " +
            "protocol cancellation.");
    }

    /// <inheritdoc />
    protected override ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        _ = cancelMsg;
        _ = cancellationToken;
        throw new NotSupportedException(
            "Aerodrome has no open-order group to cancel.");
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
            BoardCode = BoardCodes.Aerodrome,
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
                "Aerodrome orders use EVM transaction hashes, not numeric " +
                "order identifiers.");
        if (!statusMsg.UserId.IsEmpty())
            throw new NotSupportedException(
                "Aerodrome has no exchange-side user identifier.");
        if (statusMsg.SecurityIds.Length > 0)
            throw new NotSupportedException(
                "Use the primary security filter for Aerodrome order status.");
        var hash = statusMsg.OrderStringId.IsEmpty()
            ? null
            : NormalizeTransactionHash(statusMsg.OrderStringId);
        var subscription = new OrderSubscription
        {
            TransactionHash = hash,
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

    private async ValueTask EnsureApprovalAsync(AerodromeToken token,
        string spender, BigInteger amount,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
            throw new InvalidOperationException(
                "Aerodrome approval amount must be positive.");
        var allowance = await RpcClient.GetAllowanceAsync(token, spender,
            cancellationToken);
        if (allowance >= amount)
            return;
        if (allowance > 0)
            await BroadcastAndConfirmAsync(
                RpcClient.CreateApprovalTransaction(token, spender,
                    BigInteger.Zero),
                "approval reset", cancellationToken);
        await BroadcastAndConfirmAsync(
            RpcClient.CreateApprovalTransaction(token, spender, amount),
            "token approval", cancellationToken);
    }

    private async ValueTask BroadcastAndConfirmAsync(
        AerodromeTransaction transaction, string operation,
        CancellationToken cancellationToken)
    {
        var hash = await RpcClient.SendTransactionAsync(transaction,
            cancellationToken);
        var receipt = await RpcClient.WaitForReceiptAsync(hash,
            TimeSpan.FromMinutes(3), cancellationToken);
        if (!IsSuccessful(receipt))
            throw new InvalidOperationException(
                $"Aerodrome {operation} transaction '{hash}' reverted.");
    }

    private async ValueTask PollPrivateAsync(
        CancellationToken cancellationToken)
    {
        long[] portfolioTargets;
        KeyValuePair<long, OrderSubscription>[] orderTargets;
        TrackedSwap[] active;
        using (_sync.EnterScope())
        {
            portfolioTargets = [.. _portfolioSubscriptions];
            orderTargets = [.. _orderSubscriptions];
            active = [.. _trackedSwaps.Values.Where(static swap =>
                swap.State == OrderStates.Active)];
        }
        if (portfolioTargets.Length > 0)
        {
            var balances = await LoadBalancesAsync(cancellationToken);
            foreach (var target in portfolioTargets)
                await SendPortfolioSnapshotAsync(target, false, balances,
                    cancellationToken);
        }
        foreach (var swap in active)
            await RefreshSwapAsync(swap, cancellationToken);
        foreach (var target in orderTargets)
            await SendOrderSnapshotAsync(target.Value, target.Key, false,
                cancellationToken);
    }

    private async ValueTask RefreshSwapAsync(TrackedSwap swap,
        CancellationToken cancellationToken)
    {
        var receipt = await RpcClient.GetReceiptAsync(swap.TransactionHash,
            cancellationToken);
        if (receipt is null)
            return;
        await ApplyReceiptAsync(swap, receipt, cancellationToken);
    }

    private async ValueTask ApplyReceiptAsync(TrackedSwap swap,
        AerodromeRpcReceipt receipt, CancellationToken cancellationToken)
    {
        var state = IsSuccessful(receipt)
            ? OrderStates.Done
            : OrderStates.Failed;
        var execution = state == OrderStates.Done
            ? ReadSwapExecution(swap, receipt)
            : (SwapExecution?)null;
        var isOrderChanged = false;
        var isTradeRequired = false;
        using (_sync.EnterScope())
        {
            swap.Receipt = receipt;
            if (execution is SwapExecution fill)
            {
                swap.Price = fill.Price;
                swap.Volume = fill.Volume;
            }
            isOrderChanged = swap.State != state;
            swap.State = state;
            if (state == OrderStates.Done && !swap.IsTradeSent)
            {
                swap.IsTradeSent = true;
                isTradeRequired = true;
            }
        }
        if (isOrderChanged)
            await SendSwapOrderAsync(swap, swap.TransactionId, receipt,
                cancellationToken);
        if (isTradeRequired)
            await SendSwapTradeAsync(swap, swap.TransactionId, receipt,
                cancellationToken);
    }

    private async ValueTask<
        (AerodromeToken Token, BigInteger Amount)[]> LoadBalancesAsync(
        CancellationToken cancellationToken)
    {
        AerodromeToken[] tokens;
        using (_sync.EnterScope())
            tokens = [.. _tokens.Values.GroupBy(static token => token.Address,
                    StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())];
        var result = new List<(AerodromeToken, BigInteger)>();
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
        (AerodromeToken Token, BigInteger Amount)[] balances,
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
                    BoardCode = BoardCodes.Aerodrome,
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
        TrackedSwap[] swaps;
        using (_sync.EnterScope())
            swaps = [.. _trackedSwaps.Values.Where(swap =>
                    Matches(subscription, swap))
                .OrderBy(static swap => swap.SubmittedTime)];
        var skipped = 0;
        var delivered = 0;
        foreach (var swap in swaps)
        {
            var receipt = swap.State == OrderStates.Active
                ? await RpcClient.GetReceiptAsync(swap.TransactionHash,
                    cancellationToken)
                : swap.Receipt;
            if (receipt is not null)
                await ApplyReceiptAsync(swap, receipt, cancellationToken);
            if (subscription.States is { Length: > 0 } states &&
                !states.Contains(swap.State))
                continue;
            if (skipped++ < subscription.Skip)
                continue;
            if (delivered++ >= subscription.Maximum)
                break;
            var key = $"{target}:{swap.TransactionHash}";
            var isOrderRequired = false;
            var isTradeRequired = false;
            using (_sync.EnterScope())
            {
                var isKnown = _orderFingerprints.TryGetValue(key,
                    out var previous);
                isOrderRequired = isForced || !isKnown ||
                    previous.State != swap.State;
                isTradeRequired = swap.State == OrderStates.Done &&
                    (!isKnown || !previous.IsTradeSent);
                _orderFingerprints[key] = new(swap.State,
                    (isKnown && previous.IsTradeSent) || isTradeRequired);
            }
            if (isOrderRequired)
                await SendSwapOrderAsync(swap, target, swap.Receipt,
                    cancellationToken);
            if (isTradeRequired)
                await SendSwapTradeAsync(swap, target, swap.Receipt,
                    cancellationToken);
        }
    }

    private ValueTask SendSwapOrderAsync(TrackedSwap swap, long target,
        AerodromeRpcReceipt receipt, CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = swap.Market.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = swap.Side,
            OrderVolume = swap.Volume,
            Balance = swap.State == OrderStates.Active ? swap.Volume : 0m,
            OrderPrice = swap.Price,
            OrderType = OrderTypes.Market,
            OrderState = swap.State,
            OrderStringId = swap.TransactionHash,
            TransactionId = swap.TransactionId,
            OriginalTransactionId = target,
            Commission = GetCommission(receipt),
            CommissionCurrency = "ETH",
        }, cancellationToken);

    private ValueTask SendSwapTradeAsync(TrackedSwap swap, long target,
        AerodromeRpcReceipt receipt, CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = swap.Market.ToStockSharp(),
            ServerTime = CurrentTime,
            PortfolioName = GetPortfolioName(),
            Side = swap.Side,
            OrderStringId = swap.TransactionHash,
            TradeStringId = swap.TransactionHash,
            TradePrice = swap.Price,
            TradeVolume = swap.Volume,
            TransactionId = swap.TransactionId,
            OriginalTransactionId = target,
            Commission = GetCommission(receipt),
            CommissionCurrency = "ETH",
        }, cancellationToken);

    private static decimal? GetCommission(AerodromeRpcReceipt receipt)
    {
        if (receipt?.GasUsed.IsEmpty() != false ||
            receipt.EffectiveGasPrice.IsEmpty())
            return null;
        var cost = receipt.GasUsed.ParseInteger() *
            receipt.EffectiveGasPrice.ParseInteger();
        return cost.FromBaseUnits(18);
    }

    private static decimal GetSwapPrice(AerodromeMarket market, Sides side,
        decimal volume, BigInteger inputAmount, BigInteger outputAmount)
    {
        var quoteAmount = side == Sides.Sell
            ? outputAmount.FromBaseUnits(market.QuoteToken.Decimals)
            : inputAmount.FromBaseUnits(market.QuoteToken.Decimals);
        var price = quoteAmount / volume;
        if (price <= 0)
            throw new InvalidDataException(
                "Aerodrome returned a non-positive execution price.");
        return price;
    }

    private static SwapExecution ReadSwapExecution(TrackedSwap swap,
        AerodromeRpcReceipt receipt)
    {
        var amount0 = BigInteger.Zero;
        var amount1 = BigInteger.Zero;
        var isFound = false;
        var topic = swap.Market.PoolType.GetSwapTopic();
        foreach (var log in receipt.Logs ?? [])
        {
            if (log?.Address.IsEmpty() != false ||
                log.Topics is not { Length: > 0 } topics ||
                !topics[0].EqualsIgnoreCase(topic))
                continue;
            string address;
            try
            {
                address = log.Address.NormalizeAddress();
            }
            catch (ArgumentException)
            {
                continue;
            }
            if (!address.EqualsIgnoreCase(swap.Market.PoolId))
                continue;
            if (swap.Market.PoolType != AerodromePoolTypes.Slipstream)
            {
                amount0 += AerodromeExtensions.ReadAbiWord(log.Data, 0) -
                    AerodromeExtensions.ReadAbiWord(log.Data, 2);
                amount1 += AerodromeExtensions.ReadAbiWord(log.Data, 1) -
                    AerodromeExtensions.ReadAbiWord(log.Data, 3);
            }
            else
            {
                amount0 += AerodromeExtensions.ReadAbiSignedWord(
                    log.Data, 0);
                amount1 += AerodromeExtensions.ReadAbiSignedWord(
                    log.Data, 1);
            }
            isFound = true;
        }
        if (!isFound)
            throw new InvalidDataException(
                $"Successful Aerodrome transaction " +
                $"'{swap.TransactionHash}' contains no matching Swap event.");
        var isBaseToken0 = swap.Market.BaseToken.Address.EqualsIgnoreCase(
            swap.Market.Token0.Address);
        var signedBase = isBaseToken0 ? amount0 : amount1;
        var signedQuote = isBaseToken0 ? amount1 : amount0;
        var isExpectedDirection = swap.Side == Sides.Sell
            ? signedBase > 0 && signedQuote < 0
            : signedBase < 0 && signedQuote > 0;
        if (!isExpectedDirection)
            throw new InvalidDataException(
                $"Aerodrome transaction '{swap.TransactionHash}' " +
                "contains an unexpected swap direction.");
        var volume = BigInteger.Abs(signedBase).FromBaseUnits(
            swap.Market.BaseToken.Decimals);
        var quote = BigInteger.Abs(signedQuote).FromBaseUnits(
            swap.Market.QuoteToken.Decimals);
        if (volume <= 0 || quote <= 0)
            throw new InvalidDataException(
                $"Aerodrome transaction '{swap.TransactionHash}' " +
                "contains non-positive execution amounts.");
        return new(quote / volume, volume);
    }

    private static bool Matches(OrderSubscription subscription,
        TrackedSwap swap)
    {
        if (!subscription.TransactionHash.IsEmpty() &&
            !subscription.TransactionHash.EqualsIgnoreCase(
                swap.TransactionHash))
            return false;
        if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
            !subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
                swap.Market.SecurityCode))
            return false;
        if (subscription.Side is Sides side && swap.Side != side)
            return false;
        if (subscription.Volume is decimal volume && swap.Volume != volume)
            return false;
        return (subscription.From is null ||
                swap.SubmittedTime >= subscription.From) &&
            (subscription.To is null || swap.SubmittedTime <=
                subscription.To);
    }

    private static bool IsSuccessful(AerodromeRpcReceipt receipt)
        => receipt?.Status.IsEmpty() == false &&
            receipt.Status.ParseInteger() == BigInteger.One;

    private void RemoveOrderSubscription(long target)
    {
        using (_sync.EnterScope())
        {
            _orderSubscriptions.Remove(target);
            RemoveFingerprintPrefix(_orderFingerprints, target);
        }
    }

    private async ValueTask CompleteOrderStatusAsync(
        OrderStatusMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }

    private static string NormalizeTransactionHash(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (value.Length != 66 || !value.StartsWith("0x",
            StringComparison.OrdinalIgnoreCase) || value.Skip(2).Any(
            static ch => !Uri.IsHexDigit(ch)))
            throw new InvalidOperationException(
                $"Invalid EVM transaction hash '{value}'.");
        return "0x" + value[2..].ToLowerInvariant();
    }
}
