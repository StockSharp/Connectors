namespace StockSharp.QuiverQuant;

public partial class QuiverQuantMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask MarketDataAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        if (QuiverQuantDataTypes.TryGetKind(
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
            !types.Contains(SecurityTypes.Stock))
        {
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var companies = await SafeClient().GetCompanies(
            cancellationToken);
        var skip = lookupMsg.Skip ?? 0;
        var remaining = lookupMsg.Count ?? long.MaxValue;
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var company in companies ?? [])
        {
            if (company is null || company.Ticker.IsEmpty())
                continue;
            var ticker = company.Ticker
                .Trim()
                .ToUpperInvariant();
            if (!seen.Add(ticker))
                continue;
            var security = company.ToSecurityMessage(
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
            : QuiverQuantExtensions.ValidateTicker(rawTicker);
        var target = checked((int)Math.Min(
            mdMsg.Count ?? NewsLimit,
            NewsLimit));
        var perPage = Math.Min(
            PageSize,
            Math.Max(1, target));
        var collected = new List<(
            QuiverQuantNews Article,
            DateTime Time)>();
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        for (var page = 1;
            page <= MaxPages && collected.Count < target;
            page++)
        {
            var articles = await SafeClient().GetNews(
                ticker,
                page,
                perPage,
                cancellationToken);
            foreach (var article in articles ?? [])
            {
                if (article is null ||
                    !QuiverQuantExtensions.TryParseUtc(
                        article.DateTime, out var time) ||
                    (mdMsg.From is not null &&
                        time < mdMsg.From.Value) ||
                    (mdMsg.To is not null &&
                        time > mdMsg.To.Value))
                {
                    continue;
                }
                var key = article.Url
                    .IsEmpty(
                        $"{article.DateTime}:{article.Headline}");
                if (seen.Add(key))
                    collected.Add((article, time));
            }
            if ((articles?.Count ?? 0) < perPage)
                break;
        }

        var securityId = ticker.IsEmpty()
            ? default
            : mdMsg.SecurityId.Normalize(ticker);
        foreach (var item in collected
            .OrderByDescending(item => item.Time)
            .Take(target)
            .OrderBy(item => item.Time))
        {
            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = item.Time,
                    Id = item.Article.Url
                        .IsEmpty(
                            $"{item.Article.DateTime}:{item.Article.Headline}"),
                    Headline = item.Article.Headline,
                    Story = item.Article.Summary,
                    Source = "Quiver Quantitative",
                    Url = item.Article.Url,
                    SecurityId = securityId,
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async ValueTask OnDatasetSubscriptionAsync(
        MarketDataMessage mdMsg,
        QuiverQuantDataKinds kind,
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
            LimitInsiderCodes,
            MostRecentInstitutional,
            IncludeNewFunds,
            SafeCorporateDonorCycle(),
            cancellationToken);
        await SendOutMessageAsync(
            new QuiverQuantDataMessage
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
}
