namespace StockSharp.StockDataOrg;

public partial class StockDataOrgMessageAdapter
{
    private readonly record struct ParsedBar(
        StockDataOrgBar Bar,
        StockDataOrgBarValue Value,
        DateTimeOffset Time);

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

        var requestedTypes = lookupMsg.GetSecurityTypes();
        var providerTypes = requestedTypes.ToProviderTypes();
        if (providerTypes.IsEmpty())
        {
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var rawNative = lookupMsg.SecurityId.Native as string;
        var symbol = rawNative
            .IsEmpty(lookupMsg.SecurityId.SecurityCode)
            ?.Trim();
        var search = symbol
            .IsEmpty(lookupMsg.Name)
            .IsEmpty(lookupMsg.ShortName)
            ?.Trim();
        var exactSymbol =
            !lookupMsg.SecurityId.SecurityCode.IsEmpty() ||
            !rawNative.IsEmpty();
        var remaining = lookupMsg.Count ?? 50;
        var skip = lookupMsg.Skip ?? 0;
        const int pageSize = 50;
        var page = checked((int)(skip / pageSize + 1));
        var innerSkip = checked((int)(skip % pageSize));

        for (var requests = 0;
            requests < MaxRequests && remaining > 0;
            requests++, page++)
        {
            var response = await SafeClient().SearchEntities(
                exactSymbol ? null : search,
                exactSymbol ? symbol : null,
                providerTypes,
                page,
                cancellationToken);
            foreach (var entity in response.Data.Skip(innerSkip))
            {
                innerSkip = 0;
                if (entity?.Symbol.IsEmpty() != false)
                    continue;
                var security = entity.ToSecurityMessage(
                    lookupMsg.TransactionId);
                if (!security.IsMatch(lookupMsg, requestedTypes))
                    continue;
                await SendOutMessageAsync(
                    security, cancellationToken);
                if (--remaining <= 0)
                    break;
            }

            if (response.Data.Length < pageSize ||
                response.Meta?.Returned < pageSize)
            {
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
        if (mdMsg.From is not null || mdMsg.To is not null)
        {
            throw new NotSupportedException(
                "StockData.org does not expose historical Level1 events.");
        }

        var symbol = mdMsg.SecurityId.GetSymbol();
        var securityId = mdMsg.SecurityId.Normalize(symbol);
        var response = await SafeClient().GetQuote(
            symbol,
            ExtendedHours,
            cancellationToken);
        var quote = response.Data.FirstOrDefault(value =>
            value?.Ticker.EqualsIgnoreCase(symbol) == true) ??
            response.Data.FirstOrDefault();
        if (quote is not null)
        {
            var zone = SafeQuoteTimeZone();
            var hasLastTime =
                StockDataOrgExtensions.TryParseQuoteTime(
                    quote.LastTradeTime,
                    zone,
                    out var lastTime);
            var hasPreviousTime =
                StockDataOrgExtensions.TryParseQuoteTime(
                    quote.PreviousCloseTime,
                    zone,
                    out var previousTime);
            var serverTime = hasLastTime
                ? lastTime
                : hasPreviousTime
                    ? previousTime
                    : DateTimeOffset.UtcNow;
            var message = new Level1ChangeMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                SecurityId = securityId,
                ServerTime = serverTime.UtcDateTime,
            }
            .TryAdd(Level1Fields.OpenPrice, Positive(quote.DayOpen))
            .TryAdd(Level1Fields.HighPrice, Positive(quote.DayHigh))
            .TryAdd(Level1Fields.LowPrice, Positive(quote.DayLow))
            .TryAdd(Level1Fields.LastTradePrice, Positive(quote.Price))
            .TryAdd(
                Level1Fields.LastTradeTime,
                hasLastTime && Positive(quote.Price) is not null
                    ? (DateTime?)lastTime.UtcDateTime
                    : null)
            .TryAdd(
                Level1Fields.SettlementPrice,
                Positive(quote.PreviousClose))
            .TryAdd(Level1Fields.Volume, NonNegative(quote.Volume))
            .TryAdd(
                Level1Fields.HighPrice52Week,
                Positive(quote.YearHigh))
            .TryAdd(
                Level1Fields.LowPrice52Week,
                Positive(quote.YearLow))
            .TryAdd(Level1Fields.Change, quote.DayChange);
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
        var (interval, intraday) =
            timeFrame.ToStockDataInterval();
        var symbol = mdMsg.SecurityId.GetSymbol();
        var securityId = mdMsg.SecurityId.Normalize(symbol);
        var to = (mdMsg.To ?? DateTimeOffset.UtcNow)
            .ToUniversalTime();
        var from = (mdMsg.From ??
            StockDataOrgExtensions.EstimateFrom(
                to, timeFrame, mdMsg.Count))
            .ToUniversalTime();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From),
                from,
                "The candle-history start time is after its end time.");
        }

