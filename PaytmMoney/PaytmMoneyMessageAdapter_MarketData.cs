namespace StockSharp.PaytmMoney;

public partial class PaytmMoneyMessageAdapter
{
    private readonly SynchronizedDictionary<
        string, SynchronizedDictionary<DataType, long>>
        _marketSubscriptions =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, SecurityId> _securityIds =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, PaytmMoneyInstrument> _instruments =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, (DateTime time, decimal price, decimal volume)>
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

        foreach (var instrument in await _restClient
            .GetInstruments(cancellationToken))
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

            var message = new SecurityMessage
            {
                OriginalTransactionId =
                    lookupMsg.TransactionId,
                SecurityId = securityId,
                SecurityType = instrument.ToSecurityType(),
                Name = instrument.Name,
                ShortName = instrument.Symbol,
                PriceStep = instrument.TickSize is > 0
                    ? instrument.TickSize
                    : null,
                VolumeStep = instrument.LotSize is > 0
                    ? instrument.LotSize
                    : null,
                Multiplier = instrument.LotSize is > 0
                    ? instrument.LotSize
                    : null,
                ExpiryDate = instrument.ExpiryDate,
                Strike = instrument.StrikePrice is > 0
                    ? instrument.StrikePrice
                    : null,
                OptionType =
                    instrument.OptionType.ToOptionType(),
            };
            if (!instrument.UnderlyingSymbol.IsEmpty())
            {
                message.UnderlyingSecurityId = new()
                {
                    SecurityCode =
                        instrument.UnderlyingSymbol,
                };
            }
            if (!message.IsMatch(lookupMsg, securityTypes))
                continue;

