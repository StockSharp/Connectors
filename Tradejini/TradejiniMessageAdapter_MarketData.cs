namespace StockSharp.Tradejini;

public partial class TradejiniMessageAdapter
{
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

        foreach (var instrument in
            await _restClient.GetInstruments(cancellationToken))
        {
            SecurityId securityId;
            SecurityTypes securityType;
            try
            {
                securityId = instrument.ToSecurityId();
                securityType = instrument.ToSecurityType();
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            var security = new SecurityMessage
            {
                OriginalTransactionId = lookupMsg.TransactionId,
                SecurityId = securityId,
                SecurityType = securityType,
                Name = instrument.Description
                    .IsEmpty(instrument.DisplayName)
                    .IsEmpty(instrument.Symbol),
                ShortName = instrument.DisplayName
                    .IsEmpty(instrument.Symbol),
                Class = instrument.Instrument.IsEmpty(instrument.Series),
                Currency = CurrencyTypes.INR,
                PriceStep = instrument.TickSize > 0
                    ? instrument.TickSize
                    : null,
                VolumeStep = instrument.LotSize > 0
                    ? instrument.LotSize
                    : null,
                Multiplier = instrument.LotMultiplier > 0
                    ? instrument.LotMultiplier
                    : instrument.LotSize > 0
                        ? instrument.LotSize
                        : null,
                ExpiryDate = instrument.Expiry,
                Strike = instrument.Strike > 0
                    ? instrument.Strike
                    : null,
                OptionType = instrument.OptionType.ToOptionType(),
            };
            if (securityType is SecurityTypes.Future or
                SecurityTypes.Option)
            {
                var underlying =
                    CreateUnderlyingSecurityId(instrument);
                if (underlying != null)
                    security.UnderlyingSecurityId = underlying.Value;
            }
            if (!security.IsMatch(lookupMsg, securityTypes))
                continue;

            await SendOutMessageAsync(security, cancellationToken);
            if (--left <= 0)
                break;
        }

        await SendSubscriptionResultAsync(
            lookupMsg,
            cancellationToken);
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

        var timeFrame = mdMsg.GetTimeFrame();
        var candles = await _restClient.GetCandles(
            mdMsg.SecurityId.ToSymbolId(),
            timeFrame,
            mdMsg.From,
            mdMsg.To,
            cancellationToken);
        IEnumerable<TradejiniCandle> ordered =
            candles.OrderBy(candle => candle.UnixTime);
        if (mdMsg.Count is long count)
        {
            ordered = ordered
                .TakeLast((int)Math.Min(count, int.MaxValue))
                .OrderBy(candle => candle.UnixTime);
        }

        foreach (var candle in ordered)
        {
            await SendOutMessageAsync(new TimeFrameCandleMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                SecurityId = mdMsg.SecurityId,
                TypedArg = timeFrame,
                OpenTime = candle.UnixTime.FromUnixSeconds(),
                OpenPrice = candle.Open,
                HighPrice = candle.High,
                LowPrice = candle.Low,
                ClosePrice = candle.Close,
                TotalVolume = candle.Volume,
                OpenInterest = candle.OpenInterest,
                State = CandleStates.Finished,
            }, cancellationToken);
        }

        await SendSubscriptionFinishedAsync(
            mdMsg.TransactionId,
            cancellationToken);
    }

    private static SecurityId? CreateUnderlyingSecurityId(
        TradejiniInstrument instrument)
    {
        if (instrument.UnderlyingId.IsEmpty())
        {
            return instrument.Symbol.IsEmpty()
                ? null
                : new SecurityId
                {
                    SecurityCode = instrument.Symbol,
                };
        }

        try
        {
            return instrument.UnderlyingId
                .ToTradejiniSecurityId();
        }
        catch (FormatException)
        {
            return new()
            {
                SecurityCode = instrument.UnderlyingId,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return new()
            {
                SecurityCode = instrument.UnderlyingId,
            };
        }
    }
}
