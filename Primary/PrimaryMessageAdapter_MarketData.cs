namespace StockSharp.Primary;

public partial class PrimaryMessageAdapter
{
    private sealed class MarketSubscription
    {
        public long TransactionId { get; init; }
        public SecurityId SecurityId { get; init; }
        public PrimarySecurityKey Native { get; init; }
        public DataType DataType { get; init; }
        public string[] Entries { get; init; }
        public int Depth { get; init; }
        public int? MaxDepth { get; init; }
    }

    private static readonly string[] _level1Entries =
    [
        "LA", "OP", "CL", "SE", "OI", "HI", "LO", "TV",
        "IV", "EV", "NV", "ACP", "BI", "OF",
    ];
    private static readonly string[] _depthEntries = ["BI", "OF"];
    private static readonly string[] _tickEntries = ["LA"];

    private readonly CachedSynchronizedDictionary<long, MarketSubscription>
        _marketSubscriptions = [];
    private readonly SynchronizedDictionary<string, PrimarySecurityKey>
        _instruments = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, SecurityId>
        _securityIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<string> _seenMarketTrades =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var requestedTypes = lookupMsg.GetSecurityTypes();
        var code = lookupMsg.SecurityId.SecurityCode?.Trim();
        var board = lookupMsg.SecurityId.BoardCode?.Trim();
        var name = lookupMsg.Name?.Trim();
        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = Math.Min(
            lookupMsg.Count ?? LookupLimit,
            LookupLimit);

