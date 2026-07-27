namespace StockSharp.UnusualWhales;

public partial class UnusualWhalesMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask MarketDataAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        if (UnusualWhalesDataTypes.TryGetKind(
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

        var exact = (lookupMsg.SecurityId.Native as string)
            .IsEmpty(lookupMsg.SecurityId.SecurityCode)
            ?.Trim();
        if (!exact.IsEmpty())
        {
            var profile = await SafeClient().GetCompanyProfile(
                exact, cancellationToken);
            if (profile is not null &&
                !profile.Ticker.IsEmpty())
            {
                var security = profile.ToSecurityMessage(
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
        var data = await SafeClient().GetListings(
            cancellationToken);
        var skip = lookupMsg.Skip ?? 0;
        var remaining = lookupMsg.Count ?? long.MaxValue;
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var listing in data?.Listings ?? [])
        {
            if (listing is null || listing.Ticker.IsEmpty())
                continue;
            var ticker = listing.Ticker
                .Trim()
                .ToUpperInvariant();
            if (!seen.Add(ticker) ||
                (!query.IsEmpty() &&
                    !ticker.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase) &&
                    !(listing.Name?.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase) ??
                        false)))
            {
                continue;
            }
            var security = listing.ToSecurityMessage(
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
                "Unusual Whales does not expose historical Level1 events.");
        }

        var ticker = mdMsg.SecurityId.GetTicker();
        var state = await SafeClient().GetStockState(
            ticker, cancellationToken);
        if (state is not null)
        {
            var time = UnusualWhalesExtensions.TryParseUtc(
                state.TapeTime, out var parsed)
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
                Positive(state.Close))
            .TryAdd(
                Level1Fields.LastTradeTime,
                Positive(state.Close) is not null
                    ? (DateTime?)time
                    : null)
            .TryAdd(
                Level1Fields.OpenPrice,
                Positive(state.Open))
            .TryAdd(
                Level1Fields.HighPrice,
                Positive(state.High))
            .TryAdd(
                Level1Fields.LowPrice,
                Positive(state.Low))
            .TryAdd(
                Level1Fields.ClosePrice,
                Positive(state.PreviousClose))
            .TryAdd(
                Level1Fields.Volume,
                NonNegative(
                    state.TotalVolume ?? state.Volume));
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
        var candleSize = timeFrame.ToCandleSize();
        var ticker = mdMsg.SecurityId.GetTicker();
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUtcSafe();
        var from = (mdMsg.From ??
            UnusualWhalesExtensions.EstimateFrom(
                to, timeFrame, mdMsg.Count))
            .ToUtcSafe();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From),
                from,
                "The OHLC-history start time is after its end time.");
        }
        var limit = checked((int)Math.Min(
            mdMsg.Count ?? CandleLimit,
            CandleLimit));
        var candles = await SafeClient().GetCandles(
            ticker,
            candleSize,
            from,
            to,
            limit,
            cancellationToken);
        var remaining = mdMsg.Count ?? long.MaxValue;
        var seen = new HashSet<DateTime>();
        foreach (var item in candles
            .Where(item => item is not null)
            .Select(item => new
            {
                Value = item,
                Parsed = TryGetCandleTime(
                    item, out var openTime),
                OpenTime = openTime,
            })
            .Where(item =>
                item.Parsed &&
                item.OpenTime >= from &&
                item.OpenTime <= to &&
                item.Value.Open is not null &&
                item.Value.High is not null &&
                item.Value.Low is not null &&
                item.Value.Close is not null)
            .OrderBy(item => item.OpenTime))
        {
            if (remaining <= 0)
                break;
            if (!seen.Add(item.OpenTime))
                continue;
            var closeTime =
                UnusualWhalesExtensions.TryParseUtc(
                    item.Value.EndTime,
                    out var parsedClose)
                    ? parsedClose
                    : item.OpenTime.GetCloseTime(timeFrame);
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId.Normalize(ticker),
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = item.OpenTime,
                    CloseTime = closeTime,
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
        var perPage = Math.Min(100, Math.Max(1, target));
        var collected = new List<(
            UnusualWhalesHeadline Headline,
            DateTime Time)>();
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        for (var page = 1;
            page <= MaxPages && collected.Count < target;
            page++)
        {
            var headlines = await SafeClient().GetNews(
                ticker,
                page,
                perPage,
                cancellationToken);
            foreach (var headline in headlines ?? [])
            {
                if (headline is null ||
                    headline.Headline.IsEmpty() ||
                    !UnusualWhalesExtensions.TryParseUtc(
                        headline.CreatedAt, out var time) ||
                    (mdMsg.From is not null &&
                        time < mdMsg.From.Value) ||
                    (mdMsg.To is not null &&
                        time > mdMsg.To.Value))
                {
                    continue;
                }
                var key =
                    $"{headline.Source}:{headline.CreatedAt}:" +
                    headline.Headline;
                if (seen.Add(key))
                    collected.Add((headline, time));
            }
            if ((headlines?.Count ?? 0) < perPage)
                break;
        }

        foreach (var item in collected
            .OrderByDescending(item => item.Time)
            .Take(target)
            .OrderBy(item => item.Time))
        {
            var itemTicker = ticker
                .IsEmpty(item.Headline.Tickers?
                    .FirstOrDefault())
                ?.Trim()
                .ToUpperInvariant();
            var securityId = itemTicker.IsEmpty()
                ? default
                : mdMsg.SecurityId.Normalize(itemTicker);
            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = item.Time,
                    Id =
                        $"{item.Headline.Source}:" +
                        $"{item.Headline.CreatedAt}:" +
                        item.Headline.Headline,
                    Headline = item.Headline.Headline,
                    Story = BuildNewsStory(item.Headline),
                    Source = item.Headline.Source,
                    SecurityId = securityId,
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async ValueTask OnDatasetSubscriptionAsync(
        MarketDataMessage mdMsg,
        UnusualWhalesDataKinds kind,
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
            limit,
            mdMsg.From,
            mdMsg.To,
            UnusualFlowOnly,
            OtmMarketTide,
            FiveMinuteMarketTide,
            cancellationToken);
        await SendOutMessageAsync(
            new UnusualWhalesDataMessage
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

    private static bool TryGetCandleTime(
        UnusualWhalesCandle candle,
        out DateTime result)
        => UnusualWhalesExtensions.TryParseUtc(
            candle.StartTime.IsEmpty(candle.Date),
            out result);

    private static string BuildNewsStory(
        UnusualWhalesHeadline headline)
    {
        var values = new List<string>();
        if (!headline.Sentiment.IsEmpty())
            values.Add($"Sentiment: {headline.Sentiment}.");
        if (headline.IsMajor == true)
            values.Add("Major market headline.");
        if (headline.Tags?.Length > 0)
            values.Add($"Tags: {string.Join(", ", headline.Tags)}.");
        return string.Join(" ", values);
    }

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;

    private static decimal? NonNegative(decimal? value)
        => value is >= 0 ? value : null;
}