            var key = instrument.ToInstrumentKey();
            _securityIds[key] = securityId;
            _instruments[key] = instrument;
            await SendOutMessageAsync(message, cancellationToken);
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
        if (depth > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.MaxDepth), depth,
                "Paytm Money provides five market-depth levels.");
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

        var instrumentKey =
            mdMsg.SecurityId.ToInstrumentKey();

        if (mdMsg.IsSubscribe)
        {
            var instrument = await ResolveInstrument(
                instrumentKey, cancellationToken);
            if (mdMsg.IsHistoryOnly())
            {
                await SendRestSnapshot(
                    mdMsg,
                    dataType,
                    instrument,
                    cancellationToken);
                await SendSubscriptionResultAsync(
                    mdMsg, cancellationToken);
                await SendSubscriptionFinishedAsync(
                    mdMsg.TransactionId, cancellationToken);
                return;
            }

            var subscriptions =
                _marketSubscriptions.SafeAdd(instrumentKey);
            subscriptions[dataType] = mdMsg.TransactionId;
            _securityIds[instrumentKey] = mdMsg.SecurityId;
            _instruments[instrumentKey] = instrument;
            await UpdateFeed(
                instrumentKey, subscriptions, cancellationToken);
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
        }
        else if (_marketSubscriptions.TryGetValue(
            instrumentKey, out var subscriptions))
        {
            subscriptions.Remove(dataType);
            if (subscriptions.Count == 0)
            {
                _marketSubscriptions.Remove(instrumentKey);
                _lastTicks.Remove(instrumentKey);
            }
            await UpdateFeed(
                instrumentKey, subscriptions, cancellationToken);
        }
    }

    private ValueTask UpdateFeed(
        string instrumentKey,
        SynchronizedDictionary<DataType, long> subscriptions,
        CancellationToken cancellationToken)
    {
        var mode = subscriptions.Count == 0
            ? null
            : subscriptions.ContainsKey(DataType.MarketDepth)
                ? "FULL"
                : "QUOTE";
        return MarketClient.SetSubscription(
            instrumentKey, mode, cancellationToken);
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

        var key = mdMsg.SecurityId.ToInstrumentKey();
        var instrument = await ResolveInstrument(
            key, cancellationToken);
        var candles = await _restClient.GetCandles(
            instrument,
            mdMsg.GetTimeFrame(),
            mdMsg.From,
            mdMsg.To,
            cancellationToken);
        IEnumerable<PaytmMoneyCandle> ordered =
            candles.OrderBy(candle => candle.Time);
        if (mdMsg.Count is long count)
        {
            ordered = ordered
                .TakeLast((int)Math.Min(count, int.MaxValue))
                .OrderBy(candle => candle.Time);
        }

        foreach (var candle in ordered)
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId =
                        mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId,
                    TypedArg = mdMsg.GetTimeFrame(),
                    OpenTime = candle.Time,
                    OpenPrice = candle.Open,
                    HighPrice = candle.High,
                    LowPrice = candle.Low,
                    ClosePrice = candle.Close,
                    TotalVolume = candle.Volume,
                    OpenInterest = candle.OpenInterest,
                    State = CandleStates.Finished,
                },
                cancellationToken);
        }

        await SendSubscriptionFinishedAsync(
            mdMsg.TransactionId, cancellationToken);
    }

    private async Task<PaytmMoneyInstrument> ResolveInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        if (_instruments.TryGetValue(
            instrumentKey, out var instrument))
        {
            return instrument;
        }

        instrument = await _restClient.GetInstrument(
            instrumentKey, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Paytm Money instrument '{instrumentKey}' was not found in the security master.");
        _instruments[instrumentKey] = instrument;
        return instrument;
    }

    private async ValueTask SendRestSnapshot(
        MarketDataMessage mdMsg,
        DataType dataType,
        PaytmMoneyInstrument instrument,
        CancellationToken cancellationToken)
    {
        var native = (await _restClient.GetLive(
                dataType == DataType.MarketDepth
                    ? "FULL"
                    : "QUOTE",
                [instrument],
                cancellationToken))
            .FirstOrDefault(tick =>
                tick.SecurityId.ToString(
                    CultureInfo.InvariantCulture) ==
                instrument.SecurityId);
        if (native == null)
            return;

        var tick = ToTick(native);
        var securityId = mdMsg.SecurityId;
        if (dataType == DataType.Level1)
        {
            await SendLevel1(
                mdMsg.TransactionId,
                securityId,
                tick,
                cancellationToken);
        }
        else if (dataType == DataType.Ticks &&
            tick.LastPrice is decimal price)
        {
            await SendOutMessageAsync(new ExecutionMessage
            {
                DataTypeEx = DataType.Ticks,
                OriginalTransactionId = mdMsg.TransactionId,
                SecurityId = securityId,
                ServerTime =
                    tick.LastTradeTime ?? tick.ServerTime,
                TradePrice = price,
                TradeVolume = tick.LastQuantity,
            }, cancellationToken);
        }
        else if (dataType == DataType.MarketDepth)
        {
            await SendDepth(
                mdMsg.TransactionId,
                securityId,
                tick,
                cancellationToken);
        }
    }

    private async ValueTask OnTickReceived(
        PaytmMoneyTick tick,
        CancellationToken cancellationToken)
    {
        foreach (var pair in _marketSubscriptions.ToArray())
        {
            var (_, _, securityId, _, _) =
                pair.Key.ParseInstrumentKey();
            if (!securityId.EqualsIgnoreCase(tick.SecurityId))
                continue;

            var subscriptions = pair.Value;
            var stockSharpId =
                _securityIds.TryGetValue2(pair.Key) ??
                new SecurityId
                {
                    SecurityCode = tick.SecurityId,
                    BoardCode = "NSE_EQ",
                    Native = pair.Key,
                };
            if (subscriptions.TryGetValue(
                DataType.Level1, out var level1Id))
            {
                await SendLevel1(
                    level1Id,
                    stockSharpId,
                    tick,
                    cancellationToken);
            }

            if (subscriptions.TryGetValue(
                    DataType.Ticks, out var ticksId) &&
                tick.LastTradeTime is DateTime tradeTime &&
                tick.LastPrice is decimal price &&
                tick.LastQuantity is decimal volume &&
                volume > 0)
            {
                var trade = (tradeTime, price, volume);
                if (!_lastTicks.TryGetValue(
                        pair.Key, out var previous) ||
                    previous != trade)
                {
                    _lastTicks[pair.Key] = trade;
                    await SendOutMessageAsync(
                        new ExecutionMessage
                        {
                            DataTypeEx = DataType.Ticks,
                            OriginalTransactionId =
                                ticksId,
                            SecurityId = stockSharpId,
                            ServerTime = tradeTime,
                            TradePrice = price,
                            TradeVolume = volume,
                        },
                        cancellationToken);
                }
            }

            if (subscriptions.TryGetValue(
                    DataType.MarketDepth, out var depthId) &&
                (tick.Bids.Length > 0 ||
                    tick.Asks.Length > 0))
            {
                await SendDepth(
                    depthId,
                    stockSharpId,
                    tick,
                    cancellationToken);
            }
        }
    }

    private ValueTask SendLevel1(
        long subscriptionId,
        SecurityId securityId,
        PaytmMoneyTick tick,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(
            new Level1ChangeMessage
            {
                OriginalTransactionId = subscriptionId,
                SecurityId = securityId,
                ServerTime = tick.ServerTime,
            }
            .TryAdd(
                Level1Fields.LastTradePrice, tick.LastPrice)
            .TryAdd(
                Level1Fields.LastTradeVolume,
                tick.LastQuantity)
            .TryAdd(
                Level1Fields.LastTradeTime,
                tick.LastTradeTime)
            .TryAdd(
                Level1Fields.AveragePrice, tick.AveragePrice)
            .TryAdd(Level1Fields.Volume, tick.Volume)
            .TryAdd(Level1Fields.OpenPrice, tick.Open)
            .TryAdd(Level1Fields.HighPrice, tick.High)
            .TryAdd(Level1Fields.LowPrice, tick.Low)
            .TryAdd(Level1Fields.ClosePrice, tick.Close)
            .TryAdd(
                Level1Fields.OpenInterest,
                tick.OpenInterest)
            .TryAdd(
                Level1Fields.BidsVolume,
                tick.TotalBuyQuantity)
            .TryAdd(
                Level1Fields.AsksVolume,
                tick.TotalSellQuantity)
            .TryAdd(
                Level1Fields.BestBidPrice,
                tick.Bids.FirstOrDefault()?.Price)
            .TryAdd(
                Level1Fields.BestBidVolume,
                tick.Bids.FirstOrDefault()?.Quantity)
            .TryAdd(
                Level1Fields.BestAskPrice,
                tick.Asks.FirstOrDefault()?.Price)
            .TryAdd(
                Level1Fields.BestAskVolume,
                tick.Asks.FirstOrDefault()?.Quantity),
            cancellationToken);

    private ValueTask SendDepth(
        long subscriptionId,
        SecurityId securityId,
        PaytmMoneyTick tick,
        CancellationToken cancellationToken)
        => SendOutMessageAsync(new QuoteChangeMessage
        {
            OriginalTransactionId = subscriptionId,
            SecurityId = securityId,
            ServerTime = tick.ServerTime,
            Bids = [.. tick.Bids.Select(level =>
                new QuoteChange(level.Price, level.Quantity)
                {
                    OrdersCount = level.Orders,
                })],
            Asks = [.. tick.Asks.Select(level =>
                new QuoteChange(level.Price, level.Quantity)
                {
                    OrdersCount = level.Orders,
                })],
        }, cancellationToken);

    private static PaytmMoneyTick ToTick(
        PaytmMoneyLiveTick native)
    {
        var tradeTime = PaytmMoneyExtensions.FromPaytmEpoch(
            native.LastTradeTime);
        var updateTime = PaytmMoneyExtensions.FromPaytmEpoch(
            native.LastUpdateTime);
        return new()
        {
            SecurityId = native.SecurityId.ToString(
                CultureInfo.InvariantCulture),
            ServerTime =
                updateTime ?? tradeTime ?? DateTime.UtcNow,
            LastTradeTime = tradeTime,
            LastPrice = native.LastPrice,
            LastQuantity = native.LastQuantity,
            AveragePrice = native.AveragePrice,
            Volume = native.Volume,
            TotalBuyQuantity = native.TotalBuyQuantity,
            TotalSellQuantity = native.TotalSellQuantity,
            Open = native.Ohlc?.Open,
            High = native.Ohlc?.High,
            Low = native.Ohlc?.Low,
            Close = native.Ohlc?.Close,
            OpenInterest = native.OpenInterest,
            OpenInterestChange = native.OpenInterestChange,
            Bids = native.Depth?.Bids ?? [],
            Asks = native.Depth?.Asks ?? [],
        };
    }
}
