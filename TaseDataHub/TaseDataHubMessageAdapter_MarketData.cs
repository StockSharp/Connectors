namespace StockSharp.TaseDataHub;

public partial class TaseDataHubMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var code = lookupMsg.SecurityId.SecurityCode?.Trim();
        var name = lookupMsg.Name
            .IsEmpty(lookupMsg.ShortName)
            ?.Trim();
        var requestedTypes = lookupMsg.GetSecurityTypes();
        var reference = await LoadReference(cancellationToken);
        var securities = reference.Securities
            .Where(security => security.Matches(code, name))
            .Select(security =>
            {
                reference.Types.TryGetValue(
                    security.SecurityFullTypeCode.IsEmpty(string.Empty),
                    out var type);
                return security.ToSecurityMessage(
                    type, lookupMsg.TransactionId);
            })
            .Where(security =>
                security.IsMatch(lookupMsg, requestedTypes))
            .OrderBy(
                security => security.SecurityId.SecurityCode,
                StringComparer.OrdinalIgnoreCase);

        var skip = lookupMsg.Skip ?? 0;
        var left = lookupMsg.Count ?? long.MaxValue;
        foreach (var security in securities)
        {
            if (left <= 0)
                break;
            if (skip > 0)
            {
                skip--;
                continue;
            }

            await SendOutMessageAsync(
                security, cancellationToken);
            left--;
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
        if (mdMsg.Count is <= 0)
        {
            await CompleteSubscription(
                mdMsg, cancellationToken);
            return;
        }

        var records = await LoadEod(mdMsg, cancellationToken);
        var record = records
            .OrderByDescending(item => item.TradeDate.ToTaseDate())
            .FirstOrDefault();
        if (record is not null)
        {
            await SendOutMessageAsync(
                record.ToLevel1(
                    mdMsg.TransactionId,
                    record.ToSecurityId(mdMsg.SecurityId)),
                cancellationToken);
        }
        else
        {
            this.AddWarningLog(
                "TASE returned no EOD data for security {0}.",
                mdMsg.SecurityId);
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
            await CompleteSubscription(
                mdMsg, cancellationToken);
            return;
        }

        var timeFrame = mdMsg.GetTimeFrame();
        if (timeFrame != TimeSpan.FromDays(1))
        {
            throw new NotSupportedException(
                "TASE Data Hub provides native daily EOD candles only.");
        }

        var records = await LoadEod(mdMsg, cancellationToken);
        var count = mdMsg.Count is long requested
            ? checked((int)Math.Min(requested.Max(0), int.MaxValue))
            : int.MaxValue;
        var filtered = records
            .Where(record =>
                record.OpeningPrice is not null &&
                record.High is not null &&
                record.Low is not null &&
                record.ClosingPrice is not null)
            .OrderBy(record => record.TradeDate.ToTaseDate());
        if (count != int.MaxValue)
            filtered = filtered.Take(count).OrderBy(
                record => record.TradeDate.ToTaseDate());

        foreach (var record in filtered)
        {
            var date = record.TradeDate.ToTaseDate();
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = record.ToSecurityId(
                        mdMsg.SecurityId),
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = date.ToTaseTime(TimeSpan.Zero),
                    OpenPrice = record.OpeningPrice.Value,
                    HighPrice = record.High.Value,
                    LowPrice = record.Low.Value,
                    ClosePrice = record.ClosingPrice.Value,
                    TotalVolume = record.Volume ?? 0,
                    TotalTicks = record.TransactionsNumber is long ticks
                        ? checked((int)Math.Min(ticks, int.MaxValue))
                        : null,
                    State = CandleStates.Finished,
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async Task<TaseEodRecord[]> LoadEod(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var securityId = mdMsg.SecurityId.GetTaseSecurityId();
        var records = await SafeClient().GetEodBySecurity(
            securityId, cancellationToken);
        var from = mdMsg.From?.Date;
        var to = mdMsg.To?.Date;
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From), from,
                "TASE history start date is after its end date.");
        }
        if (from is not null &&
            to is not null &&
            to.Value - from.Value > TimeSpan.FromDays(14))
        {
            throw new NotSupportedException(
                "The subscribed TASE EOD product exposes only " +
                "the latest seven trading days.");
        }

        return records
            .Where(record =>
                record is not null &&
                record.SecurityId == securityId &&
                !record.TradeDate.IsEmpty())
            .Where(record =>
            {
                var date = record.TradeDate.ToTaseDate();
                return (from is null || date >= from) &&
                    (to is null || date <= to);
            })
            .ToArray();
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

static class TaseDataHubMessageExtensions
{
    public static Level1ChangeMessage ToLevel1(
        this TaseEodRecord record,
        long originalTransactionId,
        SecurityId securityId)
    {
        var date = record.TradeDate.ToTaseDate();
        return new Level1ChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = securityId,
            ServerTime = date.ToTaseTime(
                new TimeSpan(17, 0, 0)),
        }
        .TryAdd(Level1Fields.OpenPrice, record.OpeningPrice)
        .TryAdd(Level1Fields.HighPrice, record.High)
        .TryAdd(Level1Fields.LowPrice, record.Low)
        .TryAdd(Level1Fields.ClosePrice, record.ClosingPrice)
        .TryAdd(Level1Fields.LastTradePrice, record.ClosingPrice)
        .TryAdd(Level1Fields.MarketPriceYesterday, record.BasePrice)
        .TryAdd(Level1Fields.Change, record.ChangeValue)
        .TryAdd(Level1Fields.Volume, record.Volume)
        .TryAdd(Level1Fields.Turnover, record.Turnover)
        .TryAdd(Level1Fields.TradesCount, record.TransactionsNumber)
        .TryAdd(Level1Fields.IssueSize, record.ListedCapital);
    }
}
