namespace StockSharp.Marketaux;

public partial class MarketauxMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask MarketDataAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        if (MarketauxDataTypes.TryGetKind(
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
        var search = symbol.IsEmpty()
            ? lookupMsg.Name
                .IsEmpty(lookupMsg.ShortName)
                ?.Trim()
            : null;
        var skip = lookupMsg.Skip ?? 0;
        var remaining = lookupMsg.Count ?? long.MaxValue;
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        for (var page = 1;
            page <= MaxPages && remaining > 0;
            page++)
        {
            var response = await SafeClient().GetEntities(
                search,
                symbol,
                page,
                SafeEntityTypes(),
                SafeCountries(),
                cancellationToken);
            foreach (var entity in response.Data ?? [])
            {
                if (entity is null || entity.Symbol.IsEmpty())
                    continue;
                var ticker = entity.Symbol
                    .Trim()
                    .ToUpperInvariant();
                if (!seen.Add(ticker))
                    continue;
                var security = entity.ToSecurityMessage(
                    lookupMsg.TransactionId);
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
            if ((response.Data?.Length ?? 0) < 50 ||
                (response.Meta?.Found is long found &&
                    page * 50 >= found))
            {
                break;
            }
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

        var ticker = mdMsg.SecurityId.GetOptionalTicker();
        var target = checked((int)Math.Min(
            mdMsg.Count ??
                (long)NewsPageSize * MaxPages,
            (long)NewsPageSize * MaxPages));
        var perPage = Math.Min(
            NewsPageSize,
            Math.Max(1, target));
        var collected = new List<(
            MarketauxArticle Article,
            DateTime Time)>();
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        for (var page = 1;
            page <= MaxPages && collected.Count < target;
            page++)
        {
            var response = await SafeClient().GetNews(
                ticker,
                page,
                perPage,
                mdMsg.From,
                mdMsg.To,
                SafeEntityTypes(),
                SafeCountries(),
                SafeLanguages(),
                MustHaveEntities,
                GroupSimilar,
                cancellationToken);
            foreach (var article in response.Data ?? [])
            {
                if (article is null ||
                    article.Title.IsEmpty() ||
                    !MarketauxExtensions.TryParseUtc(
                        article.PublishedAt, out var time) ||
                    (mdMsg.From is not null &&
                        time < mdMsg.From.Value) ||
                    (mdMsg.To is not null &&
                        time > mdMsg.To.Value))
                {
                    continue;
                }
                var key = article.Uuid
                    .IsEmpty(
                        $"{article.PublishedAt}:{article.Url}:" +
                        article.Title);
                if (seen.Add(key))
                    collected.Add((article, time));
            }
            if ((response.Data?.Length ?? 0) < perPage ||
                (response.Meta?.Found is long found &&
                    page * perPage >= found))
            {
                break;
            }
        }

        foreach (var item in collected
            .OrderByDescending(item => item.Time)
            .Take(target)
            .OrderBy(item => item.Time))
        {
            var itemTicker = ticker
                .IsEmpty(item.Article.Entities?
                    .FirstOrDefault()?.Symbol)
                ?.Trim()
                .ToUpperInvariant();
            var securityId = itemTicker.IsEmpty()
                ? (SecurityId?)null
                : mdMsg.SecurityId.Normalize(itemTicker);
            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = item.Time,
                    Id = item.Article.Uuid
                        .IsEmpty(
                            $"{item.Article.PublishedAt}:" +
                            item.Article.Url),
                    Headline = item.Article.Title,
                    Story = item.Article.Description
                        .IsEmpty(item.Article.Snippet),
                    Source = item.Article.Source,
                    Url = item.Article.Url,
                    Language = item.Article.Language,
                    SecurityId = securityId,
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async ValueTask OnDatasetSubscriptionAsync(
        MarketDataMessage mdMsg,
        MarketauxDataKinds kind,
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

        var ticker = mdMsg.SecurityId.GetOptionalTicker();
        var limit = checked((int)Math.Min(
            mdMsg.Count ?? DatasetLimit,
            DatasetLimit));
        var response = await SafeClient().GetDataset(
            kind,
            ticker,
            SentimentInterval,
            limit,
            mdMsg.From,
            mdMsg.To,
            SafeEntityTypes(),
            SafeCountries(),
            SafeLanguages(),
            MustHaveEntities,
            GroupSimilar,
            cancellationToken);
        await SendOutMessageAsync(
            new MarketauxDataMessage
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
}
