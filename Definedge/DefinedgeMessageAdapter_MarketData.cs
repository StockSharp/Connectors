namespace StockSharp.Definedge;

public partial class DefinedgeMessageAdapter
{
    private readonly SynchronizedDictionary<
        string,
        SynchronizedDictionary<DataType, long>>
        _marketSubscriptions =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, SecurityId>
        _securityIds =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string,
        DefinedgeInstrument>
        _instruments =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, JObject>
        _marketStates =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string,
        (DateTime time, decimal price, decimal volume)>
        _lastTicks =
            new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var securityTypes = lookupMsg.GetSecurityTypes();
        var left = lookupMsg.Count ?? long.MaxValue;

        foreach (var instrument in
            await _restClient.GetInstruments(cancellationToken))
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

            var security = new SecurityMessage
            {
                OriginalTransactionId =
                    lookupMsg.TransactionId,
                SecurityId = securityId,
                SecurityType = instrument.ToSecurityType(),
                Name = instrument.Symbol,
                ShortName = instrument.TradingSymbol,
                Class = instrument.InstrumentType,
                Currency = CurrencyTypes.INR,
                PriceStep = Positive(instrument.TickSize),
                VolumeStep = Positive(instrument.LotSize),
                Multiplier = Positive(instrument.Multiplier),
                ExpiryDate = instrument.Expiry,
                Strike = Positive(instrument.StrikePrice),
                OptionType =
                    instrument.OptionType.ToOptionType(),
            };
            if (security.SecurityType is
                SecurityTypes.Future or SecurityTypes.Option &&
                !instrument.Symbol.IsEmpty())
            {
                security.UnderlyingSecurityId = new()
                {
                    SecurityCode = instrument.Symbol,
                };
            }
            if (!security.IsMatch(lookupMsg, securityTypes))
                continue;

