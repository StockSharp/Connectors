namespace StockSharp.OneInch;

public partial class OneInchMessageAdapter
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
                "1inch Classic Swap supports immediate market swaps only.");
        if (regMsg.Side is not (Sides.Buy or Sides.Sell))
            throw new ArgumentOutOfRangeException(nameof(regMsg.Side));
        if (regMsg.Condition is not null)
            throw new NotSupportedException(
                "1inch Classic Swap does not expose conditional orders.");
        if (regMsg.PostOnly == true)
            throw new NotSupportedException(
                "Post-only is not applicable to an immediate swap.");
        if (regMsg.TimeInForce is not null)
            throw new NotSupportedException(
                "Time-in-force is not applicable to an immediate swap.");
        if (regMsg.TillDate is not null)
            throw new NotSupportedException(
                "An expiry cannot be attached to a 1inch Classic Swap request.");
        if (!regMsg.UserOrderId.IsEmpty())
            throw new NotSupportedException(
                "An on-chain swap is identified by its transaction hash; a " +
                "client-order identifier cannot be embedded in it.");

        var requestedVolume = regMsg.Volume.Abs();
        if (requestedVolume <= 0)
            throw new InvalidOperationException(
                "1inch swap volume must be positive.");
        var requestedBaseAmount = requestedVolume.ToBaseUnits(
            market.BaseToken.Decimals);
        if (requestedBaseAmount <= 0)
            throw new InvalidOperationException(
                "1inch swap volume rounds to zero base units.");

        var sourceToken = regMsg.Side == Sides.Sell
            ? market.BaseToken
            : market.QuoteToken;
        var destinationToken = regMsg.Side == Sides.Sell
            ? market.QuoteToken
            : market.BaseToken;
        var quote = await GetOrderQuoteAsync(market, regMsg.Side,
            requestedBaseAmount, cancellationToken);
        var wasApproved = await EnsureApprovalAsync(sourceToken, Spender,
            quote.InputAmount, cancellationToken);
        if (wasApproved)
        {
            quote = await GetOrderQuoteAsync(market, regMsg.Side,
                requestedBaseAmount, cancellationToken);
            _ = await EnsureApprovalAsync(sourceToken, Spender,
                quote.InputAmount, cancellationToken);
        }

        var response = await HttpClient.GetSwapAsync(sourceToken.Address,
            destinationToken.Address, quote.InputAmount,
            RpcClient.WalletAddress, SlippageTolerance, cancellationToken);
        var destinationAmount = ValidateSwapResponse(response, sourceToken,
            destinationToken);
        var transaction = ToTransaction(response.Transaction);
        var hash = await RpcClient.SendTransactionAsync(transaction,
            cancellationToken);
        var price = GetSwapPrice(market, regMsg.Side, quote.InputAmount,
            destinationAmount);
        var tracked = new TrackedSwap
        {
            TransactionId = regMsg.TransactionId,
            TransactionHash = hash,
            Market = market,
            Side = regMsg.Side,
            SourceToken = sourceToken,
            DestinationToken = destinationToken,
            SourceAmount = quote.InputAmount,
            RequestedVolume = requestedVolume,
            Volume = requestedVolume,
            Price = price,
            SubmittedTime = DateTime.UtcNow,
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
            "A broadcast 1inch transaction cannot be replaced through the " +
            "Classic Swap API.");
    }

    /// <inheritdoc />
    protected override ValueTask CancelOrderAsync(
        OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
    {
        _ = cancelMsg;
        _ = cancellationToken;
        throw new NotSupportedException(
            "1inch Classic Swap has no cancellable order book. Pending EVM " +
            "nonce replacement is a wallet operation and is not emulated as " +
            "a protocol cancellation.");
    }

    /// <inheritdoc />
    protected override ValueTask CancelOrderGroupAsync(
        OrderGroupCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        _ = cancelMsg;
        _ = cancellationToken;
        throw new NotSupportedException(
            "1inch Classic Swap has no open-order group to cancel.");
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
            BoardCode = BoardCodes.OneInch,
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
                "1inch swaps use EVM transaction hashes, not numeric order " +
                "identifiers.");
        if (!statusMsg.UserId.IsEmpty())
            throw new NotSupportedException(
                "1inch Classic Swap has no exchange-side user identifier.");
        if (statusMsg.SecurityIds.Length > 0)
            throw new NotSupportedException(
                "Use the primary security filter for 1inch order status.");
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

    private async ValueTask<OneInchQuote> GetOrderQuoteAsync(
        OneInchMarket market, Sides side, BigInteger requestedBaseAmount,
        CancellationToken cancellationToken)
    {
        if (side == Sides.Sell)
            return await GetQuoteAsync(market.BaseToken, market.QuoteToken,
                requestedBaseAmount, cancellationToken);

        var forward = await GetQuoteAsync(market.BaseToken,
            market.QuoteToken, requestedBaseAmount, cancellationToken);
        var reverse = await GetQuoteAsync(market.QuoteToken,
            market.BaseToken, forward.OutputAmount, cancellationToken);
        var adjustedInput = (reverse.InputAmount * requestedBaseAmount +
            reverse.OutputAmount - 1) / reverse.OutputAmount;
        if (adjustedInput <= 0)
            throw new InvalidDataException(
                "1inch returned an invalid market-buy input estimate.");
        if (adjustedInput != reverse.InputAmount)
            reverse = await GetQuoteAsync(market.QuoteToken,
                market.BaseToken, adjustedInput, cancellationToken);
        return reverse;
    }

    private BigInteger ValidateSwapResponse(OneInchSwapResponse response,
        OneInchToken sourceToken, OneInchToken destinationToken)
    {
        if (response is null)
            throw new InvalidDataException(
                "1inch API returned an empty swap response.");
        ValidateTokenInfo(response.SourceToken, sourceToken, "source");
        ValidateTokenInfo(response.DestinationToken, destinationToken,
            "destination");
        var destinationAmount = response.DestinationAmount.ParseInteger();
        if (destinationAmount <= 0)
            throw new InvalidDataException(
                "1inch API returned a non-positive swap output amount.");
        if (response.Transaction is null)
            throw new InvalidDataException(
                "1inch API returned no swap transaction.");
        return destinationAmount;
    }

    private OneInchTransaction ToTransaction(OneInchTransactionData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.From.NormalizeAddress().EqualsIgnoreCase(
            RpcClient.WalletAddress))
            throw new InvalidDataException(
                "1inch API returned a transaction for a different wallet.");
        if (!data.To.NormalizeAddress().EqualsIgnoreCase(Spender))
            throw new InvalidDataException(
                "1inch API returned a transaction for an unexpected router.");
        var value = data.Value.ParseInteger();
        if (value != BigInteger.Zero)
            throw new InvalidDataException(
                "A wrapped-token 1inch swap must not transfer native value.");
        if (data.Gas <= 0)
            throw new InvalidDataException(
                "1inch API returned a non-positive transaction gas limit.");
        if (!data.GasPrice.IsEmpty() && data.GasPrice.ParseInteger() < 0)
            throw new InvalidDataException(
                "1inch API returned a negative gas price.");
        return new()
        {
            To = data.To.NormalizeAddress(),
            Data = data.Data.NormalizeData(),
            Value = value,
            SuggestedGas = new BigInteger(data.Gas),
        };
    }

    private async ValueTask<bool> EnsureApprovalAsync(OneInchToken token,
        string spender, BigInteger amount,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
            throw new InvalidOperationException(
                "1inch approval amount must be positive.");
        var allowance = await RpcClient.GetAllowanceAsync(token, spender,
            cancellationToken);
        if (allowance >= amount)
            return false;
        if (!IsAutoApprove)
            throw new InvalidOperationException(
                $"Token '{token.Symbol}' allowance for the 1inch router is " +
                "insufficient. Approve it manually or enable automatic " +
                "approval.");
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
        OneInchTransaction transaction, string operation,
        CancellationToken cancellationToken)
    {
        var hash = await RpcClient.SendTransactionAsync(transaction,
            cancellationToken);
        var receipt = await RpcClient.WaitForReceiptAsync(hash,
            ReceiptTimeout, cancellationToken);
        if (!IsSuccessful(receipt))
            throw new InvalidOperationException(
                $"1inch {operation} transaction '{hash}' reverted.");
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
        OneInchRpcReceipt receipt, CancellationToken cancellationToken)
    {
        var state = IsSuccessful(receipt)
            ? OrderStates.Done
            : OrderStates.Failed;
		var receiptTime = await RpcClient.GetBlockTimeAsync(
			receipt.BlockNumber.ParseInteger(), cancellationToken);
		var execution = state == OrderStates.Done
			? ReadSwapExecution(swap, receipt)
            : null;
        var isOrderChanged = false;
        var isTradeRequired = false;
        using (_sync.EnterScope())
        {
            swap.Receipt = receipt;
            swap.ExecutionTime = receiptTime;
            if (execution is not null)
            {
                swap.Price = execution.Price;
                swap.Volume = execution.Volume;
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
        (OneInchToken Token, BigInteger Amount)[]> LoadBalancesAsync(
        CancellationToken cancellationToken)
    {
        OneInchToken[] tokens;
        using (_sync.EnterScope())
            tokens = [.. _tokens.Values.GroupBy(static token => token.Address,
                    StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())];
        var result = new List<(OneInchToken, BigInteger)>();
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
        (OneInchToken Token, BigInteger Amount)[] balances,
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
                    BoardCode = BoardCodes.OneInch,
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
        OneInchRpcReceipt receipt, CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            SecurityId = swap.Market.ToStockSharp(),
            ServerTime = GetSwapTime(swap),
            PortfolioName = GetPortfolioName(),
            Side = swap.Side,
            OrderVolume = swap.RequestedVolume,
            Balance = swap.State == OrderStates.Active
                ? swap.RequestedVolume
                : 0m,
            OrderPrice = swap.Price,
            OrderType = OrderTypes.Market,
            OrderState = swap.State,
            OrderStringId = swap.TransactionHash,
            TransactionId = swap.TransactionId,
            OriginalTransactionId = target,
            Commission = GetCommission(receipt),
            CommissionCurrency = Chain.GetNativeSymbol(),
        }, cancellationToken);

    private ValueTask SendSwapTradeAsync(TrackedSwap swap, long target,
        OneInchRpcReceipt receipt, CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            SecurityId = swap.Market.ToStockSharp(),
            ServerTime = GetSwapTime(swap),
            PortfolioName = GetPortfolioName(),
            Side = swap.Side,
            OrderStringId = swap.TransactionHash,
            TradeStringId = swap.TransactionHash,
            TradePrice = swap.Price,
            TradeVolume = swap.Volume,
            TransactionId = swap.TransactionId,
            OriginalTransactionId = target,
            Commission = GetCommission(receipt),
            CommissionCurrency = Chain.GetNativeSymbol(),
        }, cancellationToken);

    private static DateTime GetSwapTime(TrackedSwap swap)
        => swap.ExecutionTime == default
            ? swap.SubmittedTime
            : swap.ExecutionTime;

    private static decimal? GetCommission(OneInchRpcReceipt receipt)
    {
        if (receipt?.GasUsed.IsEmpty() != false ||
            receipt.EffectiveGasPrice.IsEmpty())
            return null;
        var cost = receipt.GasUsed.ParseInteger() *
            receipt.EffectiveGasPrice.ParseInteger();
        return cost.FromBaseUnits(18);
    }

    private static decimal GetSwapPrice(OneInchMarket market, Sides side,
        BigInteger sourceAmount, BigInteger destinationAmount)
    {
        var volume = (side == Sides.Sell
            ? sourceAmount.FromBaseUnits(market.BaseToken.Decimals)
            : destinationAmount.FromBaseUnits(market.BaseToken.Decimals));
        var quote = (side == Sides.Sell
            ? destinationAmount.FromBaseUnits(market.QuoteToken.Decimals)
            : sourceAmount.FromBaseUnits(market.QuoteToken.Decimals));
        if (volume <= 0 || quote <= 0)
            throw new InvalidDataException(
                "1inch returned non-positive swap amounts.");
        return quote / volume;
    }

	private OneInchSwapExecution ReadSwapExecution(TrackedSwap swap,
		OneInchRpcReceipt receipt)
    {
        var sourceAmount = BigInteger.Zero;
        var destinationAmount = BigInteger.Zero;
        foreach (var log in receipt.Logs ?? [])
        {
            if (log?.IsRemoved != false || log.Address.IsEmpty() ||
                log.Topics is not { Length: >= 3 } topics ||
                !topics[0].EqualsIgnoreCase(OneInchExtensions.TransferTopic))
                continue;
            string tokenAddress;
            try
            {
                tokenAddress = log.Address.NormalizeAddress();
            }
            catch (ArgumentException)
            {
                continue;
            }
            var isSource = tokenAddress.EqualsIgnoreCase(
                swap.SourceToken.Address);
            var isDestination = tokenAddress.EqualsIgnoreCase(
                swap.DestinationToken.Address);
            if (!isSource && !isDestination)
                continue;
            var from = OneInchExtensions.ReadTopicAddress(topics[1]);
            var to = OneInchExtensions.ReadTopicAddress(topics[2]);
            var amount = log.Data.ParseInteger();
            if (amount < 0)
                throw new InvalidDataException(
                    "A 1inch Transfer event contains a negative amount.");
            if (isSource)
            {
                if (from.EqualsIgnoreCase(RpcClient.WalletAddress))
                    sourceAmount += amount;
                if (to.EqualsIgnoreCase(RpcClient.WalletAddress))
                    sourceAmount -= amount;
            }
            if (isDestination)
            {
                if (to.EqualsIgnoreCase(RpcClient.WalletAddress))
                    destinationAmount += amount;
                if (from.EqualsIgnoreCase(RpcClient.WalletAddress))
                    destinationAmount -= amount;
            }
        }
        if (sourceAmount <= 0 || destinationAmount <= 0)
            throw new InvalidDataException(
                $"Successful 1inch transaction '{swap.TransactionHash}' " +
                "contains no positive wallet execution amounts.");
        if (sourceAmount != swap.SourceAmount)
            throw new InvalidDataException(
                $"1inch transaction '{swap.TransactionHash}' spent an " +
                "unexpected source-token amount.");
        var price = GetSwapPrice(swap.Market, swap.Side, sourceAmount,
            destinationAmount);
        var volume = (swap.Side == Sides.Sell
            ? sourceAmount.FromBaseUnits(swap.Market.BaseToken.Decimals)
            : destinationAmount.FromBaseUnits(
                swap.Market.BaseToken.Decimals));
		return new()
		{
			Price = price,
            Volume = volume,
        };
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
        if (subscription.Volume is decimal volume &&
            swap.RequestedVolume != volume)
            return false;
        return (subscription.From is null ||
                swap.SubmittedTime >= subscription.From) &&
            (subscription.To is null ||
                swap.SubmittedTime <= subscription.To);
    }

    private static bool IsSuccessful(OneInchRpcReceipt receipt)
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

    private static void RemoveFingerprintPrefix<TValue>(
        IDictionary<string, TValue> values, long target)
    {
        var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
        foreach (var key in values.Keys.Where(key =>
            key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            values.Remove(key);
    }

    private async ValueTask CompleteOrderStatusAsync(
        OrderStatusMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }

    private static string NormalizeTransactionHash(string value)
        => value.NormalizeHash();
}
