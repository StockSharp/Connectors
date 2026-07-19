namespace StockSharp.PintuPro;

public partial class PintuProMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        var securityTypes = lookupMsg.GetSecurityTypes();
        var requestedSymbol = lookupMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : lookupMsg.SecurityId.SecurityCode.NormalizeSymbol();
        MarketDefinition[] markets;
        using (_sync.EnterScope())
            markets = [.. _markets.Values];

        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var market in markets.OrderBy(static value => value.Symbol,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    BoardCodes.PintuPro))
                continue;
            if (!requestedSymbol.IsEmpty() &&
                !requestedSymbol.EqualsIgnoreCase(market.Symbol) &&
                !requestedSymbol.CompactSymbol().EqualsIgnoreCase(
                    market.Symbol.CompactSymbol()))
                continue;
            var security = CreateSecurity(market, lookupMsg.TransactionId);
            if (!security.IsMatch(lookupMsg, securityTypes))
                continue;
            if (skip-- > 0)
                continue;
            await SendOutMessageAsync(security, cancellationToken);
            await SendOutMessageAsync(new Level1ChangeMessage
            {
                SecurityId = security.SecurityId,
                ServerTime = market.Reference.LastUpdatedAt
                    .FromPintuProTimestamp(CurrentTime),
                OriginalTransactionId = lookupMsg.TransactionId,
            }.TryAdd(Level1Fields.State, SecurityStates.Trading),
                cancellationToken);
            if (--left <= 0)
                break;
        }
        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
        EnsureConnected();
        if (!mdMsg.IsSubscribe)
        {
            await UnsubscribeLevel1Async(mdMsg.OriginalTransactionId,
                cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.From is not null)
            throw new NotSupportedException(
                "Pintu Pro does not expose historical Level1 events.");

        var market = GetMarket(mdMsg.SecurityId);
        var book = await RestClient.GetBookAsync(market.Symbol, 1,
            cancellationToken);
        var trades = await RestClient.GetTradesAsync(market.Symbol,
            cancellationToken);
        await SendLevel1SnapshotAsync(market.Symbol, book,
            trades?.Trades?.OrderBy(static trade => trade.Timestamp).LastOrDefault(),
            mdMsg.TransactionId,
            book?.ServerTimestamp.FromPintuProTimestamp(CurrentTime) ??
                CurrentTime,
            cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
            });
        var bookAcquired = false;
        try
        {
            await AcquireStreamAsync(StreamTypes.Book, market.Symbol,
                cancellationToken);
            bookAcquired = true;
            await AcquireStreamAsync(StreamTypes.Trade, market.Symbol,
                cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            if (bookAcquired)
                await ReleaseStreamAsync(StreamTypes.Book, market.Symbol,
                    CancellationToken.None);
            using (_sync.EnterScope())
                _level1Subscriptions.Remove(mdMsg.TransactionId);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
        EnsureConnected();
        if (!mdMsg.IsSubscribe)
        {
            await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId,
                cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.From is not null)
            throw new NotSupportedException(
                "Pintu Pro does not expose historical order-book events.");

        var market = GetMarket(mdMsg.SecurityId);
        var depth = (mdMsg.MaxDepth ?? 10).Min(10).Max(1);
        var book = await RestClient.GetBookAsync(market.Symbol,
            depth == 1 ? 1 : 10, cancellationToken);
        await SendBookAsync(market.Symbol, book, depth, mdMsg.TransactionId,
            book?.ServerTimestamp.FromPintuProTimestamp(CurrentTime) ??
                CurrentTime,
            cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
                Depth = depth,
            });
        try
        {
            await AcquireStreamAsync(StreamTypes.Book, market.Symbol,
                cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _depthSubscriptions.Remove(mdMsg.TransactionId);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
        EnsureConnected();
        if (!mdMsg.IsSubscribe)
        {
            await UnsubscribeTicksAsync(mdMsg.OriginalTransactionId,
                cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        var market = GetMarket(mdMsg.SecurityId);
        var response = await RestClient.GetTradesAsync(market.Symbol,
            cancellationToken);
        var from = mdMsg.From?.ToUniversalTime();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
        var requestedCount = (mdMsg.Count ?? long.MaxValue).Min(int.MaxValue)
            .To<int>();
        foreach (var trade in (response?.Trades ?? [])
            .Where(trade => trade is not null &&
                (from is null || trade.Timestamp.FromPintuProTimestamp(
                    DateTime.MinValue) >= from) &&
                trade.Timestamp.FromPintuProTimestamp(DateTime.MaxValue) <= to)
            .OrderBy(static trade => trade.Timestamp)
            .TakeLast(requestedCount))
            await SendPublicTradeAsync(market.Symbol, trade,
                mdMsg.TransactionId, cancellationToken);

        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
            });
        try
        {
            await AcquireStreamAsync(StreamTypes.Trade, market.Symbol,
                cancellationToken);
            await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _tickSubscriptions.Remove(mdMsg.TransactionId);
            throw;
        }
    }

    private static SecurityMessage CreateSecurity(MarketDefinition market,
        long originalTransactionId)
        => new()
        {
            SecurityId = market.Symbol.ToStockSharp(),
            Name = $"{market.BaseCurrency}/{market.QuoteCurrency}",
            ShortName = $"{market.BaseCurrency}/{market.QuoteCurrency}",
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = market.QuoteCurrency.ToCurrency(),
            PriceStep = market.Reference.PriceTickSize > 0
                ? market.Reference.PriceTickSize
                : null,
            VolumeStep = market.Reference.QuantityTickSize > 0
                ? market.Reference.QuantityTickSize
                : null,
            MinVolume = market.Reference.MinimumSize is > 0
                ? market.Reference.MinimumSize
                : null,
            MaxVolume = market.Reference.MaximumSize is > 0
                ? market.Reference.MaximumSize
                : null,
            OriginalTransactionId = originalTransactionId,
        };

    private async ValueTask UnsubscribeLevel1Async(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _level1Subscriptions.Remove(transactionId, out subscription);
        if (subscription is null)
            return;
        await ReleaseStreamAsync(StreamTypes.Book, subscription.Symbol,
            cancellationToken);
        await ReleaseStreamAsync(StreamTypes.Trade, subscription.Symbol,
            cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        DepthSubscription subscription = null;
        using (_sync.EnterScope())
            _depthSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(StreamTypes.Book, subscription.Symbol,
                cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId,
        CancellationToken cancellationToken)
    {
        MarketSubscription subscription = null;
        using (_sync.EnterScope())
            _tickSubscriptions.Remove(transactionId, out subscription);
        if (subscription is not null)
            await ReleaseStreamAsync(StreamTypes.Trade, subscription.Symbol,
                cancellationToken);
    }

    private async ValueTask OnSocketBookAsync(PintuProBookStreamMessage message,
        CancellationToken cancellationToken)
    {
        if (message?.Data is null)
            return;
        var wireSymbol = message.Data.Symbol;
        if (wireSymbol.IsEmpty() &&
            message.Channel?.StartsWith("aggrbook.snapshot.",
                StringComparison.OrdinalIgnoreCase) == true)
            wireSymbol = message.Channel.Split('.').LastOrDefault();
        if (wireSymbol.IsEmpty())
            return;
        var symbol = GetMarket(wireSymbol).Symbol;
        KeyValuePair<long, DepthSubscription>[] depthSubscriptions;
        KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
        using (_sync.EnterScope())
        {
            depthSubscriptions = [.. _depthSubscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol))];
            level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
                pair.Value.Symbol.EqualsIgnoreCase(symbol))];
        }
        var serverTime = message.Timestamp.FromPintuProTimestamp(CurrentTime);
        foreach (var pair in depthSubscriptions)
            await SendBookAsync(symbol, message.Data, pair.Value.Depth, pair.Key,
                serverTime, cancellationToken);
        foreach (var pair in level1Subscriptions)
            await SendLevel1BookAsync(symbol, message.Data, pair.Key, serverTime,
                cancellationToken);
    }

    private async ValueTask OnSocketTradeAsync(
        PintuProPublicTradeStreamMessage message,
        CancellationToken cancellationToken)
    {
        foreach (var trade in (message?.Data?.Trades ?? [])
            .Where(static value => value is not null)
            .OrderBy(static value => value.Timestamp))
        {
            var wireSymbol = trade.Symbol;
            if (wireSymbol.IsEmpty() && message.Channel?.StartsWith("trades.",
                StringComparison.OrdinalIgnoreCase) == true)
                wireSymbol = message.Channel["trades.".Length..];
            if (wireSymbol.IsEmpty())
                continue;
            var symbol = GetMarket(wireSymbol).Symbol;
            var identifier = CreatePublicTradeId(symbol, trade);
            if (!AddPublicTrade(identifier))
                continue;
            KeyValuePair<long, MarketSubscription>[] tickSubscriptions;
            KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
            using (_sync.EnterScope())
            {
                tickSubscriptions = [.. _tickSubscriptions.Where(pair =>
                    pair.Value.Symbol.EqualsIgnoreCase(symbol))];
                level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
                    pair.Value.Symbol.EqualsIgnoreCase(symbol))];
            }
            foreach (var pair in tickSubscriptions)
                await SendPublicTradeAsync(symbol, trade, pair.Key,
                    cancellationToken, identifier);
            foreach (var pair in level1Subscriptions)
                await SendLevel1TradeAsync(symbol, trade, pair.Key,
                    cancellationToken);
        }
    }

    private async ValueTask SendLevel1SnapshotAsync(string symbol,
        PintuProBookData book, PintuProPublicTrade trade, long transactionId,
        DateTime serverTime, CancellationToken cancellationToken)
    {
        var message = new Level1ChangeMessage
        {
            SecurityId = symbol.ToStockSharp(),
            ServerTime = serverTime,
            OriginalTransactionId = transactionId,
        };
        AddBookFields(message, book);
        AddTradeFields(message, trade);
        await SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendLevel1BookAsync(string symbol, PintuProBookData book,
        long transactionId, DateTime serverTime,
        CancellationToken cancellationToken)
    {
        var message = new Level1ChangeMessage
        {
            SecurityId = symbol.ToStockSharp(),
            ServerTime = serverTime,
            OriginalTransactionId = transactionId,
        };
        AddBookFields(message, book);
        return SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendLevel1TradeAsync(string symbol,
        PintuProPublicTrade trade, long transactionId,
        CancellationToken cancellationToken)
    {
        var message = new Level1ChangeMessage
        {
            SecurityId = symbol.ToStockSharp(),
            ServerTime = trade.Timestamp.FromPintuProTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
        };
        AddTradeFields(message, trade);
        return SendOutMessageAsync(message, cancellationToken);
    }

    private static void AddBookFields(Level1ChangeMessage message,
        PintuProBookData book)
    {
        var bid = (book?.Bids ?? []).Where(static value => value is not null &&
            value.Price > 0 && value.Quantity > 0)
            .OrderByDescending(static value => value.Price).FirstOrDefault();
        var ask = (book?.Asks ?? []).Where(static value => value is not null &&
            value.Price > 0 && value.Quantity > 0)
            .OrderBy(static value => value.Price).FirstOrDefault();
        message.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
            .TryAdd(Level1Fields.BestBidVolume, bid?.Quantity)
            .TryAdd(Level1Fields.BestAskPrice, ask?.Price)
            .TryAdd(Level1Fields.BestAskVolume, ask?.Quantity);
    }

    private static void AddTradeFields(Level1ChangeMessage message,
        PintuProPublicTrade trade)
    {
        if (trade is null)
            return;
        message.TryAdd(Level1Fields.LastTradePrice, trade.Price)
            .TryAdd(Level1Fields.LastTradeVolume, trade.Size)
            .TryAdd(Level1Fields.LastTradeTime,
                trade.Timestamp.FromPintuProTimestamp(message.ServerTime))
            .TryAdd(Level1Fields.LastTradeOrigin, trade.Side.ToStockSharp());
    }

    private ValueTask SendBookAsync(string symbol, PintuProBookData book,
        int depth, long transactionId, DateTime serverTime,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = symbol.ToStockSharp(),
            ServerTime = serverTime,
            OriginalTransactionId = transactionId,
            State = QuoteChangeStates.SnapshotComplete,
            Bids = ToQuotes(book?.Bids, false, depth),
            Asks = ToQuotes(book?.Asks, true, depth),
        }, cancellationToken);

    private ValueTask SendPublicTradeAsync(string symbol,
        PintuProPublicTrade trade, long transactionId,
        CancellationToken cancellationToken, string identifier = null)
    {
        if (trade is null || trade.Price <= 0 || trade.Size <= 0)
            return default;
        identifier ??= CreatePublicTradeId(symbol, trade);
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = symbol.ToStockSharp(),
            ServerTime = trade.Timestamp.FromPintuProTimestamp(CurrentTime),
            OriginalTransactionId = transactionId,
            TradeStringId = identifier,
            TradePrice = trade.Price,
            TradeVolume = trade.Size,
            OriginSide = trade.Side.ToStockSharp(),
        }, cancellationToken);
    }

    private static string CreatePublicTradeId(string symbol,
        PintuProPublicTrade trade)
        => $"{symbol}:{trade.Timestamp.ToString(CultureInfo.InvariantCulture)}:" +
            $"{trade.Side.ToApiValue()}:{trade.Price.ToWire()}:{trade.Size.ToWire()}";

    private static QuoteChange[] ToQuotes(
        IEnumerable<PintuProBookLevel> levels, bool isAsk, int depth)
    {
        var filtered = (levels ?? []).Where(static level => level is not null &&
            level.Price > 0 && level.Quantity > 0);
        return [.. (isAsk
            ? filtered.OrderBy(static level => level.Price)
            : filtered.OrderByDescending(static level => level.Price))
            .Take(depth).Select(static level =>
                new QuoteChange(level.Price, level.Quantity)
                {
                    OrdersCount = level.OrderCount > 0
                        ? level.OrderCount
                        : null,
                })];
    }

    private async ValueTask CompleteMarketSubscriptionAsync(
        MarketDataMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
