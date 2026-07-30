namespace StockSharp.TradingEconomics;

public partial class TradingEconomicsMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask MarketDataAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        if (TradingEconomicsDataTypes.TryGetKind(
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

        var native = lookupMsg.SecurityId.Native as string;
        var code = lookupMsg.SecurityId.SecurityCode?.Trim();
        var exact = !native.IsEmpty()
            ? native
            : code?.Contains(':') == true
                ? code
                : null;
        IReadOnlyList<TradingEconomicsMarket> markets;
        if (!exact.IsEmpty())
        {
            markets = await SafeClient().GetQuote(
                exact, cancellationToken);
        }
        else
        {
            var term = lookupMsg.SecurityId.Isin
                .IsEmpty(code)
                .IsEmpty(lookupMsg.Name)
                .IsEmpty(lookupMsg.ShortName)
                .IsEmpty(DefaultSearch);
            markets = await SafeClient().Search(
                term, cancellationToken);
        }

        var skip = lookupMsg.Skip ?? 0;
        var remaining = lookupMsg.Count ?? long.MaxValue;
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var market in markets ?? [])
        {
            if (market is null ||
                market.Symbol.IsEmpty() ||
                !IsSupportedMarket(market.Type))
            {
                continue;
            }
            var symbol = TradingEconomicsExtensions.NormalizeSymbol(
                market.Symbol);
            if (!seen.Add(symbol))
                continue;
            var security = market.ToSecurityMessage(
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
                "Trading Economics does not expose historical Level1 events.");
        }

        var symbol = mdMsg.SecurityId.GetSymbol(DefaultMarket);
        var quotes = await SafeClient().GetQuote(
            symbol, cancellationToken);
        var quote = quotes?.FirstOrDefault(item =>
            item is not null &&
            item.Symbol.Equals(
                symbol,
                StringComparison.OrdinalIgnoreCase)) ??
            quotes?.FirstOrDefault(item => item is not null);
        if (quote is not null)
        {
            var time = GetQuoteTime(quote);
            var message = new Level1ChangeMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                SecurityId = mdMsg.SecurityId.Normalize(symbol),
                ServerTime = time,
            }
            .TryAdd(
                Level1Fields.LastTradePrice,
                Positive(quote.Last))
            .TryAdd(
                Level1Fields.LastTradeTime,
                Positive(quote.Last) is not null
                    ? (DateTime?)time
                    : null)
            .TryAdd(
                Level1Fields.HighPrice,
                Positive(quote.DayHigh))
            .TryAdd(
                Level1Fields.LowPrice,
                Positive(quote.DayLow))
            .TryAdd(
                Level1Fields.ClosePrice,
                Positive(quote.Yesterday) ??
                    Positive(quote.Close))
            .TryAdd(
                Level1Fields.Change,
                quote.DailyPercentualChange);
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
        var (_, isDaily) =
            timeFrame.ToTradingEconomicsInterval();
        var symbol = mdMsg.SecurityId.GetSymbol(DefaultMarket);
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUtcSafe();
        var from = (mdMsg.From ??
            TradingEconomicsExtensions.EstimateFrom(
                to, timeFrame, mdMsg.Count))
            .ToUtcSafe();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From),
                from,
                "The Trading Economics history start time is after its end time.");
        }

        var bars = await SafeClient().GetBars(
            symbol,
            timeFrame,
            from,
            to,
            cancellationToken);
        var remaining = mdMsg.Count ?? long.MaxValue;
        var seen = new HashSet<DateTime>();

        foreach (var item in (bars ?? [])
            .Where(item => item is not null)
            .Select(item => new
            {
                Value = item,
                Parsed = TradingEconomicsExtensions.TryParseUtc(
                    item.Date, isDaily, out var time),
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
                    SecurityId = mdMsg.SecurityId.Normalize(symbol),
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = item.Time,
                    CloseTime = item.Time.Add(timeFrame),
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
                "The Trading Economics news start time is after its end time.");
        }

        var rawSymbol = (mdMsg.SecurityId.Native as string)
            .IsEmpty(mdMsg.SecurityId.SecurityCode);
        var symbol = rawSymbol.IsEmpty()
            ? null
            : mdMsg.SecurityId.GetSymbol(DefaultMarket);
        var articles = await SafeClient().GetNews(
            symbol, cancellationToken);
        var target = checked((int)Math.Min(
            mdMsg.Count ?? NewsLimit,
            NewsLimit));

        foreach (var item in (articles ?? [])
            .Where(item =>
                item is not null &&
                TradingEconomicsExtensions.TryParseUtc(
                    item.Date, false, out _))
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
            var itemSymbol = symbol
                .IsEmpty(item.Value.Symbol)
                ?.Trim()
                .ToUpperInvariant();
            var securityId = itemSymbol.IsEmpty()
                ? default
                : symbol.IsEmpty()
                    ? new SecurityId
                    {
                        SecurityCode =
                            TradingEconomicsExtensions.GetTicker(
                                itemSymbol),
                        BoardCode =
                            TradingEconomicsExtensions.DefaultBoard,
                        Native = itemSymbol,
                    }
                    : mdMsg.SecurityId.Normalize(itemSymbol);
            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = item.Time,
                    Id = item.Value.Id.IsEmpty(
                        $"{itemSymbol}:{item.Value.Date}:{item.Value.Title}"),
                    Headline = item.Value.Title,
                    Story = item.Value.Description,
                    Source = "Trading Economics",
                    Url = NormalizeNewsUrl(item.Value.Url),
                    SecurityId = securityId,
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async ValueTask OnDatasetSubscriptionAsync(
        MarketDataMessage mdMsg,
        TradingEconomicsDataKinds kind,
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
                "The Trading Economics dataset start time is after its end time.");
        }

        var symbol = mdMsg.SecurityId.GetSymbol(DefaultMarket);
        var response = await SafeClient().GetDataset(
            kind,
            symbol,
            mdMsg.From,
            mdMsg.To,
            cancellationToken);
        await SendOutMessageAsync(
            new TradingEconomicsDataMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                Dataset = kind,
                SecurityId = mdMsg.SecurityId.Normalize(symbol),
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

    private static bool IsSupportedMarket(string type)
        => type?.Trim().ToLowerInvariant() is
            "stocks" or "stock" or
            "index" or "indexes" or
            "fund" or "funds" or
            "etf" or "etfs";

    private static DateTime GetQuoteTime(
        TradingEconomicsMarket quote)
    {
        if (TradingEconomicsExtensions.TryParseUtc(
            quote.LastUpdate, false, out var time) ||
            TradingEconomicsExtensions.TryParseUtc(
                quote.Date, false, out time))
        {
            return time;
        }
        return DateTime.UtcNow;
    }

    private static DateTime ParseUtc(string value)
    {
        TradingEconomicsExtensions.TryParseUtc(
            value, false, out var result);
        return result;
    }

    private static string NormalizeNewsUrl(string value)
    {
        if (value.IsEmpty())
            return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttps ||
                absolute.Scheme == Uri.UriSchemeHttp))
        {
            return absolute.AbsoluteUri;
        }
        return new Uri(
            new Uri("https://tradingeconomics.com/"),
            value.TrimStart('/')).AbsoluteUri;
    }

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;

    private static decimal? NonNegative(decimal? value)
        => value is >= 0 ? value : null;
}