            var instrumentKey =
                instrument.Exchange.ToInstrumentKey(
                    instrument.Token);
            _securityIds[instrumentKey] = securityId;
            _instruments[instrumentKey] = instrument;
            await SendOutMessageAsync(
                security, cancellationToken);
            if (--left <= 0)
                break;
        }

        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessNormalSubscription(
            mdMsg, DataType.Level1, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
        => ProcessNormalSubscription(
            mdMsg, DataType.Ticks, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var depth = mdMsg.MaxDepth ?? 5;
        if (depth is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.MaxDepth),
                depth,
                "Definedge provides five market-depth levels.");
        }
        return ProcessNormalSubscription(
            mdMsg, DataType.MarketDepth, cancellationToken);
    }

    private async ValueTask ProcessNormalSubscription(
        MarketDataMessage mdMsg,
        DataType dataType,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);

        var instrumentKey = mdMsg.SecurityId.ToInstrumentKey();
        if (!mdMsg.IsSubscribe)
        {
            if (!_marketSubscriptions.TryGetValue(
                instrumentKey, out var existing))
            {
                return;
            }

            var hadDepth =
                existing.ContainsKey(DataType.MarketDepth);
            if (existing.TryGetValue(
                dataType, out var subscriptionId) &&
                subscriptionId ==
                    mdMsg.OriginalTransactionId)
            {
                existing.Remove(dataType);
            }
            if (existing.Count == 0)
            {
                _marketSubscriptions.Remove(instrumentKey);
                _securityIds.Remove(instrumentKey);
                _marketStates.Remove(instrumentKey);
                _lastTicks.Remove(instrumentKey);
                if (_socketClient != null)
                {
                    await _socketClient.Unsubscribe(
                        instrumentKey, cancellationToken);
                }
                return;
            }

            var hasDepth =
                existing.ContainsKey(DataType.MarketDepth);
            if (hadDepth != hasDepth && _socketClient != null)
            {
                await _socketClient.Subscribe(
                    instrumentKey,
                    hasDepth,
                    cancellationToken);
            }
            return;
        }

        var instrument = await GetInstrument(
            instrumentKey, cancellationToken);
        var hasHistoryRequest =
            mdMsg.IsHistoryOnly() ||
            mdMsg.From != null ||
            mdMsg.To != null;
        if (hasHistoryRequest)
        {
            if (dataType == DataType.Ticks)
            {
                await SendHistoricalTicks(
                    mdMsg, instrument, cancellationToken);
            }
            else
            {
                await SendQuoteSnapshot(
                    mdMsg, dataType, instrument,
                    cancellationToken);
            }

            if (mdMsg.IsHistoryOnly())
            {
                await SendSubscriptionFinishedAsync(
                    mdMsg.TransactionId, cancellationToken);
                return;
            }
        }

        var socket = SocketClient;
        var subscriptions =
            _marketSubscriptions.SafeAdd(instrumentKey);
        var wasEmpty = subscriptions.Count == 0;
        var wasDepth =
            subscriptions.ContainsKey(DataType.MarketDepth);
        subscriptions[dataType] = mdMsg.TransactionId;
        var isDepth =
            subscriptions.ContainsKey(DataType.MarketDepth);
        _securityIds[instrumentKey] = mdMsg.SecurityId;
        _instruments[instrumentKey] = instrument;
        if (wasEmpty || wasDepth != isDepth)
        {
            await socket.Subscribe(
                instrumentKey, isDepth, cancellationToken);
        }
        await SendSubscriptionResultAsync(
            mdMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask
        OnTFCandlesSubscriptionAsync(
            MarketDataMessage mdMsg,
            CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);

        if (!mdMsg.IsSubscribe)
            return;
        if (!mdMsg.IsHistoryOnly())
        {
            throw new NotSupportedException(
                "Definedge provides historical candles only; realtime candle subscriptions are not available.");
        }

        var instrument = await GetInstrument(
            mdMsg.SecurityId.ToInstrumentKey(),
            cancellationToken);
        var timeFrame = mdMsg.GetTimeFrame();
        var interval = timeFrame == TimeSpan.FromDays(1)
            ? "day"
            : timeFrame == TimeSpan.FromMinutes(1)
                ? "minute"
                : throw new ArgumentOutOfRangeException(
                    nameof(timeFrame),
                    timeFrame,
                    "Definedge supports one-minute and daily candles.");
        var rows = await _restClient.GetHistory(
            instrument,
            interval,
            mdMsg.From,
            mdMsg.To,
            cancellationToken);
        IEnumerable<DefinedgeHistoryRow> ordered =
            rows.OrderBy(row => row.Time);
        if (mdMsg.Count is long count)
        {
            ordered = ordered
                .TakeLast(
                    (int)Math.Min(count, int.MaxValue))
                .OrderBy(row => row.Time);
        }

        foreach (var row in ordered)
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId =
                        mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId,
                    TypedArg = timeFrame,
                    OpenTime = row.Time,
                    OpenPrice = row.Open,
                    HighPrice = row.High,
                    LowPrice = row.Low,
                    ClosePrice = row.Close,
                    TotalVolume = row.Volume,
                    OpenInterest = row.OpenInterest,
                    State = CandleStates.Finished,
                },
                cancellationToken);
        }

        await SendSubscriptionFinishedAsync(
            mdMsg.TransactionId, cancellationToken);
    }

    private async ValueTask SendHistoricalTicks(
        MarketDataMessage mdMsg,
        DefinedgeInstrument instrument,
        CancellationToken cancellationToken)
    {
        var rows = await _restClient.GetHistory(
            instrument,
            "tick",
            mdMsg.From,
            mdMsg.To,
            cancellationToken);
        IEnumerable<DefinedgeHistoryRow> ordered =
            rows.Where(row => row.LastPrice is > 0)
                .OrderBy(row => row.Time);
        if (mdMsg.Count is long count)
        {
            ordered = ordered
                .TakeLast(
                    (int)Math.Min(count, int.MaxValue))
                .OrderBy(row => row.Time);
        }

        var sequence = 0L;

        foreach (var row in ordered)
        {
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Ticks,
                OriginalTransactionId =
                    mdMsg.TransactionId,
                SecurityId = mdMsg.SecurityId,
                TradeStringId =
                    $"{instrument.Exchange}|{instrument.Token}:{row.Time.Ticks}:{sequence++}",
                TradePrice = row.LastPrice,
                TradeVolume = Positive(row.LastVolume),
                OpenInterest = row.OpenInterest,
                ServerTime = row.Time,
            }, cancellationToken);
        }
    }

    private async ValueTask SendQuoteSnapshot(
        MarketDataMessage mdMsg,
        DataType dataType,
        DefinedgeInstrument instrument,
        CancellationToken cancellationToken)
    {
        var quote = await _restClient.GetQuote(
            instrument, cancellationToken);
        var update = CreateQuoteUpdate(
            quote, instrument);
        var subscriptions =
            new SynchronizedDictionary<DataType, long>
            {
                [dataType] = mdMsg.TransactionId,
            };
        await SendMarketMessages(
            instrument.Exchange.ToInstrumentKey(
                instrument.Token),
            mdMsg.SecurityId,
            subscriptions,
            update,
            update.GetMarketTime(),
            true,
            cancellationToken);
    }

    private async Task<DefinedgeInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        if (_instruments.TryGetValue(
            instrumentKey, out var instrument))
        {
            return instrument;
        }
        instrument = await _restClient.GetInstrument(
            instrumentKey, cancellationToken) ??
            throw new InvalidOperationException(
                $"Definedge instrument '{instrumentKey}' was not found in the official security master.");
        _instruments[instrumentKey] = instrument;
        return instrument;
    }

    private async ValueTask OnMarketDataReceived(
        JObject update,
        CancellationToken cancellationToken)
    {
        var exchange = update.GetText("e");
        var token = update.GetText("tk");
        var instrumentKey =
            exchange.ToInstrumentKey(token);
        if (!_marketSubscriptions.TryGetValue(
            instrumentKey, out var subscriptions))
        {
            return;
        }

        if (!_marketStates.TryGetValue(
            instrumentKey, out var state))
        {
            state = new();
            _marketStates[instrumentKey] = state;
        }
        var isLastTradeUpdate =
            update.GetValue(
                "lp",
                StringComparison.OrdinalIgnoreCase) != null;
        state.Apply(update);
        var securityId =
            _securityIds.TryGetValue2(instrumentKey) ??
            exchange.ToSecurityId(
                token, state.GetText("ts"));
        await SendMarketMessages(
            instrumentKey,
            securityId,
            subscriptions,
            state,
            update.GetMarketTime(),
            isLastTradeUpdate,
            cancellationToken);
    }

    private async ValueTask SendMarketMessages(
        string instrumentKey,
        SecurityId securityId,
        SynchronizedDictionary<DataType, long> subscriptions,
        JObject state,
        DateTime serverTime,
        bool isLastTradeUpdate,
        CancellationToken cancellationToken)
    {
        var bids = state.GetBids()
            .OrderByDescending(level => level.Price)
            .ToArray();
        var asks = state.GetAsks()
            .OrderBy(level => level.Price)
            .ToArray();

        if (subscriptions.TryGetValue(
            DataType.Level1, out var level1Id))
        {
            var lastPrice =
                Positive(state.GetDecimal("lp"));
            var level1 = new Level1ChangeMessage
            {
                OriginalTransactionId = level1Id,
                SecurityId = securityId,
                ServerTime = serverTime,
            }
            .TryAdd(
                Level1Fields.LastTradePrice, lastPrice)
            .TryAdd(
                Level1Fields.LastTradeVolume,
                Positive(state.GetDecimal("ltq")))
            .TryAdd(
                Level1Fields.LastTradeTime,
                lastPrice != null ? serverTime : null)
            .TryAdd(
                Level1Fields.Volume,
                Positive(state.GetDecimal("v")))
            .TryAdd(
                Level1Fields.AveragePrice,
                Positive(state.GetDecimal("ap")))
            .TryAdd(
                Level1Fields.OpenPrice,
                Positive(state.GetDecimal("o")))
            .TryAdd(
                Level1Fields.HighPrice,
                Positive(state.GetDecimal("h")))
            .TryAdd(
                Level1Fields.LowPrice,
                Positive(state.GetDecimal("l")))
            .TryAdd(
                Level1Fields.ClosePrice,
                Positive(state.GetDecimal("c")))
            .TryAdd(
                Level1Fields.OpenInterest,
                Positive(state.GetDecimal("oi")))
            .TryAdd(
                Level1Fields.BidsVolume,
                Positive(state.GetDecimal("tbq")))
            .TryAdd(
                Level1Fields.AsksVolume,
                Positive(state.GetDecimal("tsq")))
            .TryAdd(
                Level1Fields.MinPrice,
                Positive(state.GetDecimal("lc")))
            .TryAdd(
                Level1Fields.MaxPrice,
                Positive(state.GetDecimal("uc")))
            .TryAdd(
                Level1Fields.HighPrice52Week,
                Positive(state.GetDecimal("52h")))
            .TryAdd(
                Level1Fields.LowPrice52Week,
                Positive(state.GetDecimal("52l")))
            .TryAdd(
                Level1Fields.BestBidPrice,
                bids.FirstOrDefault()?.Price)
            .TryAdd(
                Level1Fields.BestBidVolume,
                bids.FirstOrDefault()?.Volume)
            .TryAdd(
                Level1Fields.BestAskPrice,
                asks.FirstOrDefault()?.Price)
            .TryAdd(
                Level1Fields.BestAskVolume,
                asks.FirstOrDefault()?.Volume);
            if (level1.Changes.Count > 0)
            {
                await SendOutMessageAsync(
                    level1, cancellationToken);
            }
        }

        if (subscriptions.TryGetValue(
            DataType.MarketDepth, out var depthId))
        {
            await SendOutMessageAsync(new QuoteChangeMessage
            {
                OriginalTransactionId = depthId,
                SecurityId = securityId,
                ServerTime = serverTime,
                Bids =
                [
                    .. bids.Select(level =>
                        new QuoteChange(
                            level.Price,
                            level.Volume)
                        {
                            OrdersCount =
                                level.OrdersCount,
                        }),
                ],
                Asks =
                [
                    .. asks.Select(level =>
                        new QuoteChange(
                            level.Price,
                            level.Volume)
                        {
                            OrdersCount =
                                level.OrdersCount,
                        }),
                ],
            }, cancellationToken);
        }

        var tradePrice =
            state.GetDecimal("lp") ?? 0;
        var tradeVolume =
            state.GetDecimal("ltq") ?? 0;
        if (isLastTradeUpdate &&
            tradePrice > 0 &&
            subscriptions.TryGetValue(
                DataType.Ticks, out var ticksId))
        {
            var trade =
                (serverTime, tradePrice, tradeVolume);
            if (!_lastTicks.TryGetValue(
                instrumentKey, out var previous) ||
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
                            $"{instrumentKey}:{serverTime.Ticks}:{tradePrice.ToString(CultureInfo.InvariantCulture)}",
                        TradePrice = tradePrice,
                        TradeVolume =
                            Positive(tradeVolume),
                        OpenInterest =
                            Positive(
                                state.GetDecimal("oi")),
                        ServerTime = serverTime,
                    },
                    cancellationToken);
            }
        }
    }

    internal static JObject CreateQuoteUpdate(
        JObject quote,
        DefinedgeInstrument instrument)
    {
        var update = new JObject
        {
            ["t"] = "tk",
            ["e"] = quote.GetText("exchange")
                .IsEmpty(instrument.Exchange),
            ["tk"] = quote.GetText("token")
                .IsEmpty(instrument.Token),
            ["ts"] = quote.GetText("tradingsymbol")
                .IsEmpty(instrument.TradingSymbol),
        };
        CopyQuote(update, quote, "lp", "ltp");
        CopyQuote(
            update, quote, "ltq", "last_traded_qty");
        CopyQuote(
            update, quote, "ltt", "last_trade_time");
        CopyQuote(update, quote, "v", "volume");
        CopyQuote(update, quote, "o", "day_open");
        CopyQuote(update, quote, "h", "day_high");
        CopyQuote(update, quote, "l", "day_low");
        CopyQuote(
            update, quote, "ap",
            "average_traded_price");
        CopyQuote(
            update, quote, "lc", "lower_circuit");
        CopyQuote(
            update, quote, "uc", "upper_circuit");
        CopyQuote(update, quote, "ft", "feed_time");

        for (var index = 1; index <= 5; index++)
        {
            CopyQuote(
                update,
                quote,
                $"bp{index}",
                $"best_bid_price{index}");
            CopyQuote(
                update,
                quote,
                $"bq{index}",
                $"best_bid_qty{index}");
            CopyQuote(
                update,
                quote,
                $"bo{index}",
                $"best_bid_orders{index}");
            CopyQuote(
                update,
                quote,
                $"sp{index}",
                $"best_ask_price{index}");
            CopyQuote(
                update,
                quote,
                $"sq{index}",
                $"best_ask_qty{index}");
            CopyQuote(
                update,
                quote,
                $"so{index}",
                $"best_ask_orders{index}");
        }

        return update;
    }

    private static void CopyQuote(
        JObject destination,
        JObject source,
        string destinationName,
        string sourceName)
    {
        if (source.GetValue(
            sourceName,
            StringComparison.OrdinalIgnoreCase) is { } value &&
            value.Type != JTokenType.Null)
        {
            destination[destinationName] =
                value.DeepClone();
        }
    }

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;
}
