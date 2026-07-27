namespace StockSharp.EuronextWebServices;

public partial class EuronextWebServicesMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);
        if (lookupMsg.Count is > 0 or null)
        {
            var id = lookupMsg.SecurityId.GetEuronextId();
            var instrument = await SafeClient().GetInstrument(
                id.Isin,
                id.Mic,
                SessionQuality,
                cancellationToken);
            var message = instrument.ToSecurityMessage(
                lookupMsg.TransactionId);
            if (message.IsMatch(
                lookupMsg, lookupMsg.GetSecurityTypes()))
            {
                await SendOutMessageAsync(
                    message, cancellationToken);
            }
        }
        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.Count is > 0 or null)
        {
            var id = mdMsg.SecurityId.GetEuronextId();
            var instrument = await SafeClient().GetInstrument(
                id.Isin,
                id.Mic,
                SessionQuality,
                cancellationToken);
            await SendOutMessageAsync(
                instrument.ToLevel1(
                    mdMsg.TransactionId,
                    mdMsg.SecurityId),
                cancellationToken);
        }
        await CompleteSubscription(mdMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.Count is > 0 or null)
        {
            var id = mdMsg.SecurityId.GetEuronextId();
            var instrument = await SafeClient().GetInstrument(
                id.Isin,
                id.Mic,
                SessionQuality,
                cancellationToken);
            await SendOutMessageAsync(
                instrument.ToDepth(
                    mdMsg.TransactionId,
                    mdMsg.SecurityId),
                cancellationToken);
        }
        await CompleteSubscription(mdMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteSubscription(mdMsg, cancellationToken);
            return;
        }

        var id = mdMsg.SecurityId.GetEuronextId();
        var response = await SafeClient().GetIntraday(
            id.Isin,
            id.Mic,
            SessionQuality,
            trades: true,
            default,
            IntradayDepth,
            cancellationToken);
        var count = checked((int)Math.Min(
            mdMsg.Count ?? int.MaxValue,
            int.MaxValue));
        var values = response.Points
            .Where(point =>
                point is not null &&
                !point.TradeStatus.EqualsIgnoreCase("DEL") &&
                point.OpenPrice > 0 &&
                point.Volume >= 0)
            .Select(point => new
            {
                Point = point,
                Time = point.Time.ToEuronextDate(),
            })
            .Where(value =>
                value.Time is not null &&
                (mdMsg.From is null ||
                    value.Time >= mdMsg.From) &&
                (mdMsg.To is null ||
                    value.Time <= mdMsg.To))
            .OrderBy(value => value.Time)
            .Take(count);
        foreach (var value in values)
        {
            await SendOutMessageAsync(
                new ExecutionMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId,
                    DataTypeEx = DataType.Ticks,
                    ServerTime = value.Time.Value,
                    TradeId = long.TryParse(
                        value.Point.TradeId,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var tradeId)
                            ? tradeId
                            : null,
                    TradeStringId = value.Point.TradeId,
                    TradePrice = value.Point.OpenPrice,
                    TradeVolume = value.Point.Volume,
                },
                cancellationToken);
        }
        await CompleteSubscription(mdMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteSubscription(mdMsg, cancellationToken);
            return;
        }

        var timeFrame = mdMsg.GetTimeFrame();
        if (timeFrame < TimeSpan.FromSeconds(1) ||
            timeFrame > TimeSpan.FromDays(1) ||
            timeFrame.TotalMilliseconds % 1 != 0)
        {
            throw new NotSupportedException(
                "Euronext interval bars require a whole-millisecond " +
                "resolution from one second to one day.");
        }
        var id = mdMsg.SecurityId.GetEuronextId();
        var response = await SafeClient().GetIntraday(
            id.Isin,
            id.Mic,
            SessionQuality,
            trades: false,
            timeFrame,
            IntradayDepth,
            cancellationToken);
        var count = checked((int)Math.Min(
            mdMsg.Count ?? int.MaxValue,
            int.MaxValue));
        var values = response.Points
            .Where(point =>
                point is not null &&
                point.OpenPrice is not null &&
                point.HighPrice is not null &&
                point.LowPrice is not null &&
                point.ClosePrice is not null)
            .Select(point => new
            {
                Point = point,
                Time = point.Time.ToEuronextDate(),
            })
            .Where(value =>
                value.Time is not null &&
                (mdMsg.From is null ||
                    value.Time >= mdMsg.From) &&
                (mdMsg.To is null ||
                    value.Time <= mdMsg.To))
            .OrderBy(value => value.Time)
            .Take(count);
        foreach (var value in values)
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId,
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = value.Time.Value,
                    OpenPrice = value.Point.OpenPrice.Value,
                    HighPrice = value.Point.HighPrice.Value,
                    LowPrice = value.Point.LowPrice.Value,
                    ClosePrice = value.Point.ClosePrice.Value,
                    TotalVolume = value.Point.Volume ?? 0,
                    TotalPrice = value.Point.Turnover ?? 0,
                    TotalTicks = value.Point.TradesCount,
                    State = CandleStates.Finished,
                },
                cancellationToken);
        }
        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async Task CompleteSubscription(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(
            mdMsg, cancellationToken);
        await SendSubscriptionFinishedAsync(
            mdMsg.TransactionId, cancellationToken);
    }
}