        var values = new List<ParsedBar>();
        var seen = new HashSet<DateTimeOffset>();
        var cursor = from;
        var requests = 0;
        var completed = false;
        while (cursor <= to && requests < MaxRequests)
        {
            var chunkTo = intraday
                ? cursor.AddDays(interval == "minute" ? 6 : 179)
                : to;
            if (chunkTo > to)
                chunkTo = to;
            var response = await SafeClient().GetBars(
                symbol,
                interval,
                intraday,
                AdjustedIntraday,
                ExtendedHours,
                cursor,
                chunkTo,
                cancellationToken);
            requests++;

            foreach (var bar in response.Data)
            {
                if (bar is null ||
                    !StockDataOrgExtensions.TryParseUtc(
                        bar.Date, out var time) ||
                    time < from ||
                    time > to ||
                    !seen.Add(time))
                {
                    continue;
                }
                var value = bar.GetValue();
                if (value.Open is null ||
                    value.High is null ||
                    value.Low is null ||
                    value.Close is null)
                {
                    continue;
                }
                values.Add(new ParsedBar(bar, value, time));
            }

            if (!intraday || chunkTo >= to)
            {
                completed = true;
                break;
            }
            cursor = new DateTimeOffset(
                chunkTo.UtcDateTime.Date.AddDays(1),
                TimeSpan.Zero);
        }
        if (!completed &&
            cursor <= to &&
            requests >= MaxRequests)
        {
            throw new InvalidOperationException(
                "StockData.org candle request exceeded the configured maximum requests.");
        }

        var target = mdMsg.Count ?? long.MaxValue;
        foreach (var item in values
            .OrderBy(value => value.Time)
            .Take(checked((int)Math.Min(target, int.MaxValue))))
        {
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = securityId,
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = item.Time.UtcDateTime,
                    CloseTime = item.Time.Add(timeFrame).UtcDateTime,
                    OpenPrice = item.Value.Open.Value,
                    HighPrice = item.Value.High.Value,
                    LowPrice = item.Value.Low.Value,
                    ClosePrice = item.Value.Close.Value,
                    TotalVolume =
                        NonNegative(item.Value.Volume) ?? 0,
                    State = CandleStates.Finished,
                },
                cancellationToken);
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

        var rawSymbol = (mdMsg.SecurityId.Native as string)
            .IsEmpty(mdMsg.SecurityId.SecurityCode);
        var symbol = rawSymbol.IsEmpty()
            ? null
            : mdMsg.SecurityId.GetSymbol();
        var requestedId = symbol.IsEmpty()
            ? default
            : mdMsg.SecurityId.Normalize(symbol);
        var capacity = checked(MaxRequests * NewsPageSize);
        var target = checked((int)Math.Min(
            mdMsg.Count ?? NewsPageSize,
            capacity));
        var pageSize = Math.Min(NewsPageSize, target);
        var values = new List<(
            StockDataOrgArticle Article,
            DateTimeOffset Time)>();
        var ids = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        for (var page = 1;
            page <= MaxRequests && values.Count < target;
            page++)
        {
            var response = await SafeClient().GetNews(
                symbol,
                NewsLanguage,
                mdMsg.From,
                mdMsg.To,
                page,
                pageSize,
                cancellationToken);
            foreach (var article in response.Data)
            {
                if (article is null ||
                    !StockDataOrgExtensions.TryParseUtc(
                        article.PublishedAt, out var time) ||
                    (mdMsg.From is not null &&
                        time < mdMsg.From) ||
                    (mdMsg.To is not null &&
                        time > mdMsg.To))
                {
                    continue;
                }
                var id = article.Uuid
                    .IsEmpty(article.Url);
                if (id.IsEmpty() || !ids.Add(id))
                    continue;
                values.Add((article, time));
                if (values.Count >= target)
                    break;
            }
            if (response.Data.Length < pageSize ||
                response.Meta?.Returned < pageSize)
            {
                break;
            }
        }

        foreach (var value in values
            .OrderBy(value => value.Time)
            .Take(target))
        {
            var securityId = requestedId;
            if (symbol.IsEmpty())
            {
                var entity = value.Article.Entities
                    .FirstOrDefault(item =>
                        item?.Symbol.IsEmpty() == false &&
                        item.Type.ToSecurityType() is
                            SecurityTypes.Stock or
                            SecurityTypes.Index or
                            SecurityTypes.Etf or
                            SecurityTypes.Fund);
                if (entity is not null)
                {
                    securityId = new SecurityId
                    {
                        SecurityCode = entity.Symbol
                            .Trim()
                            .ToUpperInvariant(),
                        BoardCode = entity.Exchange
                            .IsEmpty(StockDataOrgExtensions.DefaultBoard)
                            .Trim()
                            .ToUpperInvariant()
                            .Replace(' ', '_'),
                        Native = entity.Symbol
                            .Trim()
                            .ToUpperInvariant(),
                    };
                }
            }

            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = value.Time.UtcDateTime,
                    Id = value.Article.Uuid
                        .IsEmpty(value.Article.Url),
                    Headline = value.Article.Title,
                    Story = value.Article.Description
                        .IsEmpty(value.Article.Snippet),
                    Source = value.Article.Source,
                    Url = value.Article.Url,
                    SecurityId = securityId,
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

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;

    private static decimal? NonNegative(decimal? value)
        => value is >= 0 ? value : null;
}
