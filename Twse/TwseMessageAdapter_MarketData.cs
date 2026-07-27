namespace StockSharp.Twse;

public partial class TwseMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var value = lookupMsg.SecurityId.GetTwseSymbol();
        var name = lookupMsg.Name
            .IsEmpty(lookupMsg.ShortName)
            ?.Trim();
        var types = lookupMsg.GetSecurityTypes();
        var skip = lookupMsg.Skip ?? 0;
        var left = lookupMsg.Count ?? long.MaxValue;

        if (left > 0)
        {
            var snapshot = await LoadSnapshot(cancellationToken);
            var prices = (snapshot.Prices ?? [])
                .Where(price => !price.Code.IsEmpty())
                .GroupBy(
                    price => price.Code,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            var securities = snapshot
                .GetAllProfiles()
                .Where(profile => profile.Matches(value, name))
                .Select(profile => profile.ToSecurityMessage(
                    prices.TryGetValue(profile.Code, out var price)
                        ? price
                        : null,
                    lookupMsg.TransactionId))
                .Where(security =>
                    security.IsMatch(lookupMsg, types))
                .OrderBy(
                    security => security.SecurityId.SecurityCode,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var security in securities)
            {
                if (skip > 0)
                {
                    skip--;
                    continue;
                }

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

        var records = await LoadRecords(mdMsg, cancellationToken);
        var sent = 0;

        foreach (var record in records)
        {
            await SendOutMessageAsync(
                new Level1ChangeMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = record.SecurityId,
                    ServerTime = record.CloseTime,
                }
                .TryAdd(
                    Level1Fields.OpenPrice,
                    record.OpenPrice)
                .TryAdd(
                    Level1Fields.HighPrice,
                    record.HighPrice)
                .TryAdd(
                    Level1Fields.LowPrice,
                    record.LowPrice)
                .TryAdd(
                    Level1Fields.ClosePrice,
                    record.ClosePrice)
                .TryAdd(
                    Level1Fields.LastTradePrice,
                    record.ClosePrice)
                .TryAdd(
                    Level1Fields.MarketPriceYesterday,
                    record.PreviousClosePrice)
                .TryAdd(
                    Level1Fields.Change,
                    record.PriceChange)
                .TryAdd(
                    Level1Fields.Volume,
                    record.Volume)
                .TryAdd(
                    Level1Fields.Turnover,
                    record.Turnover)
                .TryAdd(
                    Level1Fields.TradesCount,
                    record.TradesCount)
                .TryAdd(
                    Level1Fields.PriceEarnings,
                    record.PriceEarnings)
                .TryAdd(
                    Level1Fields.Yield,
                    record.DividendYield)
                .TryAdd(
                    Level1Fields.PriceBook,
                    record.PriceBook),
                cancellationToken);
            sent++;
        }

        WarnIfEmpty(sent, mdMsg.SecurityId.GetTwseSymbol());
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
                "TWSE OpenAPI provides only native daily candles.");
        }

        var records = await LoadRecords(mdMsg, cancellationToken);
        var sent = 0;

        foreach (var record in records.Where(
            record => record.HasOhlc))
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = record.SecurityId,
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = record.OpenTime,
                    OpenPrice = record.OpenPrice.Value,
                    HighPrice = record.HighPrice.Value,
                    LowPrice = record.LowPrice.Value,
                    ClosePrice = record.ClosePrice.Value,
                    TotalVolume = record.Volume ?? 0,
                    State = CandleStates.Finished,
                },
                cancellationToken);
            sent++;
        }

        WarnIfEmpty(sent, mdMsg.SecurityId.GetTwseSymbol());
        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async Task<TwseDailyRecord[]> LoadRecords(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var symbol = mdMsg.SecurityId.GetTwseSymbol();
        var from = mdMsg.From?.ToTaipeiDate();
        var to = mdMsg.To?.ToTaipeiDate();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From), from,
                "TWSE history start date is after its end date.");
        }

        var requested = mdMsg.Count is long count
            ? checked((int)Math.Min(count.Max(0), int.MaxValue))
            : int.MaxValue;
        var snapshot = await LoadSnapshot(cancellationToken);
        var valuations = (snapshot.Valuations ?? [])
            .Where(value => !value.Code.IsEmpty())
            .GroupBy(
                value => value.Code,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        return (snapshot.Prices ?? [])
            .Where(row =>
                symbol.IsEmpty() ||
                row.Code.EqualsIgnoreCase(symbol))
            .Select(row => row.ToRecord(
                valuations.TryGetValue(row.Code, out var valuation)
                    ? valuation
                    : null))
            .Where(record =>
                (from is null ||
                    record.TradingDate >= from.Value) &&
                (to is null ||
                    record.TradingDate <= to.Value))
            .OrderBy(record => record.TradingDate)
            .ThenBy(
                record => record.SecurityId.SecurityCode,
                StringComparer.OrdinalIgnoreCase)
            .Take(requested)
            .ToArray();
    }

    private void WarnIfEmpty(int sent, string symbol)
    {
        if (sent == 0)
        {
            this.AddWarningLog(
                "TWSE OpenAPI returned no daily observations for {0}.",
                symbol.IsEmpty("all listed securities"));
        }
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
