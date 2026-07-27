namespace StockSharp.GuruFocus;

public partial class GuruFocusMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask MarketDataAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        if (GuruFocusDataTypes.TryGetKind(
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
        var includeStocks = types.Count == 0 ||
            types.Contains(SecurityTypes.Stock);
        var includeEtfs = types.Contains(SecurityTypes.Etf) ||
            (types.Count == 0 &&
                SafeRegionCode() == "U");
        if (!includeStocks && !includeEtfs)
        {
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var exact = lookupMsg.SecurityId.SecurityCode
            .IsEmpty(lookupMsg.SecurityId.Native as string)
            ?.Trim();
        if (!exact.IsEmpty())
        {
            await LookupExactSecurity(
                lookupMsg,
                types,
                includeStocks,
                includeEtfs,
                exact,
                cancellationToken);
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var skip = lookupMsg.Skip ?? 0;
        var remaining = lookupMsg.Count ?? long.MaxValue;
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        if (includeStocks)
        {
            for (var pageNumber = 1;
                pageNumber <= MaxLookupPages &&
                    remaining > 0;
                pageNumber++)
            {
                var page = await SafeClient().GetStocks(
                    SafeRegionCode(),
                    pageNumber,
                    PageSize,
                    cancellationToken);
                foreach (var item in page.Data ?? [])
                {
                    if (item is null || item.Symbol.IsEmpty())
                        continue;
                    var key = item.StockId
                        .IsEmpty(item.Symbol);
                    if (!seen.Add(key))
                        continue;
                    var security = item.ToSecurityMessage(
                        SecurityTypes.Stock,
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
                if (page.IsPageComplete(
                    pageNumber, PageSize))
                {
                    break;
                }
            }
        }

        if (includeEtfs && remaining > 0)
        {
            for (var pageNumber = 1;
                pageNumber <= MaxLookupPages &&
                    remaining > 0;
                pageNumber++)
            {
                var page = await SafeClient().GetEtfs(
                    pageNumber,
                    PageSize,
                    cancellationToken);
                foreach (var item in page.Data ?? [])
                {
                    if (item is null || item.Symbol.IsEmpty())
                        continue;
                    var key = item.StockId
                        .IsEmpty(item.Symbol);
                    if (!seen.Add(key))
                        continue;
                    var security = item.ToSecurityMessage(
                        SecurityTypes.Etf,
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
                if (page.IsPageComplete(
                    pageNumber, PageSize))
                {
                    break;
                }
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
        if (mdMsg.From is not null || mdMsg.To is not null)
        {
            throw new NotSupportedException(
                "GuruFocus does not expose historical Level1 events.");
        }

        var ticker = mdMsg.SecurityId.GetTicker();
        var result = await SafeClient().GetSnapshot(
            ticker, cancellationToken);
        var snapshot = result.Snapshot;
        if (snapshot is not null)
        {
            var time = GetSnapshotTime(snapshot);
            var price = Positive(snapshot.Price);
            var open = Positive(snapshot.Open);
            var message = new Level1ChangeMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                SecurityId = mdMsg.SecurityId.Normalize(ticker),
                ServerTime = time,
            }
            .TryAdd(Level1Fields.LastTradePrice, price)
            .TryAdd(
                Level1Fields.LastTradeTime,
                price is null ? null : (DateTime?)time)
            .TryAdd(Level1Fields.OpenPrice, open)
            .TryAdd(
                Level1Fields.HighPrice,
                Positive(snapshot.High))
            .TryAdd(
                Level1Fields.LowPrice,
                Positive(snapshot.Low))
            .TryAdd(
                Level1Fields.Volume,
                NonNegative(
                    snapshot.IntradayVolume ??
                    snapshot.Volume))
            .TryAdd(
                Level1Fields.Change,
                price is not null && open is not null
                    ? price.Value - open.Value
                    : null);
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
        if (timeFrame != TimeSpan.FromDays(1))
        {
            throw new NotSupportedException(
                $"GuruFocus does not support {timeFrame} candles.");
        }
        var ticker = mdMsg.SecurityId.GetTicker();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUtcSafe();
        var from = (mdMsg.From ??
            GuruFocusExtensions.EstimateFrom(to, mdMsg.Count))
            .ToUtcSafe();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From),
                from,
                "The price-history start time is after its end time.");
        }

        var prices = await SafeClient().GetPrices(
            ticker, from, to, cancellationToken);
        var remaining = mdMsg.Count ?? long.MaxValue;
        var seen = new HashSet<DateTime>();
        foreach (var item in (prices ?? [])
            .Where(item => item is not null)
            .Select(item => new
            {
                Value = item,
                Parsed = GuruFocusExtensions.TryParseUtc(
                    item.Date, out var time),
                Time = time,
            })
            .Where(item =>
                item.Parsed &&
                item.Time >= from.Date &&
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
                    CloseTime = item.Time.AddDays(1),
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

        var rawTicker = mdMsg.SecurityId.SecurityCode
            .IsEmpty(mdMsg.SecurityId.Native as string);
        var ticker = rawTicker.IsEmpty()
            ? null
            : GuruFocusExtensions.ValidateTicker(rawTicker);
        var target = checked((int)Math.Min(
            mdMsg.Count ?? NewsLimit,
            NewsLimit));
        var collected = new List<(
            GuruFocusArticle Article,
            DateTime Time,
            string Ticker)>();
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var pageLimit = Math.Min(MaxLookupPages, 20);

        for (var pageNumber = 1;
            pageNumber <= pageLimit &&
                collected.Count < target;
            pageNumber++)
        {
            var maximum = ticker.IsEmpty() ? 200 : 100;
            var perPage = Math.Min(
                maximum,
                Math.Max(1, Math.Min(PageSize, target)));
            GuruFocusArticle[] articles;
            long? total;
            if (ticker.IsEmpty())
            {
                var page = await SafeClient().GetHeadlines(
                    pageNumber,
                    perPage,
                    cancellationToken);
                articles = page.Data ?? [];
                total = page.Total;
            }
            else
            {
                var page = await SafeClient().GetNews(
                    ticker,
                    pageNumber,
                    perPage,
                    cancellationToken);
                articles = page.Articles ?? [];
                total = page.Total;
            }

            foreach (var article in articles)
            {
                if (article is null ||
                    !GuruFocusExtensions.TryParseUtc(
                        article.PublishTime,
                        out var time) ||
                    (mdMsg.From is not null &&
                        time < mdMsg.From.Value) ||
                    (mdMsg.To is not null &&
                        time > mdMsg.To.Value))
                {
                    continue;
                }
                var articleTicker = ticker
                    .IsEmpty(article.Stocks?
                        .FirstOrDefault())
                    ?.Trim()
                    .ToUpperInvariant();
                var key = article.Id
                    .IsEmpty(article.Link)
                    .IsEmpty(
                        $"{article.PublishTime}:{article.Subject}");
                if (!seen.Add(key))
                    continue;
                collected.Add((article, time, articleTicker));
            }

            if (articles.Length < perPage ||
                total is > 0 &&
                    checked((long)pageNumber * perPage) >=
                        total.Value)
            {
                break;
            }
        }

        foreach (var item in collected
            .OrderByDescending(item => item.Time)
            .Take(target)
            .OrderBy(item => item.Time))
        {
            var securityId = item.Ticker.IsEmpty()
                ? default
                : new SecurityId
                {
                    SecurityCode = item.Ticker,
                    BoardCode =
                        GuruFocusExtensions.DefaultBoard,
                    Native = item.Ticker,
                };
            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = item.Time,
                    Id = item.Article.Id
                        .IsEmpty(item.Article.Link),
                    Headline = item.Article.Subject,
                    Story = item.Article.Body
                        .IsEmpty(item.Article.Subtitle),
                    Source = "GuruFocus",
                    Url = item.Article.Link,
                    SecurityId = securityId,
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async ValueTask OnDatasetSubscriptionAsync(
        MarketDataMessage mdMsg,
        GuruFocusDataKinds kind,
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
            mdMsg.Count ?? DatasetLimit,
            DatasetLimit));
        var response = await SafeClient().GetDataset(
            kind,
            ticker,
            limit,
            mdMsg.From,
            mdMsg.To,
            FilingFormType,
            SafeGuruTradeActions(),
            cancellationToken);
        await SendOutMessageAsync(
            new GuruFocusDataMessage
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

    private async Task LookupExactSecurity(
        SecurityLookupMessage lookupMsg,
        HashSet<SecurityTypes> types,
        bool includeStocks,
        bool includeEtfs,
        string exact,
        CancellationToken cancellationToken)
    {
        var ticker = GuruFocusExtensions.ValidateTicker(exact);
        GuruFocusSecurity identity = null;
        var securityType = SecurityTypes.Stock;
        if (includeStocks)
        {
            try
            {
                var profile = await SafeClient().GetProfile(
                    ticker, cancellationToken);
                identity = profile?.Identity;
            }
            catch (GuruFocusApiException ex)
                when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }
        if (identity is null && includeEtfs)
        {
            var etf = await SafeClient().GetEtfData(
                ticker, cancellationToken);
            identity = etf?.BasicInformation;
            securityType = SecurityTypes.Etf;
        }
        if (identity is null)
            return;

        var security = identity.ToSecurityMessage(
            securityType,
            lookupMsg.TransactionId);
        if ((lookupMsg.Skip ?? 0) == 0 &&
            security.IsMatch(lookupMsg, types))
        {
            await SendOutMessageAsync(
                security, cancellationToken);
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

    private static DateTime GetSnapshotTime(
        GuruFocusSnapshot snapshot)
        => GuruFocusExtensions.TryParseDisplayTime(
            snapshot.DisplayTimestamp, out var time)
                ? time
                : DateTime.UtcNow;

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;

    private static decimal? NonNegative(decimal? value)
        => value is >= 0 ? value : null;
}
