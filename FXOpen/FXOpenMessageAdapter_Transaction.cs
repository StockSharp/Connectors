namespace StockSharp.FXOpen;

public partial class FXOpenMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivate();
        ValidatePortfolio(regMsg.PortfolioName);
        var condition = regMsg.Condition as FXOpenOrderCondition ?? new();
        var comment = regMsg.Comment.IsEmpty(condition.Comment);

        if (condition.IsWithdraw)
        {
            var positionId = condition.PositionId is > 0
                ? condition.PositionId.Value
                : throw new InvalidOperationException(
                "FXOpen position trade identifier is required to close a position.");
            if (condition.CloseByPositionId is <= 0)
                throw new InvalidOperationException(
                    "FXOpen close-by trade identifier must be positive.");
            var closeByPositionId = condition.CloseByPositionId is > 0
                ? condition.CloseByPositionId : null;
            if (closeByPositionId == positionId)
                throw new InvalidOperationException(
                    "FXOpen close-by positions must have different trade identifiers.");
            var deleteType = closeByPositionId is not null
                ? TickTraderDeleteTypes.CloseBy : TickTraderDeleteTypes.Close;
            this.AddInfoLog("FXOpen {0} position {1}, transaction {2}.", deleteType,
                positionId, regMsg.TransactionId);
            var result = await RestClient.DeleteTradeAsync(positionId, deleteType,
                deleteType == TickTraderDeleteTypes.Close && regMsg.Volume > 0
                    ? regMsg.Volume : null,
                closeByPositionId, cancellationToken);
            if (result?.Trade is not null)
                await SendTrade(result.Trade, regMsg.TransactionId, cancellationToken);
            if (result?.ByTrade is not null)
                await SendTrade(result.ByTrade, regMsg.TransactionId, cancellationToken);
            return;
        }

        if (regMsg.Volume <= 0)
            throw new ArgumentOutOfRangeException(nameof(regMsg.Volume), regMsg.Volume,
                "FXOpen trade amount must be positive.");
        if (regMsg.PostOnly == true)
            throw new NotSupportedException("FXOpen Web API does not expose post-only orders.");
        var slippage = regMsg.Slippage ?? condition.Slippage;
        if (slippage is < 0)
            throw new ArgumentOutOfRangeException(nameof(regMsg.Slippage), slippage,
                "FXOpen slippage cannot be negative.");

        var nativeSymbol = ResolveSymbol(regMsg.SecurityId).Symbol;
        var orderType = regMsg.OrderType ?? OrderTypes.Limit;
        var nativeType = orderType switch
        {
            OrderTypes.Market => TickTraderOrderTypes.Market,
            OrderTypes.Limit when regMsg.Price > 0 => TickTraderOrderTypes.Limit,
            OrderTypes.Conditional when condition.StopPrice is > 0 && regMsg.Price > 0 =>
                TickTraderOrderTypes.StopLimit,
            OrderTypes.Conditional when condition.StopPrice is > 0 => TickTraderOrderTypes.Stop,
            OrderTypes.Limit => throw new InvalidOperationException(
                "FXOpen limit orders require a positive price."),
            OrderTypes.Conditional => throw new InvalidOperationException(
                "FXOpen stop orders require an activation price."),
            _ => throw new NotSupportedException(
                $"FXOpen does not support StockSharp order type '{orderType}'."),
        };
        if (regMsg.VisibleVolume is > 0 && nativeType != TickTraderOrderTypes.Limit)
            throw new NotSupportedException(
                "FXOpen visible volume is supported only for limit orders.");
        if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume > regMsg.Volume)
            throw new ArgumentOutOfRangeException(nameof(regMsg.VisibleVolume),
                regMsg.VisibleVolume, "FXOpen visible volume cannot exceed order volume.");

        var clientId = CreateClientId(regMsg.TransactionId, regMsg.UserOrderId);
        using (_sync.EnterScope())
            _clientTransactions[clientId] = regMsg.TransactionId;
        this.AddInfoLog("Registering FXOpen {0} {1} {2} {3}, transaction {4}.",
            nativeType, regMsg.Side, nativeSymbol, regMsg.Volume, regMsg.TransactionId);
        TickTraderTrade trade;
        try
        {
            trade = await RestClient.CreateTradeAsync(new TickTraderTradeCreate
            {
                ClientId = clientId,
                Type = nativeType,
                Side = regMsg.Side.ToNative(),
                Symbol = nativeSymbol,
                Price = nativeType is TickTraderOrderTypes.Limit or TickTraderOrderTypes.StopLimit
                    ? regMsg.Price : null,
                StopPrice = nativeType is TickTraderOrderTypes.Stop or TickTraderOrderTypes.StopLimit
                    ? condition.StopPrice : null,
                Amount = regMsg.Volume,
                MaxVisibleAmount = regMsg.VisibleVolume is > 0 ? regMsg.VisibleVolume : null,
                StopLoss = condition.StopLoss,
                TakeProfit = condition.TakeProfit,
                Expired = regMsg.TillDate?.EnsureUtc(),
                ImmediateOrCancel = regMsg.TimeInForce == TimeInForce.CancelBalance ? true : null,
                FillOrKill = regMsg.TimeInForce == TimeInForce.MatchOrCancel ? true : null,
                MarketWithSlippage = slippage is not null ? true : null,
                Slippage = slippage,
                Comment = comment,
            }, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _clientTransactions.Remove(clientId);
            throw;
        }
        if (trade is null || trade.Id <= 0)
        {
            using (_sync.EnterScope())
                _clientTransactions.Remove(clientId);
            throw new InvalidOperationException("FXOpen returned no trade identifier.");
        }
        using (_sync.EnterScope())
            _orderTransactions[regMsg.TransactionId] = trade.Id;
        this.AddInfoLog("FXOpen trade {0} registered for transaction {1}.", trade.Id,
            regMsg.TransactionId);
        await SendTrade(trade, regMsg.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivate();
        ValidatePortfolio(replaceMsg.PortfolioName);
        var orderId = ResolveOrderId(replaceMsg.OldOrderId, replaceMsg.OriginalTransactionId);
        var current = await RestClient.GetTradeAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException($"FXOpen trade {orderId} was not found.");
        var condition = replaceMsg.Condition as FXOpenOrderCondition ?? new FXOpenOrderCondition
        {
            StopPrice = current.StopPrice,
            StopLoss = current.StopLoss,
            TakeProfit = current.TakeProfit,
            Slippage = current.Slippage,
            Comment = current.Comment,
        };
        var comment = replaceMsg.Comment.IsEmpty(condition.Comment);
        var slippage = replaceMsg.Slippage ?? condition.Slippage;
        if (slippage is < 0)
            throw new ArgumentOutOfRangeException(nameof(replaceMsg.Slippage), slippage,
                "FXOpen slippage cannot be negative.");
        if (replaceMsg.VisibleVolume is > 0 && replaceMsg.Volume > 0 &&
            replaceMsg.VisibleVolume > replaceMsg.Volume)
            throw new ArgumentOutOfRangeException(nameof(replaceMsg.VisibleVolume),
                replaceMsg.VisibleVolume, "FXOpen visible volume cannot exceed order volume.");
        var filledAmount = current.FilledAmount ??
            Math.Max(0, current.InitialAmount - current.CurrentAmount);
        if (replaceMsg.Volume > 0 && replaceMsg.Volume < filledAmount)
            throw new ArgumentOutOfRangeException(nameof(replaceMsg.Volume), replaceMsg.Volume,
                "FXOpen replacement volume cannot be less than the already filled amount.");
        this.AddInfoLog("Replacing FXOpen trade {0}, transaction {1}.", orderId,
            replaceMsg.TransactionId);
        var trade = await RestClient.ModifyTradeAsync(new TickTraderTradeModify
        {
            Id = orderId,
            Price = replaceMsg.Price > 0 ? replaceMsg.Price : null,
            StopPrice = condition.StopPrice,
            AmountChange = replaceMsg.Volume > 0
                ? replaceMsg.Volume - current.InitialAmount : null,
            MaxVisibleAmount = replaceMsg.VisibleVolume is > 0
                ? replaceMsg.VisibleVolume : null,
            StopLoss = condition.StopLoss,
            TakeProfit = condition.TakeProfit,
            Expired = replaceMsg.TillDate?.EnsureUtc(),
            ImmediateOrCancel = replaceMsg.TimeInForce is null ? null :
                replaceMsg.TimeInForce == TimeInForce.CancelBalance,
            FillOrKill = replaceMsg.TimeInForce is null ? null :
                replaceMsg.TimeInForce == TimeInForce.MatchOrCancel,
            Slippage = slippage,
            Comment = comment,
        }, cancellationToken);
        if (trade is null || trade.Id <= 0)
            throw new InvalidOperationException("FXOpen returned no modified trade.");
        using (_sync.EnterScope())
            _orderTransactions[replaceMsg.TransactionId] = orderId;
        await SendTrade(trade, replaceMsg.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
        CancellationToken cancellationToken)
    {
        EnsurePrivate();
        ValidatePortfolio(cancelMsg.PortfolioName);
        var orderId = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OriginalTransactionId);
        var current = await RestClient.GetTradeAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException($"FXOpen trade {orderId} was not found.");
        var deleteType = current.Type == TickTraderOrderTypes.Position
            ? TickTraderDeleteTypes.Close : TickTraderDeleteTypes.Cancel;
        this.AddInfoLog("FXOpen {0} trade {1}, transaction {2}.", deleteType, orderId,
            cancelMsg.TransactionId);
        var result = await RestClient.DeleteTradeAsync(orderId, deleteType,
            deleteType == TickTraderDeleteTypes.Close && cancelMsg.Volume is > 0
                ? cancelMsg.Volume : null,
            null, cancellationToken);
        if (result?.Trade is not null)
            await SendTrade(result.Trade, cancelMsg.TransactionId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
        if (!statusMsg.IsSubscribe)
        {
            if (_orderSubscriptionId == statusMsg.OriginalTransactionId)
                _orderSubscriptionId = 0;
            return;
        }

        EnsurePrivate();
        ValidatePortfolio(statusMsg.PortfolioName);
        var remaining = Math.Max(0, statusMsg.Count ?? long.MaxValue);
        var skip = Math.Max(0, statusMsg.Skip ?? 0);
        foreach (var trade in await RestClient.GetTradesAsync(cancellationToken))
        {
            if (!IsOrderMatch(trade, statusMsg))
                continue;
            if (skip > 0)
            {
                skip--;
                continue;
            }
            if (remaining <= 0)
                break;
            await SendTrade(trade, statusMsg.TransactionId, cancellationToken);
            remaining--;
        }

        if ((statusMsg.IsHistoryOnly() || statusMsg.From is not null || statusMsg.To is not null ||
            statusMsg.Count is not null) && remaining > 0)
        {
            var historyCount = statusMsg.Count is null ? Math.Min(1000, remaining) : remaining;
            await SendTradeHistory(statusMsg, historyCount, skip, cancellationToken);
        }

        if (statusMsg.IsHistoryOnly())
        {
            await SendSubscriptionResultAsync(statusMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
            return;
        }

        _orderSubscriptionId = statusMsg.TransactionId;
        this.AddDebugLog("FXOpen order stream subscribed, transaction {0}.",
            statusMsg.TransactionId);
        await SendSubscriptionResultAsync(statusMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
        if (!lookupMsg.IsSubscribe)
        {
            if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
                _portfolioSubscriptionId = 0;
            return;
        }

        EnsurePrivate();
        await SendPortfolioSnapshot(lookupMsg.TransactionId, true, cancellationToken);
        if (lookupMsg.IsHistoryOnly())
        {
            await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
            await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
            return;
        }

        _portfolioSubscriptionId = lookupMsg.TransactionId;
        this.AddDebugLog("FXOpen portfolio stream subscribed, transaction {0}.",
            lookupMsg.TransactionId);
        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
    }

    private async ValueTask SendPortfolioSnapshot(long originalTransactionId, bool sendPortfolio,
        CancellationToken cancellationToken)
    {
        var account = await RestClient.GetAccountAsync(cancellationToken);
        _portfolioName = GetPortfolioName(account);
        await SendAccount(account, originalTransactionId, sendPortfolio, cancellationToken);

        switch (account.AccountingType)
        {
            case TickTraderAccountingTypes.Cash:
                foreach (var asset in await RestClient.GetAssetsAsync(cancellationToken))
                {
                    await SendOutMessageAsync(new PositionChangeMessage
                    {
                        OriginalTransactionId = originalTransactionId,
                        PortfolioName = _portfolioName,
                        SecurityId = asset.Currency.ToSecurityId(),
                        ServerTime = DateTime.UtcNow,
                    }
                    .Add(PositionChangeTypes.CurrentValue, asset.Amount)
                    .TryAdd(PositionChangeTypes.BlockedValue,
                        asset.LockedAmount > 0 ? asset.LockedAmount : null), cancellationToken);
                }
                break;
            case TickTraderAccountingTypes.Net:
                foreach (var position in await RestClient.GetPositionsAsync(cancellationToken))
                    await SendPosition(position, originalTransactionId, cancellationToken);
                break;
            default:
                foreach (var group in (await RestClient.GetTradesAsync(cancellationToken))
                    .Where(static trade => trade.Type == TickTraderOrderTypes.Position)
                    .GroupBy(static trade => trade.Symbol, StringComparer.OrdinalIgnoreCase))
                    await SendGrossPosition(group.Key, group, originalTransactionId, cancellationToken);
                break;
        }
    }

    private async ValueTask SendAccount(TickTraderAccount account, long originalTransactionId,
        bool sendPortfolio, CancellationToken cancellationToken)
    {
        if (account is null)
            return;
        _portfolioName = GetPortfolioName(account);
        if (sendPortfolio)
        {
            await SendOutMessageAsync(new PortfolioMessage
            {
                OriginalTransactionId = originalTransactionId,
                PortfolioName = _portfolioName,
                BoardCode = BoardCodes.FXOpen,
                Currency = account.BalanceCurrency?.FromMicexCurrencyName(this.AddErrorLog),
            }, cancellationToken);
        }
        await SendOutMessageAsync(new PositionChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            PortfolioName = _portfolioName,
            SecurityId = SecurityId.Money,
            ServerTime = DateTime.UtcNow,
        }
        .TryAdd(PositionChangeTypes.BeginValue, account.Balance)
        .TryAdd(PositionChangeTypes.CurrentValue, account.Equity ?? account.Balance)
        .TryAdd(PositionChangeTypes.BlockedValue, account.Margin)
        .TryAdd(PositionChangeTypes.Leverage, account.Leverage)
        .TryAdd(PositionChangeTypes.UnrealizedPnL, account.Profit)
        .TryAdd(PositionChangeTypes.Commission, account.Commission)
        .TryAdd(PositionChangeTypes.VariationMargin, account.Swap), cancellationToken);
    }

    private ValueTask SendPosition(TickTraderPosition position, long originalTransactionId,
        CancellationToken cancellationToken)
    {
        var value = position.LongAmount - position.ShortAmount;
        var averagePrice = value > 0 ? position.LongPrice : value < 0 ? position.ShortPrice : 0;
        var currentPrice = value > 0 ? position.CurrentBestBid :
            value < 0 ? position.CurrentBestAsk : null;
        return SendOutMessageAsync(new PositionChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            PortfolioName = _portfolioName,
            SecurityId = position.Symbol.ToSecurityId(),
            ServerTime = position.Modified?.EnsureUtc() ?? DateTime.UtcNow,
        }
        .Add(PositionChangeTypes.CurrentValue, value)
        .TryAdd(PositionChangeTypes.AveragePrice, averagePrice > 0 ? averagePrice : null)
        .TryAdd(PositionChangeTypes.CurrentPrice, currentPrice)
        .TryAdd(PositionChangeTypes.BlockedValue, position.Margin)
        .TryAdd(PositionChangeTypes.UnrealizedPnL, position.Profit)
        .TryAdd(PositionChangeTypes.Commission, position.Commission)
        .TryAdd(PositionChangeTypes.VariationMargin, position.Swap), cancellationToken);
    }

    private ValueTask SendGrossPosition(string symbol, IEnumerable<TickTraderTrade> trades,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        var items = trades.ToArray();
        var value = items.Sum(trade => trade.CurrentAmount *
            (trade.Side == TickTraderOrderSides.Buy ? 1 : -1));
        var total = items.Sum(static trade => Math.Abs(trade.CurrentAmount));
        var price = total == 0 ? 0 : items.Sum(trade =>
            Math.Abs(trade.CurrentAmount) * (trade.Price ?? 0)) / total;
        return SendOutMessageAsync(new PositionChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            PortfolioName = _portfolioName,
            SecurityId = symbol.ToSecurityId(),
            ServerTime = DateTime.UtcNow,
        }
        .Add(PositionChangeTypes.CurrentValue, value)
        .TryAdd(PositionChangeTypes.AveragePrice, price > 0 ? price : null)
        .TryAdd(PositionChangeTypes.BlockedValue, items.Sum(static trade => trade.Margin ?? 0))
        .TryAdd(PositionChangeTypes.UnrealizedPnL, items.Sum(static trade => trade.Profit ?? 0))
        .TryAdd(PositionChangeTypes.Commission, items.Sum(static trade => trade.Commission ?? 0))
        .TryAdd(PositionChangeTypes.VariationMargin, items.Sum(static trade => trade.Swap ?? 0)),
            cancellationToken);
    }

    private async ValueTask SendTrade(TickTraderTrade trade, long lookupTransactionId,
        CancellationToken cancellationToken)
    {
        if (trade is null || trade.Id <= 0 || trade.Symbol.IsEmpty())
            return;
        var localTransactionId = ResolveTransactionId(trade.ClientId);
        if (localTransactionId == 0)
        {
            using (_sync.EnterScope())
                localTransactionId = _orderTransactions.FirstOrDefault(pair => pair.Value == trade.Id).Key;
        }
        var originalTransactionId = lookupTransactionId != 0
            ? lookupTransactionId : localTransactionId != 0 ? localTransactionId : _orderSubscriptionId;
        if (originalTransactionId == 0)
            return;

        var state = trade.Status.ToOrderState();
        var condition = new FXOpenOrderCondition
        {
            StopPrice = trade.StopPrice,
            StopLoss = trade.StopLoss,
            TakeProfit = trade.TakeProfit,
            Slippage = trade.Slippage,
            Comment = trade.Comment,
            PositionId = trade.Type == TickTraderOrderTypes.Position ? trade.Id : null,
        };
        await SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = originalTransactionId,
            TransactionId = localTransactionId,
            OrderId = trade.Id,
            OrderStringId = trade.Id.ToString(CultureInfo.InvariantCulture),
            SecurityId = trade.Symbol.ToSecurityId(),
            PortfolioName = _portfolioName,
            Side = trade.Side.ToSide(),
            OrderType = trade.InitialType.ToOrderType(),
            OrderPrice = trade.Price ?? 0,
            OrderVolume = trade.InitialAmount,
            VisibleVolume = trade.MaxVisibleAmount,
            Balance = trade.CurrentAmount,
            OrderState = state,
            ServerTime = (trade.Modified ?? trade.Filled ?? trade.Created).EnsureUtc(),
            ExpiryDate = trade.Expired?.EnsureUtc(),
            TimeInForce = trade.ImmediateOrCancel ? TimeInForce.CancelBalance :
                trade.FillOrKill ? TimeInForce.MatchOrCancel : null,
            Slippage = trade.Slippage,
            UserOrderId = ParseTransactionId(trade.ClientId) == 0 ? trade.ClientId : null,
            Comment = trade.Comment,
            Condition = condition,
            PnL = trade.Profit,
            Commission = trade.Commission,
            Error = state == OrderStates.Failed
                ? new InvalidOperationException("FXOpen rejected the trade.") : null,
        }, cancellationToken);
    }

    private async ValueTask SendTradeHistory(OrderStatusMessage statusMsg, long requestedCount,
        long requestedSkip, CancellationToken cancellationToken)
    {
        var remaining = requestedCount.Min(100000);
        var skip = requestedSkip.Min(100000);
        var direction = statusMsg.From is null
            ? TickTraderStreamingDirections.Backward
            : TickTraderStreamingDirections.Forward;
        var historyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string lastId = null;
        while (remaining > 0)
        {
            var report = await RestClient.GetTradeHistoryAsync(new()
            {
                TimestampFrom = statusMsg.From?.EnsureUtc(),
                TimestampTo = statusMsg.To?.EnsureUtc(),
                OrderId = statusMsg.OrderId,
                RequestDirection = direction,
                RequestPageSize = (int)Math.Min(100, remaining + skip),
                RequestLastId = lastId,
            }, cancellationToken);
            if (report is null)
                break;
            foreach (var record in report.Records ?? [])
            {
                if (!IsHistoryMatch(record, statusMsg))
                    continue;
                if (!historyIds.Add(record.Id))
                    continue;
                if (skip > 0)
                {
                    skip--;
                    continue;
                }
                await SendHistoryRecord(record, statusMsg.TransactionId, cancellationToken);
                if (--remaining <= 0)
                    break;
            }
            if (remaining <= 0 || report.IsLastReport || report.LastId.IsEmpty() ||
                report.LastId.EqualsIgnoreCase(lastId))
                break;
            lastId = report.LastId;
        }
    }

    private ValueTask SendHistoryRecord(TickTraderHistoryRecord record,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        if (record.Symbol.IsEmpty() || record.TradeSide is null)
            return default;
        var isFill = record.TransactionType is TickTraderTransactionTypes.OrderFilled or
            TickTraderTransactionTypes.PositionOpened or TickTraderTransactionTypes.PositionClosed;
        var orderState = ToHistoryOrderState(record.TransactionType);
        var fillPrice = record.TransactionType switch
        {
            TickTraderTransactionTypes.PositionOpened => record.PositionOpenPrice,
            TickTraderTransactionTypes.PositionClosed => record.PositionClosePrice,
            _ => record.TradeFillPrice ?? record.TradePrice,
        };
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Transactions,
            HasOrderInfo = true,
            OriginalTransactionId = originalTransactionId,
            TransactionId = ResolveTransactionId(record.ClientTradeId),
            OrderId = record.TradeId,
            OrderStringId = record.TradeId.ToString(CultureInfo.InvariantCulture),
            TradeStringId = isFill ? record.Id : null,
            SecurityId = record.Symbol.ToSecurityId(),
            PortfolioName = _portfolioName,
            Side = record.TradeSide.Value.ToSide(),
            OrderType = (record.TradeType ?? TickTraderOrderTypes.Market).ToOrderType(),
            OrderPrice = record.TradePrice ?? record.PositionOpenPrice ??
                record.TradeFillPrice ?? 0,
            OrderVolume = record.TradeInitialAmount ?? record.PositionInitialAmount ??
                record.TradeAmount ?? 0,
            Balance = record.TradeAmount ?? record.PositionAmount ?? 0,
            OrderState = orderState,
            TimeInForce = record.ImmediateOrCancel ? TimeInForce.CancelBalance :
                record.FillOrKill ? TimeInForce.MatchOrCancel : null,
            Slippage = record.Slippage,
            UserOrderId = ParseTransactionId(record.ClientTradeId) == 0
                ? record.ClientTradeId : null,
            Comment = record.Comment,
            TradePrice = isFill ? fillPrice : null,
            TradeVolume = isFill ? record.TradeLastFillAmount ?? record.PositionLastAmount ??
                record.TradeAmount : null,
            ServerTime = record.TransactionTimestamp.EnsureUtc(),
            PnL = record.BalanceMovement,
            Commission = record.Commission,
        }, cancellationToken);
    }

    private async ValueTask ProcessExecutionReport(TickTraderExecutionReport report,
        CancellationToken cancellationToken)
    {
        if (report?.Trade is null)
            return;
        var localTransactionId = ResolveTransactionId(report.Trade.ClientId);
        if (_orderSubscriptionId != 0 || localTransactionId != 0)
            await SendTrade(report.Trade, 0, cancellationToken);
        if (report.Fill is not null && (_orderSubscriptionId != 0 || localTransactionId != 0))
        {
            var originalTransactionId = localTransactionId != 0
                ? localTransactionId : _orderSubscriptionId;
            var fillTime = (report.Trade.Filled ?? report.Trade.Modified ?? report.Trade.Created)
                .EnsureUtc();
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Transactions,
                OriginalTransactionId = originalTransactionId,
                OrderId = report.Trade.Id,
                OrderStringId = report.Trade.Id.ToString(CultureInfo.InvariantCulture),
                TradeStringId = $"{report.Trade.Id}:{report.Event}:{fillTime:O}:" +
                    $"{report.Fill.Amount.ToString(CultureInfo.InvariantCulture)}:" +
                    report.Fill.Price.ToString(CultureInfo.InvariantCulture),
                SecurityId = report.Trade.Symbol.ToSecurityId(),
                PortfolioName = _portfolioName,
                Side = report.Trade.Side.ToSide(),
                TradePrice = report.Fill.Price,
                TradeVolume = report.Fill.Amount,
                ServerTime = fillTime,
                PnL = report.Trade.Profit,
                Commission = report.Trade.Commission,
            }, cancellationToken);
        }

        if (_portfolioSubscriptionId != 0 &&
            (report.Fill is not null || report.Trade.Type == TickTraderOrderTypes.Position))
            await SendPortfolioSnapshot(_portfolioSubscriptionId, false, cancellationToken);
    }

    private ValueTask ProcessAccount(TickTraderAccount account,
        CancellationToken cancellationToken)
        => account is null || _portfolioSubscriptionId == 0
            ? default
            : SendAccount(account, _portfolioSubscriptionId, false, cancellationToken);

    private long ResolveOrderId(long? orderId, long originalTransactionId)
    {
        if (orderId is > 0)
            return orderId.Value;
        using (_sync.EnterScope())
        {
            if (_orderTransactions.TryGetValue(originalTransactionId, out var value))
                return value;
        }
        throw new InvalidOperationException("FXOpen trade identifier is required.");
    }

    private static bool IsOrderMatch(TickTraderTrade trade, OrderStatusMessage message)
    {
        if (trade is null || trade.Id <= 0 || trade.Symbol.IsEmpty())
            return false;
        if (message.OrderId is long orderId && orderId != trade.Id)
            return false;
        if (!message.OrderStringId.IsEmpty() && !message.OrderStringId.EqualsIgnoreCase(
            trade.Id.ToString(CultureInfo.InvariantCulture)))
            return false;
        if (!message.UserOrderId.IsEmpty() &&
            !message.UserOrderId.EqualsIgnoreCase(trade.ClientId))
            return false;
        if (message.SecurityId.SecurityCode.IsEmpty() == false &&
            !message.SecurityId.SecurityCode.EqualsIgnoreCase(trade.Symbol))
            return false;
        if (message.SecurityIds.Length > 0 && !message.SecurityIds.Any(id =>
            id.SecurityCode.EqualsIgnoreCase(trade.Symbol)))
            return false;
        if (message.Side is Sides side && side != trade.Side.ToSide())
            return false;
        if (message.Volume is decimal volume && volume != trade.InitialAmount)
            return false;
        if (message.Balance is decimal balance && balance != trade.CurrentAmount)
            return false;
        if (message.OrderType is OrderTypes orderType &&
            orderType != trade.InitialType.ToOrderType())
            return false;
        var state = trade.Status.ToOrderState();
        if (message.States.Length > 0 && !message.States.Contains(state))
            return false;
        var created = trade.Created.EnsureUtc();
        if (message.From is DateTime from && created < from.EnsureUtc())
            return false;
        return message.To is not DateTime to || created <= to.EnsureUtc();
    }

    private static bool IsHistoryMatch(TickTraderHistoryRecord record, OrderStatusMessage message)
    {
        if (record is null || record.TradeId <= 0 || record.Symbol.IsEmpty())
            return false;
        if (message.OrderId is long orderId && orderId != record.TradeId)
            return false;
        if (!message.OrderStringId.IsEmpty() && !message.OrderStringId.EqualsIgnoreCase(
            record.TradeId.ToString(CultureInfo.InvariantCulture)))
            return false;
        if (!message.UserOrderId.IsEmpty() &&
            !message.UserOrderId.EqualsIgnoreCase(record.ClientTradeId))
            return false;
        if (message.SecurityId.SecurityCode.IsEmpty() == false &&
            !message.SecurityId.SecurityCode.EqualsIgnoreCase(record.Symbol))
            return false;
        if (message.SecurityIds.Length > 0 && !message.SecurityIds.Any(id =>
            id.SecurityCode.EqualsIgnoreCase(record.Symbol)))
            return false;
        if (message.Side is Sides side && record.TradeSide?.ToSide() != side)
            return false;
        if (message.Volume is decimal volume &&
            volume != (record.TradeInitialAmount ?? record.PositionInitialAmount ??
                record.TradeAmount ?? 0))
            return false;
        if (message.Balance is decimal balance &&
            balance != (record.TradeAmount ?? record.PositionAmount ?? 0))
            return false;
        if (message.OrderType is OrderTypes orderType &&
            orderType != (record.TradeType ?? TickTraderOrderTypes.Market).ToOrderType())
            return false;
        if (message.States.Length > 0 &&
            !message.States.Contains(ToHistoryOrderState(record.TransactionType)))
            return false;
        var timestamp = record.TransactionTimestamp.EnsureUtc();
        if (message.From is DateTime from && timestamp < from.EnsureUtc())
            return false;
        return message.To is not DateTime to || timestamp <= to.EnsureUtc();
    }

    private static OrderStates ToHistoryOrderState(TickTraderTransactionTypes type)
        => type is TickTraderTransactionTypes.OrderOpened or
            TickTraderTransactionTypes.OrderActivated or TickTraderTransactionTypes.TradeModified or
            TickTraderTransactionTypes.PositionOpened
                ? OrderStates.Active
                : OrderStates.Done;
}
