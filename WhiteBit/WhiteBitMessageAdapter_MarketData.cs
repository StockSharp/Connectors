namespace StockSharp.WhiteBit;

public partial class WhiteBitMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
        EnsureConnected();

        var securityTypes = lookupMsg.GetSecurityTypes();
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var market in await RestClient.GetMarketsAsync(cancellationToken) ?? [])
        {
            if (market?.Name.IsEmpty() != false)
                continue;

            var sections = GetMarketSections(market);
            foreach (var section in sections)
            {
                if (!IsSectionEnabled(section))
                    continue;

                var securityType = section == WhiteBitSections.Futures
                    ? SecurityTypes.Future
                    : SecurityTypes.CryptoCurrency;
                if (securityTypes.Count > 0 && !securityTypes.Contains(securityType))
                    continue;

                var boardCode = section.ToBoardCode();
                using (_sync.EnterScope())
                {
                    if (section == WhiteBitSections.Futures || !_marketBoards.ContainsKey(market.Name))
                        _marketBoards[market.Name] = boardCode;
                }

                var priceStep = 1m / (decimal)Math.Pow(10, market.MoneyPrecision);
                var volumeStep = 1m / (decimal)Math.Pow(10, market.StockPrecision);
                var security = new SecurityMessage
                {
                    SecurityId = ToSecurityId(market.Name, boardCode),
                    Name = market.Name,
                    SecurityType = securityType,
                    OriginalTransactionId = lookupMsg.TransactionId,
                    PriceStep = priceStep,
                    Decimals = market.MoneyPrecision,
                    VolumeStep = volumeStep,
                    MinVolume = market.MinAmount.ToDecimal(),
                }.TryFillUnderlyingId(market.Stock?.ToUpperInvariant());

                if (!security.IsMatch(lookupMsg, securityTypes))
                    continue;

                await SendOutMessageAsync(security, cancellationToken);
                if (--left <= 0)
                    break;
            }

            if (left <= 0)
                break;
        }

        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
        EnsureConnected();

        if (!mdMsg.IsSubscribe)
        {
            await UnsubscribeLevel1Async(mdMsg.OriginalTransactionId, cancellationToken);
            return;
        }

        var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
        var boardCode = ResolveBoardCode(mdMsg.SecurityId);
        var ticker = (await RestClient.GetTickersAsync(cancellationToken))
            .FirstOrDefault(item => item.Symbol.EqualsIgnoreCase(symbol));
        if (ticker is not null)
            await SendTickerAsync(ticker, symbol, boardCode, mdMsg.TransactionId, cancellationToken);

        var book = await RestClient.GetOrderBookAsync(symbol, 1, cancellationToken);
        await SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = ToSecurityId(symbol, boardCode),
            ServerTime = book.Timestamp > 0 ? book.Timestamp.ToUtcTime() : CurrentTime,
            OriginalTransactionId = mdMsg.TransactionId,
        }
        .TryAdd(Level1Fields.BestBidPrice, book.Bids?.FirstOrDefault()?.Price.ToDecimal())
        .TryAdd(Level1Fields.BestBidVolume, book.Bids?.FirstOrDefault()?.Amount.ToDecimal())
        .TryAdd(Level1Fields.BestAskPrice, book.Asks?.FirstOrDefault()?.Price.ToDecimal())
        .TryAdd(Level1Fields.BestAskVolume, book.Asks?.FirstOrDefault()?.Amount.ToDecimal()), cancellationToken);

        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
            return;
        }

        string[] markets;
        using (_sync.EnterScope())
        {
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = symbol,
                BoardCode = boardCode,
            });
            AddReference(_marketReferences, symbol);
            markets = [.. _marketReferences.Keys];
        }
        await MarketWsClient.SetMarketsAsync(markets, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
        EnsureConnected();

        if (!mdMsg.IsSubscribe)
        {
            await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId, cancellationToken);
            return;
        }

        var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
        var boardCode = ResolveBoardCode(mdMsg.SecurityId);
        var depth = NormalizeDepth(mdMsg.MaxDepth);
        var book = await RestClient.GetOrderBookAsync(symbol, depth, cancellationToken);
        await SendBookAsync(symbol, boardCode, book, QuoteChangeStates.SnapshotComplete,
            mdMsg.TransactionId, depth, cancellationToken);

        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
            return;
        }

        int streamDepth;
        using (_sync.EnterScope())
        {
            _depthSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = symbol,
                BoardCode = boardCode,
                Depth = depth,
                LastSequence = book.UpdateId ?? 0,
            });
            AddReference(_depthReferences, symbol);
            streamDepth = _depthSubscriptions.Values
                .Where(item => item.Symbol.EqualsIgnoreCase(symbol))
                .Max(static item => item.Depth);
        }
        await MarketWsClient.SetDepthAsync(symbol, streamDepth, true, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
        EnsureConnected();

        if (!mdMsg.IsSubscribe)
        {
            await UnsubscribeTicksAsync(mdMsg.OriginalTransactionId, cancellationToken);
            return;
        }

        var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
        var boardCode = ResolveBoardCode(mdMsg.SecurityId);
        var from = mdMsg.From;
        var to = mdMsg.To ?? DateTime.UtcNow;
        var limit = (mdMsg.Count ?? 100).Min(100).To<int>();
        var trades = (await RestClient.GetTradesAsync(symbol, cancellationToken) ?? [])
            .Where(trade => (from is null || trade.Time.ToUtcTime() >= from) && trade.Time.ToUtcTime() <= to)
            .OrderBy(static trade => trade.Time)
            .TakeLast(limit)
            .ToArray();

        long? lastTradeId = null;
        var lastTime = from ?? default;
        foreach (var trade in trades)
        {
            await SendPublicTradeAsync(trade, symbol, boardCode, mdMsg.TransactionId, cancellationToken);
            lastTradeId = trade.TradeId;
            lastTime = trade.Time.ToUtcTime();
        }

        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
            return;
        }

        string[] markets;
        using (_sync.EnterScope())
        {
            _tickSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = symbol,
                BoardCode = boardCode,
                LastTradeId = lastTradeId,
                LastTime = lastTime,
            });
            AddReference(_tradeReferences, symbol);
            markets = [.. _tradeReferences.Keys];
        }
        await MarketWsClient.SetTradesAsync(markets, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
        EnsureConnected();

        if (!mdMsg.IsSubscribe)
        {
            await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId, cancellationToken);
            return;
        }

        var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
        var boardCode = ResolveBoardCode(mdMsg.SecurityId);
        var timeFrame = mdMsg.GetTimeFrame();
        _ = timeFrame.ToNative();
        var candles = await LoadCandlesAsync(symbol, timeFrame, mdMsg.From, mdMsg.To ?? DateTime.UtcNow,
            mdMsg.Count, cancellationToken);
        var lastOpenTime = mdMsg.From ?? default;
        foreach (var candle in candles)
        {
            await SendCandleAsync(candle, symbol, boardCode, timeFrame,
                mdMsg.TransactionId, cancellationToken);
            lastOpenTime = candle.OpenTime.FromUnix(false);
        }

        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
            return;
        }

        var key = (Symbol: symbol, TimeFrame: timeFrame);
        WhiteBitWsClient candleClient = null;
        using (_sync.EnterScope())
        {
            _candleSubscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = symbol,
                BoardCode = boardCode,
                TimeFrame = timeFrame,
                LastOpenTime = lastOpenTime,
            });
            if (AddReference(_candleReferences, key))
            {
                candleClient = new(WsEndpoint, false, null, ReConnectionSettings.WorkingTime)
                {
                    Parent = this,
                };
                candleClient.CandleReceived += (update, token) => OnCandleAsync(update, key, token);
                candleClient.Error += OnWebSocketErrorAsync;
                _candleWsClients.Add(key, candleClient);
            }
        }

        if (candleClient is not null)
        {
            try
            {
                await candleClient.ConnectAsync(cancellationToken);
                await candleClient.SetCandlesAsync([key], cancellationToken);
            }
            catch
            {
                using (_sync.EnterScope())
                {
                    _candleWsClients.Remove(key);
                    _candleReferences.Remove(key);
                    _candleSubscriptions.Remove(mdMsg.TransactionId);
                }
                try
                {
                    await candleClient.DisconnectAsync(cancellationToken);
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                {
                    await SendOutErrorAsync(error, cancellationToken);
                }
                candleClient.Dispose();
                throw;
            }
        }
    }

    private async ValueTask UnsubscribeLevel1Async(long transactionId, CancellationToken cancellationToken)
    {
        string[] markets = null;
        using (_sync.EnterScope())
        {
            if (_level1Subscriptions.Remove(transactionId, out var subscription))
            {
                ReleaseReference(_marketReferences, subscription.Symbol);
                markets = [.. _marketReferences.Keys];
            }
        }
        if (markets is not null)
            await MarketWsClient.SetMarketsAsync(markets, cancellationToken);
    }

    private async ValueTask UnsubscribeDepthAsync(long transactionId, CancellationToken cancellationToken)
    {
        string symbol = null;
        int? streamDepth = null;
        using (_sync.EnterScope())
        {
            if (_depthSubscriptions.Remove(transactionId, out var subscription))
            {
                symbol = subscription.Symbol;
                ReleaseReference(_depthReferences, symbol);
                var remaining = _depthSubscriptions.Values
                    .Where(item => item.Symbol.EqualsIgnoreCase(symbol))
                    .ToArray();
                if (remaining.Length > 0)
                    streamDepth = remaining.Max(static item => item.Depth);
            }
        }

        if (symbol is not null)
            await MarketWsClient.SetDepthAsync(symbol, streamDepth ?? 1, streamDepth is not null, cancellationToken);
    }

    private async ValueTask UnsubscribeTicksAsync(long transactionId, CancellationToken cancellationToken)
    {
        string[] markets = null;
        using (_sync.EnterScope())
        {
            if (_tickSubscriptions.Remove(transactionId, out var subscription))
            {
                ReleaseReference(_tradeReferences, subscription.Symbol);
                markets = [.. _tradeReferences.Keys];
            }
        }
        if (markets is not null)
            await MarketWsClient.SetTradesAsync(markets, cancellationToken);
    }

    private async ValueTask UnsubscribeCandlesAsync(long transactionId, CancellationToken cancellationToken)
    {
        WhiteBitWsClient client = null;
        using (_sync.EnterScope())
        {
            if (_candleSubscriptions.Remove(transactionId, out var subscription))
            {
                var key = (subscription.Symbol, subscription.TimeFrame);
                if (ReleaseReference(_candleReferences, key))
                    _candleWsClients.Remove(key, out client);
            }
        }
        if (client is not null)
        {
            await client.DisconnectAsync(cancellationToken);
            client.Dispose();
        }
    }

    private async ValueTask OnMarketAsync(WhiteBitMarketUpdateParams update,
        CancellationToken cancellationToken)
    {
        if (update?.Symbol.IsEmpty() != false || update.Statistics is null)
            return;

        (long TransactionId, StreamSubscription Subscription)[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _level1Subscriptions
                .Where(pair => pair.Value.Symbol.EqualsIgnoreCase(update.Symbol))
                .Select(static pair => (pair.Key, pair.Value))];

        foreach (var item in subscriptions)
        {
            await SendOutMessageAsync(new Level1ChangeMessage
            {
                SecurityId = ToSecurityId(update.Symbol, item.Subscription.BoardCode),
                ServerTime = CurrentTime,
                OriginalTransactionId = item.TransactionId,
            }
            .TryAdd(Level1Fields.LastTradePrice, update.Statistics.Last.ToDecimal())
            .TryAdd(Level1Fields.OpenPrice, update.Statistics.Open.ToDecimal())
            .TryAdd(Level1Fields.HighPrice, update.Statistics.High.ToDecimal())
            .TryAdd(Level1Fields.LowPrice, update.Statistics.Low.ToDecimal())
            .TryAdd(Level1Fields.Volume, update.Statistics.Volume.ToDecimal())
            .TryAdd(Level1Fields.Change, GetChange(update.Statistics.Last, update.Statistics.Open)),
            cancellationToken);
        }
    }

    private async ValueTask OnDepthAsync(WhiteBitDepthUpdateParams update,
        CancellationToken cancellationToken)
    {
        if (update?.Symbol.IsEmpty() != false || update.Book is null)
            return;

        (long TransactionId, DepthSubscription Subscription)[] subscriptions;
        var isGap = false;
        using (_sync.EnterScope())
        {
            subscriptions = [.. _depthSubscriptions
                .Where(pair => pair.Value.Symbol.EqualsIgnoreCase(update.Symbol))
                .Select(static pair => (pair.Key, pair.Value))];
            if (!update.IsSnapshot && update.Book.PreviousUpdateId is long previous)
                isGap = subscriptions.Any(item => item.Subscription.LastSequence > 0 && item.Subscription.LastSequence != previous);

            if (!isGap)
            {
                foreach (var item in subscriptions)
                    item.Subscription.LastSequence = update.Book.UpdateId ?? item.Subscription.LastSequence;
            }
        }

        if (isGap)
        {
            var depth = subscriptions.Length == 0 ? 100 : subscriptions.Max(static item => item.Subscription.Depth);
            await MarketWsClient.SetDepthAsync(update.Symbol, depth, false, cancellationToken);
            await MarketWsClient.SetDepthAsync(update.Symbol, depth, true, cancellationToken);
            return;
        }

        foreach (var item in subscriptions)
        {
            await SendBookAsync(update.Symbol, item.Subscription.BoardCode, update.Book,
                update.IsSnapshot ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment,
                item.TransactionId, item.Subscription.Depth, cancellationToken);
        }
    }

    private async ValueTask OnTradesAsync(WhiteBitTradesUpdateParams update,
        CancellationToken cancellationToken)
    {
        if (update?.Symbol.IsEmpty() != false)
            return;

        foreach (var trade in (update.Trades ?? []).OrderBy(static item => item.Time))
        {
            (long TransactionId, TickSubscription Subscription)[] subscriptions;
            using (_sync.EnterScope())
            {
                var accepted = new List<(long, TickSubscription)>();
                foreach (var pair in _tickSubscriptions)
                {
                    var state = pair.Value;
                    if (!state.Symbol.EqualsIgnoreCase(update.Symbol) ||
                        (state.LastTradeId is long lastId && trade.TradeId <= lastId) ||
                        (state.LastTradeId is null && state.LastTime != default && trade.Time.ToUtcTime() <= state.LastTime))
                        continue;

                    state.LastTradeId = trade.TradeId;
                    state.LastTime = trade.Time.ToUtcTime();
                    accepted.Add((pair.Key, state));
                }
                subscriptions = [.. accepted];
            }

            foreach (var item in subscriptions)
                await SendPublicTradeAsync(trade, update.Symbol, item.Subscription.BoardCode,
                    item.TransactionId, cancellationToken);
        }
    }

    private async ValueTask OnCandleAsync(WhiteBitCandleUpdateParams update,
        (string Symbol, TimeSpan TimeFrame) stream, CancellationToken cancellationToken)
    {
        foreach (var candle in update?.Candles ?? [])
        {
            if (candle.Symbol.IsEmpty() || !candle.Symbol.EqualsIgnoreCase(stream.Symbol))
                continue;

            var openTime = candle.OpenTime.FromUnix(false);
            (long TransactionId, CandleSubscription Subscription)[] subscriptions;
            using (_sync.EnterScope())
            {
                var accepted = new List<(long, CandleSubscription)>();
                foreach (var pair in _candleSubscriptions)
                {
                    var state = pair.Value;
                    if (!state.Symbol.EqualsIgnoreCase(stream.Symbol) ||
                        state.TimeFrame != stream.TimeFrame ||
                        (state.LastOpenTime != default && openTime < state.LastOpenTime))
                        continue;

                    state.LastOpenTime = openTime;
                    accepted.Add((pair.Key, state));
                }
                subscriptions = [.. accepted];
            }

            foreach (var item in subscriptions)
                await SendCandleAsync(candle, stream.Symbol, item.Subscription.BoardCode,
                    item.Subscription.TimeFrame, item.TransactionId, cancellationToken);
        }
    }

    private async ValueTask<WhiteBitCandle[]> LoadCandlesAsync(string symbol, TimeSpan timeFrame,
        DateTime? from, DateTime to, long? count, CancellationToken cancellationToken)
    {
        var requested = (count ?? 1440).Min(100_000).Max(1);
        var maxBarsToMinimumDate = ((to.Ticks - DateTime.MinValue.Ticks) / timeFrame.Ticks).Max(1);
        requested = requested.Min(maxBarsToMinimumDate);
        var start = from ?? to - TimeSpan.FromTicks(timeFrame.Ticks * requested);
        var result = new List<WhiteBitCandle>();
        var cursor = start;
        while (cursor <= to && result.Count < requested)
        {
            var pageSize = (requested - result.Count).Min(1440).To<int>();
            var pageEnd = (cursor + TimeSpan.FromTicks(timeFrame.Ticks * pageSize)).Min(to);
            var page = await RestClient.GetCandlesAsync(symbol, timeFrame, cursor, pageEnd,
                pageSize, cancellationToken);
            result.AddRange((page ?? []).Where(candle => candle.OpenTime.FromUnix(false) >= start &&
                candle.OpenTime.FromUnix(false) <= to));

            var next = page?.Length > 0
                ? page.Max(static candle => candle.OpenTime).FromUnix(false) + timeFrame
                : pageEnd + timeFrame;
            if (next <= cursor)
                break;
            cursor = next;
        }

        return [.. result.GroupBy(static candle => candle.OpenTime)
            .Select(static group => group.Last())
            .OrderBy(static candle => candle.OpenTime)
            .Take(requested.To<int>())];
    }

    private ValueTask SendTickerAsync(WhiteBitTicker ticker, string symbol, string boardCode,
        long transactionId, CancellationToken cancellationToken)
    {
        var last = ticker.LastPrice.ToDecimal();
        var percent = ticker.ChangePercent.ToDecimal();
        decimal? open = null;
        if (last is decimal lastValue && percent is decimal percentValue && percentValue != -100m)
            open = lastValue / (1m + percentValue / 100m);

        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = ToSecurityId(symbol, boardCode),
            ServerTime = CurrentTime,
            OriginalTransactionId = transactionId,
        }
        .TryAdd(Level1Fields.LastTradePrice, last)
        .TryAdd(Level1Fields.OpenPrice, open)
        .TryAdd(Level1Fields.Volume, ticker.BaseVolume.ToDecimal())
        .TryAdd(Level1Fields.Change, last is decimal current && open is decimal opening ? current - opening : null),
        cancellationToken);
    }

    private ValueTask SendBookAsync(string symbol, string boardCode, WhiteBitOrderBook book,
        QuoteChangeStates state, long transactionId, int maxDepth, CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            SecurityId = ToSecurityId(symbol, boardCode),
            ServerTime = (book.EventTime ?? book.Timestamp) > 0
                ? (book.EventTime ?? book.Timestamp).ToUtcTime()
                : CurrentTime,
            OriginalTransactionId = transactionId,
            State = state,
            Bids = ToQuotes(book.Bids, maxDepth),
            Asks = ToQuotes(book.Asks, maxDepth),
            SeqNum = book.UpdateId ?? 0,
        }, cancellationToken);

    private ValueTask SendPublicTradeAsync(WhiteBitPublicTrade trade, string symbol, string boardCode,
        long transactionId, CancellationToken cancellationToken)
        => SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            SecurityId = ToSecurityId(symbol, boardCode),
            ServerTime = trade.Time.ToUtcTime(),
            TradeId = trade.TradeId,
            TradePrice = trade.Price.ToDecimal(),
            TradeVolume = trade.Amount.ToDecimal(),
            OriginSide = trade.Side.ToStockSharp(),
            OriginalTransactionId = transactionId,
        }, cancellationToken);

    private ValueTask SendCandleAsync(WhiteBitCandle candle, string symbol, string boardCode,
        TimeSpan timeFrame, long transactionId, CancellationToken cancellationToken)
    {
        var openTime = candle.OpenTime.FromUnix(false);
        var closeTime = openTime + timeFrame;
        return SendOutMessageAsync(new TimeFrameCandleMessage
        {
            SecurityId = ToSecurityId(symbol, boardCode),
            TypedArg = timeFrame,
            OpenTime = openTime,
            CloseTime = closeTime,
            OpenPrice = candle.Open.ToDecimal() ?? 0m,
            HighPrice = candle.High.ToDecimal() ?? 0m,
            LowPrice = candle.Low.ToDecimal() ?? 0m,
            ClosePrice = candle.Close.ToDecimal() ?? 0m,
            TotalVolume = candle.Volume.ToDecimal() ?? 0m,
            State = closeTime <= DateTime.UtcNow ? CandleStates.Finished : CandleStates.Active,
            OriginalTransactionId = transactionId,
        }, cancellationToken);
    }

    private static QuoteChange[] ToQuotes(WhiteBitBookLevel[] levels, int maxDepth)
        => [.. (levels ?? [])
            .Take(maxDepth)
            .Select(static level => (Price: level.Price.ToDecimal(), Amount: level.Amount.ToDecimal()))
            .Where(static item => item.Price is not null && item.Amount is not null)
            .Select(static item => new QuoteChange(item.Price.Value, item.Amount.Value))];

    private static decimal? GetChange(string last, string open)
        => last.ToDecimal() is decimal lastValue && open.ToDecimal() is decimal openValue
            ? lastValue - openValue
            : null;

    private string ResolveBoardCode(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty())
            return securityId.BoardCode;

        using (_sync.EnterScope())
            return _marketBoards.TryGetValue(securityId.SecurityCode, out var boardCode)
                ? boardCode
                : BoardCodes.WhiteBit;
    }

    private static WhiteBitSections[] GetMarketSections(WhiteBitMarket market)
    {
        if (market.Type is WhiteBitMarketTypes.Futures or WhiteBitMarketTypes.TradFiFutures)
            return [WhiteBitSections.Futures];
        return market.IsCollateral
            ? [WhiteBitSections.Spot, WhiteBitSections.Margin]
            : [WhiteBitSections.Spot];
    }
}