        if (left > 0)
        {
            foreach (var instrument in
                await _rest.GetInstruments(true, cancellationToken))
            {
                if (instrument?.InstrumentId?.Symbol.IsEmpty() != false)
                    continue;

                var security = instrument.ToSecurityMessage(
                    lookupMsg.TransactionId);
                if (!code.IsEmpty() &&
                    !security.SecurityId.SecurityCode.Contains(
                        code,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!board.IsEmpty() &&
                    !security.SecurityId.BoardCode.EqualsIgnoreCase(board))
                {
                    continue;
                }
                if (!name.IsEmpty() &&
                    !(security.Name?.Contains(
                        name,
                        StringComparison.OrdinalIgnoreCase) == true))
                {
                    continue;
                }
                if (!security.IsMatch(lookupMsg, requestedTypes))
                    continue;
                if (skip-- > 0)
                    continue;

                var native = instrument.ToNative();
                RememberInstrument(native, security.SecurityId);
                await SendOutMessageAsync(
                    security, cancellationToken);
                if (--left <= 0)
                    break;
            }
        }

        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg,
            DataType.Level1,
            _level1Entries,
            cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg,
            DataType.Ticks,
            _tickEntries,
            cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessMarketSubscription(
            mdMsg,
            DataType.MarketDepth,
            _depthEntries,
            cancellationToken);

    private async ValueTask ProcessMarketSubscription(
        MarketDataMessage mdMsg,
        DataType dataType,
        string[] entries,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);

        if (!mdMsg.IsSubscribe)
        {
            if (_marketSubscriptions.Remove(
                mdMsg.OriginalTransactionId, out var previous))
            {
                await _socket.UnsubscribeMarket(
                    previous.Native,
                    previous.Entries,
                    previous.Depth);
            }
            return;
        }

        var native = await ResolveNative(
            mdMsg.SecurityId, cancellationToken);
        RememberInstrument(native, mdMsg.SecurityId);
        var depth = Math.Max(1, mdMsg.MaxDepth ?? 1);

        await SendMarketSnapshot(
            mdMsg,
            dataType,
            native,
            entries,
            depth,
            cancellationToken);

        if (!mdMsg.IsHistoryOnly())
        {
            var subscription = new MarketSubscription
            {
                TransactionId = mdMsg.TransactionId,
                SecurityId = mdMsg.SecurityId,
                Native = native,
                DataType = dataType,
                Entries = entries,
                Depth = depth,
                MaxDepth = mdMsg.MaxDepth,
            };
            _marketSubscriptions[mdMsg.TransactionId] = subscription;
            try
            {
                await _socket.SubscribeMarket(
                    native, entries, depth, cancellationToken);
            }
            catch
            {
                _marketSubscriptions.Remove(mdMsg.TransactionId);
                throw;
            }
        }

        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                mdMsg.TransactionId, cancellationToken);
        }
    }

    private async ValueTask SendMarketSnapshot(
        MarketDataMessage mdMsg,
        DataType dataType,
        PrimarySecurityKey native,
        string[] entries,
        int depth,
        CancellationToken cancellationToken)
    {
        if (dataType == DataType.Ticks &&
            (mdMsg.IsHistoryOnly() || mdMsg.From is not null ||
                mdMsg.To is not null || mdMsg.Count is not null))
        {
            var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
            var from = (mdMsg.From ?? to.Date).ToUniversalTime();
            if (from > to)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mdMsg.From),
                    mdMsg.From,
                    "Primary history start time cannot be after the end time.");
            }

            var trades = (await _rest.GetTrades(
                native,
                from,
                to,
                IsDemo,
                cancellationToken))
                .Where(trade =>
                {
                    var time = trade.ServerTime.ToUtc(
                        trade.DateTime.ToUtc(CurrentTime));
                    return time >= from && time <= to;
                })
                .OrderBy(trade => trade.ServerTime)
                .ToArray();
            if (mdMsg.Count is > 0)
            {
                trades =
                [
                    .. trades.TakeLast(
                        (int)Math.Min(
                            mdMsg.Count.Value, int.MaxValue)),
                ];
            }
            foreach (var trade in trades)
            {
                await SendTick(
                    trade,
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    cancellationToken);
            }
            return;
        }

        var snapshot = await _rest.GetMarketData(
            native, entries, depth, cancellationToken);
        if (dataType == DataType.Level1)
        {
            await SendLevel1(
                snapshot,
                mdMsg.TransactionId,
                mdMsg.SecurityId,
                cancellationToken);
        }
        else if (dataType == DataType.MarketDepth)
        {
            await SendDepth(
                snapshot,
                mdMsg.TransactionId,
                mdMsg.SecurityId,
                mdMsg.MaxDepth,
                cancellationToken);
        }
        else
        {
            await SendLiveTick(
                snapshot,
                mdMsg.TransactionId,
                mdMsg.SecurityId,
                cancellationToken);
        }
    }

    private async ValueTask ProcessMarketUpdate(
        PrimaryMarketUpdate update,
        CancellationToken cancellationToken)
    {
        if (update?.InstrumentId?.Symbol.IsEmpty() != false ||
            update.MarketData is null)
        {
            return;
        }

        var subscriptions = _marketSubscriptions.CachedValues
            .Where(subscription =>
                subscription.Native.Symbol.EqualsIgnoreCase(
                    update.InstrumentId.Symbol) &&
                subscription.Native.Market.EqualsIgnoreCase(
                    update.InstrumentId.MarketId.IsEmpty(
                        subscription.Native.Market)))
            .ToArray();

        foreach (var subscription in subscriptions)
        {
            if (subscription.DataType == DataType.Level1)
            {
                await SendLevel1(
                    update,
                    subscription.TransactionId,
                    subscription.SecurityId,
                    cancellationToken);
            }
            else if (subscription.DataType == DataType.MarketDepth)
            {
                await SendDepth(
                    update,
                    subscription.TransactionId,
                    subscription.SecurityId,
                    subscription.MaxDepth,
                    cancellationToken);
            }
            else
            {
                await SendLiveTick(
                    update,
                    subscription.TransactionId,
                    subscription.SecurityId,
                    cancellationToken);
            }
        }
    }

    private ValueTask SendLevel1(
        PrimaryMarketUpdate update,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var data = update?.MarketData;
        if (data is null)
            return default;

        var bids = GetLevels(data, "BI")
            .Where(level => level.Price > 0 && level.Size >= 0)
            .OrderByDescending(level => level.Price)
            .ToArray();
        var offers = GetLevels(data, "OF")
            .Where(level => level.Price > 0 && level.Size >= 0)
            .OrderBy(level => level.Price)
            .ToArray();
        var last = GetEntry(data, "LA");
        var time = GetTime(update, last);

        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = time,
        }
        .TryAdd(Level1Fields.LastTradePrice, last?.Price.Positive())
        .TryAdd(Level1Fields.LastTradeVolume, last?.Size.Positive())
        .TryAdd(
            Level1Fields.LastTradeTime,
            last?.Price > 0 ? time : null)
        .TryAdd(Level1Fields.OpenPrice, GetNumber(data, "OP"))
        .TryAdd(Level1Fields.ClosePrice, GetPrice(data, "CL"))
        .TryAdd(
            Level1Fields.SettlementPrice, GetPrice(data, "SE"))
        .TryAdd(Level1Fields.HighPrice, GetNumber(data, "HI"))
        .TryAdd(Level1Fields.LowPrice, GetNumber(data, "LO"))
        .TryAdd(Level1Fields.Volume, GetNumber(data, "TV"))
        .TryAdd(Level1Fields.OpenInterest, GetSize(data, "OI"))
        .TryAdd(Level1Fields.Index, GetNumber(data, "IV"))
        .TryAdd(Level1Fields.BestBidPrice, bids.FirstOrDefault()?.Price)
        .TryAdd(Level1Fields.BestBidVolume, bids.FirstOrDefault()?.Size)
        .TryAdd(
            Level1Fields.BestAskPrice, offers.FirstOrDefault()?.Price)
        .TryAdd(
            Level1Fields.BestAskVolume, offers.FirstOrDefault()?.Size);

        return message.Changes.Count == 0
            ? default
            : SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendDepth(
        PrimaryMarketUpdate update,
        long transactionId,
        SecurityId securityId,
        int? maxDepth,
        CancellationToken cancellationToken)
    {
        var data = update?.MarketData;
        if (data is null)
            return default;

        return SendOutMessageAsync(
            new QuoteChangeMessage
            {
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                ServerTime = GetTime(update),
                Bids =
                [
                    .. GetLevels(data, "BI")
                        .Where(level =>
                            level.Price > 0 && level.Size >= 0)
                        .OrderByDescending(level => level.Price)
                        .Take(maxDepth ?? int.MaxValue)
                        .Select(level =>
                            new QuoteChange(level.Price, level.Size)),
                ],
                Asks =
                [
                    .. GetLevels(data, "OF")
                        .Where(level =>
                            level.Price > 0 && level.Size >= 0)
                        .OrderBy(level => level.Price)
                        .Take(maxDepth ?? int.MaxValue)
                        .Select(level =>
                            new QuoteChange(level.Price, level.Size)),
                ],
            },
            cancellationToken);
    }

    private ValueTask SendLiveTick(
        PrimaryMarketUpdate update,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var last = GetEntry(update?.MarketData, "LA");
        if (last?.Price is not > 0 || last.Size <= 0)
            return default;
        var time = GetTime(update, last);
        return SendTick(
            last.Price,
            last.Size,
            time,
            transactionId,
            securityId,
            cancellationToken);
    }

    private ValueTask SendTick(
        PrimaryTrade trade,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        if (trade is null || trade.Price <= 0 || trade.Size <= 0)
            return default;
        return SendTick(
            trade.Price,
            trade.Size,
            trade.ServerTime.ToUtc(
                trade.DateTime.ToUtc(CurrentTime)),
            transactionId,
            securityId,
            cancellationToken);
    }

    private ValueTask SendTick(
        decimal price,
        decimal volume,
        DateTime time,
        long transactionId,
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var tradeId =
            $"{securityId.SecurityCode}:{time:O}:{price}:{volume}";
        if (!_seenMarketTrades.TryAdd(
            $"{transactionId}:{tradeId}"))
        {
            return default;
        }

        return SendOutMessageAsync(
            new ExecutionMessage
            {
                DataTypeEx = DataType.Ticks,
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                TradeStringId = tradeId,
                TradePrice = price,
                TradeVolume = volume,
                ServerTime = time,
            },
            cancellationToken);
    }

    private async Task<PrimarySecurityKey> ResolveNative(
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        if (securityId.Native is string native && !native.IsEmpty())
        {
            return securityId.ToPrimaryNative(DefaultMarket);
        }

        var lookupKey = GetLookupKey(
            securityId.SecurityCode, securityId.BoardCode);
        if (_instruments.TryGetValue(lookupKey, out var cached))
            return cached;

        var requested = securityId.ToPrimaryNative(DefaultMarket);
        var instrument = await _rest.GetInstrument(
            requested, cancellationToken);
        var result = instrument?.InstrumentId?.Symbol.IsEmpty() == false
            ? instrument.ToNative()
            : requested;
        RememberInstrument(result, result.ToSecurityId());
        return result;
    }

    private SecurityId ResolveSecurityId(
        string market,
        string symbol)
    {
        var nativeKey =
            $"{market?.Trim().ToUpperInvariant()}|" +
            symbol?.Trim().ToUpperInvariant();
        if (_securityIds.TryGetValue(nativeKey, out var securityId))
            return securityId;

        var native = new PrimarySecurityKey(
            market.IsEmpty(DefaultMarket).IsEmpty("ROFX"),
            symbol?.StartsWith(
                "MERV -", StringComparison.OrdinalIgnoreCase) == true
                    ? "MERV"
                    : null,
            null,
            symbol.ThrowIfEmpty(nameof(symbol)));
        securityId = native.ToSecurityId();
        RememberInstrument(native, securityId);
        return securityId;
    }

    private void RememberInstrument(
        PrimarySecurityKey native,
        SecurityId securityId)
    {
        _instruments[native.LookupKey] = native;
        _instruments[GetLookupKey(
            native.Symbol, native.BoardCode)] = native;
        _instruments[GetLookupKey(native.Symbol, null)] = native;

        _securityIds[native.LookupKey] = securityId;
        _securityIds[GetLookupKey(
            native.Symbol, native.BoardCode)] = securityId;
        _securityIds[GetLookupKey(native.Symbol, null)] = securityId;
    }

    private static string GetLookupKey(string symbol, string board)
        => $"{symbol?.Trim().ToUpperInvariant()}|" +
            board?.Trim().ToUpperInvariant();

    private static PrimaryPriceSize[] GetLevels(
        JObject data,
        string entry)
        => data?[entry] is JArray array
            ? array.ToObject<PrimaryPriceSize[]>() ?? []
            : [];

    private static PrimaryPriceSize GetEntry(
        JObject data,
        string entry)
    {
        if (data?[entry] is not JToken token ||
            token.Type is JTokenType.Null or JTokenType.Undefined)
        {
            return null;
        }
        if (token is JObject value)
            return value.ToObject<PrimaryPriceSize>();
        var price = ToDecimal(token);
        return price is null
            ? null
            : new PrimaryPriceSize { Price = price.Value };
    }

    private static decimal? GetPrice(JObject data, string entry)
        => GetEntry(data, entry)?.Price.Positive();

    private static decimal? GetSize(JObject data, string entry)
        => GetEntry(data, entry)?.Size.Positive();

    private static decimal? GetNumber(JObject data, string entry)
    {
        if (data?[entry] is not JToken token ||
            token.Type is JTokenType.Null or JTokenType.Undefined)
        {
            return null;
        }
        if (token is JObject value)
        {
            return ToDecimal(value["price"]) ??
                ToDecimal(value["size"]);
        }
        return ToDecimal(token);
    }

    private static decimal? ToDecimal(JToken token)
    {
        if (token is null)
            return null;
        if (token.Type is JTokenType.Integer or JTokenType.Float)
            return token.Value<decimal>();
        return decimal.TryParse(
            token.Value<string>(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
                ? value
                : null;
    }

    private DateTime GetTime(
        PrimaryMarketUpdate update,
        PrimaryPriceSize entry = null)
        => (entry?.Date ?? 0) > 0
            ? entry.Date.ToUtc(CurrentTime)
            : update?.Timestamp > 0
                ? update.Timestamp.ToUtc(CurrentTime)
                : CurrentTime;
}
