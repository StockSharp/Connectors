namespace StockSharp.FinancialDatasets;

public partial class FinancialDatasetsMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask MarketDataAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        if (FinancialDatasetsDataTypes.TryGetKind(
            mdMsg.DataType2, out var kind))
        {
            await OnDatasetSubscriptionAsync(
                mdMsg, kind, cancellationToken);
            return;
        }
        await base.MarketDataAsync(mdMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        if (lookupMsg.Skip is < 0)
            throw new ArgumentOutOfRangeException(nameof(lookupMsg.Skip));
        if (lookupMsg.Count is <= 0)
        {
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var types = lookupMsg.GetSecurityTypes();
        if (types.Count > 0 &&
            !types.Any(type =>
                type is SecurityTypes.Stock or
                    SecurityTypes.Etf or
                    SecurityTypes.Fund))
        {
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var native = lookupMsg.SecurityId.Native as string;
        var exact = native
            .IsEmpty(lookupMsg.SecurityId.SecurityCode)
            ?.Trim();
        if (!exact.IsEmpty())
        {
            var isCik = native == exact &&
                exact.All(char.IsDigit) &&
                exact.Length <= 10;
            var response = await SafeClient().GetFacts(
                exact,
                isCik,
                cancellationToken);
            var facts = response.Facts;
            if (facts is not null &&
                !facts.Ticker.IsEmpty() &&
                (!ActiveOnly || facts.IsActive != false))
            {
                var security = facts.ToSecurityMessage(
                    lookupMsg.TransactionId);
                if (security.IsMatch(lookupMsg, types))
                {
                    await SendOutMessageAsync(
                        security, cancellationToken);
                }
            }
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var query = lookupMsg.Name
            .IsEmpty(lookupMsg.ShortName)
            ?.Trim();
        var responseTickers = await SafeClient().GetTickers(
            ActiveOnly, cancellationToken);
        var skip = lookupMsg.Skip ?? 0;
        var remaining = lookupMsg.Count ?? long.MaxValue;
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var value in responseTickers.Tickers ?? [])
        {
            var ticker = value?.Trim().ToUpperInvariant();
            if (ticker.IsEmpty() ||
                !seen.Add(ticker) ||
                (!query.IsEmpty() &&
                    !ticker.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var security = ticker.ToSecurityMessage(
                lookupMsg.TransactionId);
            if (!security.IsMatch(lookupMsg, types))
                continue;
            if (skip > 0)
            {
                skip--;
                continue;
            }
            if (remaining <= 0)
                break;
            await SendOutMessageAsync(
                security, cancellationToken);
            remaining--;
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
        if (mdMsg.From is not null || mdMsg.To is not null)
        {
            throw new NotSupportedException(
                "Financial Datasets does not expose historical Level1 events.");
        }

        var ticker = mdMsg.SecurityId.GetTicker();
        var response = await SafeClient().GetSnapshot(
            ticker, cancellationToken);
        var snapshot = response.Snapshot;
        if (snapshot is not null)
        {
            var time = GetSnapshotTime(snapshot);
            var message = new Level1ChangeMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                SecurityId = mdMsg.SecurityId.Normalize(ticker),
                ServerTime = time,
            }
            .TryAdd(
                Level1Fields.LastTradePrice,
                Positive(snapshot.Price))
            .TryAdd(
                Level1Fields.LastTradeTime,
                Positive(snapshot.Price) is not null
                    ? (DateTime?)time
                    : null)
            .TryAdd(
                Level1Fields.Change,
                snapshot.DayChangePercent);
            if (message.Changes.Count > 0)
            {
                await SendOutMessageAsync(
                    message, cancellationToken);
            }
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
        var (interval, _) =
            timeFrame.ToFinancialDatasetsInterval();
        var ticker = mdMsg.SecurityId.GetTicker();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUtcSafe();
        var from = (mdMsg.From ??
            FinancialDatasetsExtensions.EstimateFrom(
                to, timeFrame, mdMsg.Count))
            .ToUtcSafe();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From),
                from,
                "The price-history start time is after its end time.");
        }

        var response = await SafeClient().GetPrices(
            ticker,
            interval,
            from,
            to,
            cancellationToken);
        var remaining = mdMsg.Count ?? long.MaxValue;
        var seen = new HashSet<DateTime>();

        foreach (var item in response.Prices
            .Where(item => item is not null)
            .Select(item => new
            {
                Value = item,
                Parsed = FinancialDatasetsExtensions.TryParseUtc(
                    item.Time, out var time),
                Time = time,
            })
            .Where(item =>
                item.Parsed &&
                item.Time >= from &&
                item.Time <= to &&
                item.Value.Open is not null &&
                item.Value.High is not null &&
                item.Value.Low is not null &&
                item.Value.Close is not null)
            .OrderBy(item => item.Time))
        {
            if (remaining <= 0)
                break;
            if (!seen.Add(item.Time))
                continue;
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId.Normalize(ticker),
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = item.Time,
                    CloseTime = item.Time.GetCloseTime(timeFrame),
                    OpenPrice = item.Value.Open.Value,
                    HighPrice = item.Value.High.Value,
                    LowPrice = item.Value.Low.Value,
                    ClosePrice = item.Value.Close.Value,
                    TotalVolume =
                        NonNegative(item.Value.Volume) ?? 0,
                    State = CandleStates.Finished,
                },
                cancellationToken);
            remaining--;
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnNewsSubscriptionAsync(
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
        if (mdMsg.From > mdMsg.To)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From),
                mdMsg.From,
                "The news-history start time is after its end time.");
        }

        var rawTicker = (mdMsg.SecurityId.Native as string)
            .IsEmpty(mdMsg.SecurityId.SecurityCode);
        var ticker = rawTicker.IsEmpty()
            ? null
            : mdMsg.SecurityId.GetTicker();
        var target = checked((int)Math.Min(
            mdMsg.Count ?? NewsLimit,
            NewsLimit));
        var response = await SafeClient().GetNews(
            ticker, target, cancellationToken);

        foreach (var item in response.News
            .Where(item =>
                item is not null &&
                FinancialDatasetsExtensions.TryParseUtc(
                    item.Date, out _))
            .Select(item => new
            {
                Value = item,
                Time = ParseUtc(item.Date),
            })
            .Where(item =>
                (mdMsg.From is null ||
                    item.Time >= mdMsg.From) &&
                (mdMsg.To is null ||
                    item.Time <= mdMsg.To))
            .OrderBy(item => item.Time)
            .Take(target))
        {
            var itemTicker = ticker
                .IsEmpty(item.Value.Ticker)
                ?.Trim()
                .ToUpperInvariant();
            var securityId = itemTicker.IsEmpty()
                ? default
                : ticker.IsEmpty()
                    ? new SecurityId
                    {
                        SecurityCode = itemTicker,
                        BoardCode =
                            FinancialDatasetsExtensions.DefaultBoard,
                        Native = itemTicker,
                    }
                    : mdMsg.SecurityId.Normalize(itemTicker);
            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = item.Time,
                    Id = item.Value.Url
                        .IsEmpty($"{itemTicker}:{item.Value.Date}:{item.Value.Title}"),
                    Headline = item.Value.Title,
                    Source = item.Value.Source,
                    Url = item.Value.Url,
                    SecurityId = securityId,
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async ValueTask OnDatasetSubscriptionAsync(
        MarketDataMessage mdMsg,
        FinancialDatasetsDataKinds kind,
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
        if (mdMsg.From > mdMsg.To)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From),
                mdMsg.From,
                "The dataset start time is after its end time.");
        }

        var ticker = mdMsg.SecurityId.GetTicker();
        var limit = checked((int)Math.Min(
            mdMsg.Count ?? DataLimit,
            DataLimit));
        var response = await SafeClient().GetDataset(
            kind,
            ticker,
            FinancialPeriod,
            limit,
            mdMsg.From,
            mdMsg.To,
            cancellationToken);
        await SendOutMessageAsync(
            new FinancialDatasetsDataMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                Dataset = kind,
                SecurityId = mdMsg.SecurityId.Normalize(ticker),
                ServerTime = DateTime.UtcNow,
                Resource = response.Resource,
                Payload = response.Payload,
            },
            cancellationToken);
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

    private static DateTime GetSnapshotTime(
        FinancialDatasetsSnapshot snapshot)
    {
        if (FinancialDatasetsExtensions.TryParseUtc(
            snapshot.Time, out var time))
        {
            return time;
        }
        if (snapshot.TimeMilliseconds is > 0)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(
                    snapshot.TimeMilliseconds.Value).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
        return DateTime.UtcNow;
    }

    private static DateTime ParseUtc(string value)
    {
        FinancialDatasetsExtensions.TryParseUtc(
            value, out var result);
        return result;
    }

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;

    private static decimal? NonNegative(decimal? value)
        => value is >= 0 ? value : null;
}
