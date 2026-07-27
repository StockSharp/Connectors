namespace StockSharp.Nuvama;

public partial class NuvamaMessageAdapter
{
    private readonly SynchronizedDictionary<
        string,
        SynchronizedDictionary<DataType, long>> _marketSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, SecurityId> _securityIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, NuvamaInstrument>
        _instruments = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, string> _instrumentKeys =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string,
        (DateTime time, decimal price, decimal volume)> _lastTicks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId,
            cancellationToken);
        var securityTypes = lookupMsg.GetSecurityTypes();
        var left = lookupMsg.Count ?? long.MaxValue;

        foreach (var instrument in await _restClient.GetInstruments(
            cancellationToken))
        {
            SecurityId securityId;
            try
            {
                securityId = instrument.ToSecurityId();
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            var lotSize = Positive(instrument.LotSize);
            var multiplier = Positive(instrument.Multiplier) ?? lotSize;
            var security = new SecurityMessage
            {
                OriginalTransactionId = lookupMsg.TransactionId,
                SecurityId = securityId,
                SecurityType = instrument.ToSecurityType(),
                Name = instrument.Description
                    .IsEmpty(instrument.SymbolName)
                    .IsEmpty(instrument.TradingSymbol),
                ShortName = instrument.SymbolName
                    .IsEmpty(instrument.TradingSymbol),
                Class = instrument.AssetType.IsEmpty(instrument.Series),
                Currency = CurrencyTypes.INR,
                PriceStep = Positive(instrument.TickSize),
                VolumeStep = lotSize,
                Multiplier = multiplier,
                ExpiryDate = instrument.Expiry.ToExpiry(),
                Strike = Positive(instrument.StrikePrice),
                OptionType = instrument.OptionType.ToOptionType(),
            };
            if (security.SecurityType is SecurityTypes.Future or
                SecurityTypes.Option &&
                !instrument.SymbolName.IsEmpty())
            {
                security.UnderlyingSecurityId = new()
                {
                    SecurityCode = instrument.SymbolName,
                };
            }
            if (!security.IsMatch(lookupMsg, securityTypes))
                continue;

            var instrumentKey = instrument.Exchange.ToInstrumentKey(
                instrument.ExchangeToken);
            _securityIds[instrumentKey] = securityId;
            _instruments[instrumentKey] = instrument;
            _instrumentKeys[instrument.ExchangeToken] = instrumentKey;
            await SendOutMessageAsync(security, cancellationToken);
            if (--left <= 0)
                break;
        }

        await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessRealtimeSubscription(
            mdMsg,
            DataType.Level1,
            cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessRealtimeSubscription(
            mdMsg,
            DataType.Ticks,
            cancellationToken);

    /// <inheritdoc />
    protected override async ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var depth = mdMsg.MaxDepth ?? 5;
        if (depth is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.MaxDepth),
                depth,
                "Nuvama provides five market-depth levels.");
        }

        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId,
            cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            await RemoveRealtimeSubscription(
                mdMsg,
                DataType.MarketDepth,
                cancellationToken);
            return;
        }

        var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();
        var instrument = await GetInstrument(
            instrumentKey,
            cancellationToken);
        RememberInstrument(instrumentKey, mdMsg.SecurityId, instrument);
        var snapshot = await _restClient.GetMarketDepth(
            instrument.ExchangeToken,
            cancellationToken);
        if (snapshot != null)
        {
            await SendDepth(
                snapshot,
                instrumentKey,
                mdMsg.TransactionId,
                cancellationToken);
        }
        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                mdMsg.TransactionId,
                cancellationToken);
            return;
        }

        var subscriptions = _marketSubscriptions.SafeAdd(instrumentKey);
        var firstDepth = !subscriptions.ContainsKey(DataType.MarketDepth);
        subscriptions[DataType.MarketDepth] = mdMsg.TransactionId;
        if (firstDepth)
        {
            await _streamClient.SubscribeDepth(
                instrument.ExchangeToken,
                cancellationToken);
        }
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId,
            cancellationToken);
        if (!mdMsg.IsSubscribe)
            return;
        if (!mdMsg.IsHistoryOnly())
        {
            throw new NotSupportedException(
                "Nuvama provides historical candles only; realtime candle subscriptions are not available.");
        }

        var instrument = await GetInstrument(
            mdMsg.SecurityId.ToInstrumentKey(),
            cancellationToken);
        var timeFrame = mdMsg.GetTimeFrame();
        var candles = await _restClient.GetCandles(
            instrument,
            timeFrame,
            mdMsg.From,
            mdMsg.To,
            cancellationToken);
        IEnumerable<NuvamaCandle> selected = candles
            .Where(candle =>
                (mdMsg.From == null ||
                 candle.Time >= mdMsg.From.Value.ToUniversalTime()) &&
                (mdMsg.To == null ||
                 candle.Time <= mdMsg.To.Value.ToUniversalTime()))
            .OrderBy(candle => candle.Time);
        if (mdMsg.Count is > 0 and var count)
        {
            selected = selected
                .TakeLast((int)Math.Min(count, int.MaxValue))
                .OrderBy(candle => candle.Time);
        }

        foreach (var candle in selected)
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId,
                    TypedArg = timeFrame,
                    OpenTime = candle.Time,
                    OpenPrice = candle.Open,
                    HighPrice = candle.High,
                    LowPrice = candle.Low,
                    ClosePrice = candle.Close,
                    TotalVolume = candle.Volume,
                    State = CandleStates.Finished,
                },
                cancellationToken);
        }

        await SendSubscriptionFinishedAsync(
            mdMsg.TransactionId,
            cancellationToken);
    }

    private async ValueTask ProcessRealtimeSubscription(
        MarketDataMessage mdMsg,
        DataType dataType,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId,
            cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            await RemoveRealtimeSubscription(
                mdMsg,
                dataType,
                cancellationToken);
            return;
        }
        if (mdMsg.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                mdMsg.TransactionId,
                cancellationToken);
            return;
        }

        var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();
        var instrument = await GetInstrument(
            instrumentKey,
            cancellationToken);
        var subscriptions = _marketSubscriptions.SafeAdd(instrumentKey);
        var hadQuotes =
            subscriptions.ContainsKey(DataType.Level1) ||
            subscriptions.ContainsKey(DataType.Ticks);
        subscriptions[dataType] = mdMsg.TransactionId;
        RememberInstrument(instrumentKey, mdMsg.SecurityId, instrument);
        if (!hadQuotes)
        {
            await _streamClient.SubscribeQuotes(
                instrument.ExchangeToken,
                cancellationToken);
        }
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
    }

    private async ValueTask RemoveRealtimeSubscription(
        MarketDataMessage mdMsg,
        DataType dataType,
        CancellationToken cancellationToken)
    {
        var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();
        if (!_marketSubscriptions.TryGetValue(
            instrumentKey,
            out var subscriptions))
            return;

        var hadQuotes =
            subscriptions.ContainsKey(DataType.Level1) ||
            subscriptions.ContainsKey(DataType.Ticks);
        if (subscriptions.TryGetValue(dataType, out var subscriptionId) &&
            subscriptionId == mdMsg.OriginalTransactionId)
            subscriptions.Remove(dataType);
        var hasQuotes =
            subscriptions.ContainsKey(DataType.Level1) ||
            subscriptions.ContainsKey(DataType.Ticks);
        var instrument = await GetInstrument(
            instrumentKey,
            cancellationToken);
        if (hadQuotes && !hasQuotes)
        {
            await _streamClient.UnsubscribeQuotes(
                instrument.ExchangeToken,
                cancellationToken);
        }
        if (dataType == DataType.MarketDepth &&
            !subscriptions.ContainsKey(DataType.MarketDepth))
        {
            await _streamClient.UnsubscribeDepth(
                instrument.ExchangeToken,
                cancellationToken);
        }
        if (subscriptions.Count == 0)
        {
            _marketSubscriptions.Remove(instrumentKey);
            _securityIds.Remove(instrumentKey);
            _lastTicks.Remove(instrumentKey);
        }
    }

    private async Task<NuvamaInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        if (_instruments.TryGetValue(instrumentKey, out var instrument))
            return instrument;
        instrument = await _restClient.GetInstrument(
            instrumentKey,
            cancellationToken) ??
            throw new InvalidOperationException(
                $"Nuvama instrument '{instrumentKey}' was not found in the official contract master.");
        _instruments[instrumentKey] = instrument;
        _instrumentKeys[instrument.ExchangeToken] = instrumentKey;
        return instrument;
    }

    private void RememberInstrument(
        string instrumentKey,
        SecurityId securityId,
        NuvamaInstrument instrument)
    {
        _securityIds[instrumentKey] = securityId;
        _instruments[instrumentKey] = instrument;
        _instrumentKeys[instrument.ExchangeToken] = instrumentKey;
    }

    private async ValueTask OnQuoteReceived(
        NuvamaQuote quote,
        CancellationToken cancellationToken)
    {
        var instrumentKey = ResolveInstrumentKey(quote.Symbol);
        if (instrumentKey.IsEmpty() ||
            !_marketSubscriptions.TryGetValue(
                instrumentKey,
                out var subscriptions))
            return;

        var securityId = _securityIds.TryGetValue2(instrumentKey) ??
            instrumentKey.ParseInstrumentKey().exchange.ToSecurityId(
                quote.Symbol);
        var serverTime = quote.LastTradeTime.ToNuvamaTime() ??
            quote.LastUpdatedTime.ToNuvamaTime() ??
            CurrentTime;

        if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
        {
            var level1 = new Level1ChangeMessage
            {
                OriginalTransactionId = level1Id,
                SecurityId = securityId,
                ServerTime = serverTime,
            }
            .TryAdd(
                Level1Fields.LastTradePrice,
                Positive(quote.LastPrice))
            .TryAdd(
                Level1Fields.LastTradeVolume,
                Positive(quote.LastQuantity))
            .TryAdd(
                Level1Fields.LastTradeTime,
                Positive(quote.LastPrice) != null ? serverTime : null)
            .TryAdd(Level1Fields.Volume, Positive(quote.Volume))
            .TryAdd(
                Level1Fields.AveragePrice,
                Positive(quote.AveragePrice))
            .TryAdd(Level1Fields.OpenPrice, Positive(quote.Open))
            .TryAdd(Level1Fields.HighPrice, Positive(quote.High))
            .TryAdd(Level1Fields.LowPrice, Positive(quote.Low))
            .TryAdd(Level1Fields.ClosePrice, Positive(quote.Close))
            .TryAdd(
                Level1Fields.OpenInterest,
                Positive(quote.OpenInterest))
            .TryAdd(
                Level1Fields.BidsVolume,
                Positive(quote.TotalBuyQuantity))
            .TryAdd(
                Level1Fields.AsksVolume,
                Positive(quote.TotalSellQuantity))
            .TryAdd(
                Level1Fields.MinPrice,
                Positive(quote.LowerCircuit))
            .TryAdd(
                Level1Fields.MaxPrice,
                Positive(quote.UpperCircuit))
            .TryAdd(
                Level1Fields.HighPrice52Week,
                Positive(quote.YearHigh))
            .TryAdd(
                Level1Fields.LowPrice52Week,
                Positive(quote.YearLow))
            .TryAdd(
                Level1Fields.BestBidPrice,
                Positive(quote.BestBidPrice))
            .TryAdd(
                Level1Fields.BestBidVolume,
                Positive(quote.BestBidQuantity))
            .TryAdd(
                Level1Fields.BestAskPrice,
                Positive(quote.BestAskPrice))
            .TryAdd(
                Level1Fields.BestAskVolume,
                Positive(quote.BestAskQuantity));
            if (level1.Changes.Count > 0)
                await SendOutMessageAsync(level1, cancellationToken);
        }

        var lastPrice = quote.LastPrice.ToDecimal();
        var lastVolume = quote.LastQuantity.ToDecimal();
        if (lastPrice > 0 &&
            subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
        {
            var trade = (serverTime, lastPrice, lastVolume);
            if (!_lastTicks.TryGetValue(instrumentKey, out var previous) ||
                previous != trade)
            {
                _lastTicks[instrumentKey] = trade;
                await SendOutMessageAsync(
                    new ExecutionMessage
                    {
                        DataTypeEx = DataType.Ticks,
                        OriginalTransactionId = ticksId,
                        SecurityId = securityId,
                        TradeStringId =
                            $"{instrumentKey}:{serverTime.Ticks}:{lastPrice.ToString(CultureInfo.InvariantCulture)}:{lastVolume.ToString(CultureInfo.InvariantCulture)}",
                        TradePrice = lastPrice,
                        TradeVolume =
                            lastVolume > 0 ? lastVolume : null,
                        ServerTime = serverTime,
                    },
                    cancellationToken);
            }
        }
    }

    private async ValueTask OnDepthReceived(
        NuvamaDepth depth,
        CancellationToken cancellationToken)
    {
        var instrumentKey = ResolveInstrumentKey(depth.Symbol);
        if (instrumentKey.IsEmpty() ||
            !_marketSubscriptions.TryGetValue(
                instrumentKey,
                out var subscriptions) ||
            !subscriptions.TryGetValue(
                DataType.MarketDepth,
                out var transactionId))
            return;

        await SendDepth(
            depth,
            instrumentKey,
            transactionId,
            cancellationToken);
    }

    private async ValueTask SendDepth(
        NuvamaDepth depth,
        string instrumentKey,
        long transactionId,
        CancellationToken cancellationToken)
    {
        var bids = depth.BidValues ?? depth.Bids ?? [];
        var asks = depth.AskValues ?? depth.Asks ?? [];
        var securityId = _securityIds.TryGetValue2(instrumentKey) ??
            instrumentKey.ParseInstrumentKey().exchange.ToSecurityId(
                instrumentKey.ParseInstrumentKey().streamingSymbol);
        await SendOutMessageAsync(
            new QuoteChangeMessage
            {
                OriginalTransactionId = transactionId,
                SecurityId = securityId,
                ServerTime =
                    depth.LastTradeTime.ToNuvamaTime() ?? CurrentTime,
                Bids =
                [
                    ..
                        bids
                            .Where(level => level.Price.ToDecimal() > 0)
                            .OrderByDescending(
                                level => level.Price.ToDecimal())
                            .Select(level => new QuoteChange(
                                level.Price.ToDecimal(),
                                level.Quantity.ToDecimal())
                            {
                                OrdersCount = level.OrdersCount.ToInt(),
                            })
                ],
                Asks =
                [
                    ..
                        asks
                            .Where(level => level.Price.ToDecimal() > 0)
                            .OrderBy(level => level.Price.ToDecimal())
                            .Select(level => new QuoteChange(
                                level.Price.ToDecimal(),
                                level.Quantity.ToDecimal())
                            {
                                OrdersCount = level.OrdersCount.ToInt(),
                            })
                ],
            },
            cancellationToken);
    }

    private string ResolveInstrumentKey(string streamingSymbol)
    {
        if (streamingSymbol.IsEmpty())
            return null;

        return _instrumentKeys.TryGetValue(
            streamingSymbol,
            out var instrumentKey)
                ? instrumentKey
                : null;
    }

    private static decimal? Positive(string value)
        => NuvamaExtensions.Positive(value);
}
