namespace StockSharp.Bavest;

public partial class BavestMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask MarketDataAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        if (BavestDataTypes.TryGetKind(
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
                    SecurityTypes.Fund or
                    SecurityTypes.Index))
        {
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var symbol = (lookupMsg.SecurityId.Native as string)
            .IsEmpty(lookupMsg.SecurityId.SecurityCode)
            ?.Trim();
        var query = symbol
            .IsEmpty(lookupMsg.Name)
            .IsEmpty(lookupMsg.ShortName)
            ?.Trim();
        var skip = lookupMsg.Skip ?? 0;
        var remaining = lookupMsg.Count ?? long.MaxValue;
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        if (!query.IsEmpty())
        {
            await EmitSearch(
                query,
                lookupMsg,
                types,
                seen,
                skip,
                remaining,
                cancellationToken);
        }
        else
        {
            var includeStocks = types.Count == 0 ||
                types.Contains(SecurityTypes.Stock);
            var includeEtfs = types.Count == 0 ||
                types.Contains(SecurityTypes.Etf);
            foreach (var source in new[]
            {
                (Enabled: includeStocks, Etfs: false,
                    Type: SecurityTypes.Stock),
                (Enabled: includeEtfs, Etfs: true,
                    Type: SecurityTypes.Etf),
            })
            {
                if (!source.Enabled || remaining <= 0)
                    continue;
                for (var page = 0;
                    page < MaxPages && remaining > 0;
                    page++)
                {
                    var response = await SafeClient().GetSecurities(
                        source.Etfs,
                        PageSize,
                        checked(page * PageSize),
                        SafeExchangeCode(),
                        cancellationToken);
                    foreach (var item in response.Data ?? [])
                    {
                        if (item is null ||
                            item.Symbol.IsEmpty() ||
                            !seen.Add(item.Symbol))
                        {
                            continue;
                        }
                        var security = item.ToSecurityMessage(
                            lookupMsg.TransactionId,
                            source.Type);
                        if (!security.IsMatch(lookupMsg, types))
                            continue;
                        if (skip > 0)
                        {
                            skip--;
                            continue;
                        }
                        await SendOutMessageAsync(
                            security, cancellationToken);
                        remaining--;
                        if (remaining <= 0)
                            break;
                    }
                    if ((response.Data?.Length ?? 0) < PageSize ||
                        (response.Meta?.TotalCount is long total &&
                            (page + 1L) * PageSize >= total) ||
                        (response.Meta?.TotalPages is int pages &&
                            page + 1 >= pages))
                    {
                        break;
                    }
                }
            }
        }

        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }

    private async Task EmitSearch(
        string query,
        SecurityLookupMessage lookupMsg,
        HashSet<SecurityTypes> types,
        ISet<string> seen,
        long initialSkip,
        long initialRemaining,
        CancellationToken cancellationToken)
    {
        var skip = initialSkip;
        var remaining = initialRemaining;
        for (var page = 0;
            page < MaxPages && remaining > 0;
            page++)
        {
            var response = await SafeClient().SearchSecurities(
                query,
                PageSize,
                checked(page * PageSize),
                cancellationToken);
            foreach (var item in response.Data ?? [])
            {
                if (item is null ||
                    item.Symbol.IsEmpty() ||
                    !seen.Add(item.Symbol))
                {
                    continue;
                }
                var security = item.ToSecurityMessage(
                    lookupMsg.TransactionId,
                    SecurityTypes.Stock);
                if (!security.IsMatch(lookupMsg, types))
                    continue;
                if (skip > 0)
                {
                    skip--;
                    continue;
                }
                await SendOutMessageAsync(
                    security, cancellationToken);
                remaining--;
                if (remaining <= 0)
                    break;
            }
            if ((response.Data?.Length ?? 0) < PageSize ||
                (response.Meta?.TotalCount is long total &&
                    (page + 1L) * PageSize >= total) ||
                (response.Meta?.TotalPages is int pages &&
                    page + 1 >= pages))
            {
                break;
            }
        }
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
                "Bavest v2 does not expose historical Level1 events.");
        }

        var ticker = mdMsg.SecurityId.GetTicker();
        var quote = await SafeClient().GetQuote(
            ticker,
            SafeCurrency(),
            SafeExchange(),
            cancellationToken);
        if (quote is not null)
        {
            var time = BavestExtensions.TryUnixTime(
                quote.Timestamp, out var parsed)
                ? parsed
                : DateTime.UtcNow;
            var message = new Level1ChangeMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                SecurityId = mdMsg.SecurityId.Normalize(ticker),
                ServerTime = time,
            }
            .TryAdd(
                Level1Fields.LastTradePrice,
                Positive(quote.CurrentPrice))
            .TryAdd(
                Level1Fields.LastTradeTime,
                Positive(quote.CurrentPrice) is not null
                    ? (DateTime?)time
                    : null)
            .TryAdd(
                Level1Fields.OpenPrice,
                Positive(quote.Open))
            .TryAdd(
                Level1Fields.HighPrice,
                Positive(quote.High))
            .TryAdd(
                Level1Fields.LowPrice,
                Positive(quote.Low))
            .TryAdd(
                Level1Fields.ClosePrice,
                Positive(quote.PreviousClose))
            .TryAdd(
                Level1Fields.Change,
                quote.ChangePercent)
            .TryAdd(
                Level1Fields.Volume,
                NonNegative(quote.Volume));
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
        var resolution = timeFrame.ToResolution();
        var ticker = mdMsg.SecurityId.GetTicker();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUtcSafe();
        var from = (mdMsg.From ??
            BavestExtensions.EstimateFrom(
                to, timeFrame, mdMsg.Count))
            .ToUtcSafe();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From),
                from,
                "The candle-history start time is after its end time.");
        }
        var data = await SafeClient().GetCandles(
            ticker,
            resolution,
            from,
            to,
            SafeCurrency(),
            SafeExchange(),
            cancellationToken);
        var remaining = mdMsg.Count ?? long.MaxValue;
        var seen = new HashSet<DateTime>();
        foreach (var item in (data?.Candles ?? [])
            .Where(item =>
                item is not null &&
                item.Open is not null &&
                item.High is not null &&
                item.Low is not null &&
                item.Close is not null &&
                BavestExtensions.TryUnixTime(
                    item.Timestamp, out _))
            .Select(item => new
            {
                Value = item,
                OpenTime = ParseUnix(item.Timestamp),
            })
            .Where(item =>
                item.OpenTime >= from &&
                item.OpenTime <= to)
            .OrderBy(item => item.OpenTime))
        {
            if (remaining <= 0)
                break;
            if (!seen.Add(item.OpenTime))
                continue;
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId.Normalize(ticker),
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = item.OpenTime,
                    CloseTime =
                        item.OpenTime.GetCloseTime(timeFrame),
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

        var ticker = mdMsg.SecurityId.GetOptionalTicker();
        var target = checked((int)Math.Min(
            mdMsg.Count ?? NewsLimit,
            NewsLimit));
        var response = await SafeClient().GetNews(
            ticker,
            target,
            mdMsg.From,
            mdMsg.To,
            cancellationToken);
        foreach (var article in (response.Data ?? [])
            .Where(article =>
                article is not null &&
                !article.Title.IsEmpty() &&
                BavestExtensions.TryParseUtc(
                    article.PublishedDate, out _))
            .Select(article => new
            {
                Value = article,
                Time = ParseUtc(article.PublishedDate),
            })
            .Where(item =>
                (mdMsg.From is null ||
                    item.Time >= mdMsg.From.Value) &&
                (mdMsg.To is null ||
                    item.Time <= mdMsg.To.Value))
            .OrderBy(item => item.Time)
            .Take(target))
        {
            var itemTicker = ticker
                .IsEmpty(article.Value.Symbol)
                ?.Trim()
                .ToUpperInvariant();
            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = article.Time,
                    Id = article.Value.Url
                        .IsEmpty(
                            $"{article.Value.PublishedDate}:" +
                            article.Value.Title),
                    Headline = article.Value.Title,
                    Story = article.Value.Text,
                    Source = article.Value.Site,
                    Url = article.Value.Url,
                    SecurityId = itemTicker.IsEmpty()
                        ? (SecurityId?)null
                        : mdMsg.SecurityId.Normalize(itemTicker),
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async ValueTask OnDatasetSubscriptionAsync(
        MarketDataMessage mdMsg,
        BavestDataKinds kind,
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

        var ticker = mdMsg.SecurityId.GetOptionalTicker();
        var limit = checked((int)Math.Min(
            mdMsg.Count ?? DatasetLimit,
            DatasetLimit));
        var response = await SafeClient().GetDataset(
            kind,
            ticker,
            FinancialFrequency,
            limit,
            SafeCurrency(),
            TraceEtfMetrics,
            SafeScreenerQuery(),
            cancellationToken);
        await SendOutMessageAsync(
            new BavestDataMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                Dataset = kind,
                SecurityId = ticker.IsEmpty()
                    ? default
                    : mdMsg.SecurityId.Normalize(ticker),
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

    private static DateTime ParseUnix(long? value)
    {
        BavestExtensions.TryUnixTime(value, out var result);
        return result;
    }

    private static DateTime ParseUtc(string value)
    {
        BavestExtensions.TryParseUtc(value, out var result);
        return result;
    }

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;

    private static decimal? NonNegative(decimal? value)
        => value is >= 0 ? value : null;
}
