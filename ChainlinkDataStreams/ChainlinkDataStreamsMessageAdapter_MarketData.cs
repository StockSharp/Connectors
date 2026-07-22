namespace StockSharp.ChainlinkDataStreams;

public partial class ChainlinkDataStreamsMessageAdapter
{
    private readonly record struct HistoryRange(DateTime From, DateTime To,
        int Limit);

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
        if (message.Count is <= 0)
        {
            await SendSubscriptionResultAsync(message, cancellationToken);
            return;
        }

        var value = (message.SecurityId.Native as string)
            .IsEmpty(message.SecurityId.SecurityCode).IsEmpty(message.Name)?.Trim();
        var securityTypes = message.GetSecurityTypes();
        var skip = Math.Max(0L, message.Skip ?? 0);
        var left = Math.Max(0L,
            Math.Min(message.Count ?? MaximumFeeds, MaximumFeeds));
        foreach (var feed in GetFeeds()
            .Where(feed => Matches(feed, value))
            .OrderBy(static feed => feed.FeedId, StringComparer.OrdinalIgnoreCase))
        {
            var security = ToSecurityMessage(feed, message.TransactionId);
            if (!security.IsMatch(message, securityTypes))
                continue;
            if (skip > 0)
            {
                skip--;
                continue;
            }
            if (left <= 0)
                break;
            await SendOutMessageAsync(security, cancellationToken);
            left--;
        }
        await SendSubscriptionResultAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
        if (!message.IsSubscribe)
        {
            await DisposeSubscriptionAsync(message.OriginalTransactionId);
            await SendSubscriptionResultAsync(message, cancellationToken);
            return;
        }
        if (message.Count is <= 0)
        {
            await FinishAsync(message, cancellationToken);
            return;
        }

        var feed = ResolveFeed(message.SecurityId);
        var securityId = ToSecurityId(feed);
        var remaining = message.Count;
        DateTime? lastObservationTime = null;
        var lastUpdateKey = string.Empty;

        if (message.From is not null || message.To is not null ||
            message.IsHistoryOnly())
        {
            var range = GetRange(message);
            var reports = await GetHistoryAsync(feed, range, cancellationToken);
            IEnumerable<ChainlinkDecodedReport> selected = reports;
            selected = message.From is null
                ? selected.TakeLast(range.Limit)
                : selected.Take(range.Limit);
            foreach (var report in selected)
            {
                await SendOutMessageAsync(ToLevel1(report, message.TransactionId,
                    securityId), cancellationToken);
                lastObservationTime = report.ObservationTime;
                lastUpdateKey = report.UpdateKey;
                if (remaining is > 0 && --remaining == 0)
                    break;
            }
        }

        if (message.IsHistoryOnly() || message.To is not null || remaining == 0)
        {
            await FinishAsync(message, cancellationToken);
            return;
        }

        var snapshotEnvelope = await SafeRest().GetLatestReportAsync(feed.FeedId,
            cancellationToken);
        var snapshot = ChainlinkReportDecoder.Decode(snapshotEnvelope);
        if (!snapshot.FeedId.EqualsIgnoreCase(feed.FeedId))
            throw new InvalidDataException(
                "Chainlink latest report has an unexpected feed ID.");
        if (lastObservationTime is null ||
            snapshot.ObservationTime > lastObservationTime)
        {
            await SendOutMessageAsync(ToLevel1(snapshot, message.TransactionId,
                securityId), cancellationToken);
            lastObservationTime = snapshot.ObservationTime;
            lastUpdateKey = snapshot.UpdateKey;
            if (remaining is > 0)
                remaining--;
        }
        if (remaining == 0)
        {
            await FinishAsync(message, cancellationToken);
            return;
        }

        var origins = IsHighAvailability ? _origins : [];
        var pool = new ChainlinkStreamPool(message.TransactionId, feed.FeedId,
            WebSocketEndpoint, origins, Key, Secret,
            Math.Max(1, ReConnectionSettings.ReAttemptCount))
        { Parent = this };
        AttachPool(pool);
        var subscription = new LiveSubscription
        {
            TransactionId = message.TransactionId,
            SecurityId = securityId,
            Feed = feed,
            Pool = pool,
            Remaining = remaining,
            LastObservationTime = lastObservationTime,
            LastUpdateKey = lastUpdateKey,
        };
        AddLiveSubscription(subscription);
        try
        {
            await pool.ConnectAsync(cancellationToken);
        }
        catch
        {
            RemoveLiveSubscription(message.TransactionId, false);
            DetachPool(pool);
            pool.Dispose();
            throw;
        }
        await SendSubscriptionResultAsync(message, cancellationToken);
    }

    private async ValueTask<List<ChainlinkDecodedReport>>
        GetHistoryAsync(ChainlinkFeedInfo feed, HistoryRange range,
        CancellationToken cancellationToken)
    {
        var result = new List<ChainlinkDecodedReport>();
        var timestamps = new HashSet<DateTime>();
        var nextTimestamp = checked((long)range.From.ToUnix());
        var endTimestamp = checked((long)range.To.ToUnix());
        while (nextTimestamp <= endTimestamp && result.Count < range.Limit)
        {
            var pageLimit = Math.Min(ReportsPerPage, range.Limit - result.Count);
            var envelopes = await SafeRest().GetReportPageAsync(feed.FeedId,
                nextTimestamp, pageLimit, cancellationToken);
            if (envelopes.Length == 0)
                break;

            var followingTimestamp = nextTimestamp;
            foreach (var envelope in envelopes)
            {
                if (envelope is null)
                    throw new InvalidDataException(
                        "Chainlink history contains an empty report.");
                var report = ChainlinkReportDecoder.Decode(envelope);
                if (!report.FeedId.EqualsIgnoreCase(feed.FeedId))
                    throw new InvalidDataException(
                        "Chainlink history contains an unexpected feed ID.");
                followingTimestamp = Math.Max(followingTimestamp,
                    checked((long)report.ObservationTime.ToUnix()) + 1);
                if (report.ObservationTime < range.From ||
                    report.ObservationTime > range.To ||
                    !timestamps.Add(report.ObservationTime))
                    continue;
                result.Add(report);
                if (result.Count >= range.Limit)
                    break;
            }
            if (followingTimestamp <= nextTimestamp)
                throw new InvalidDataException(
                    "Chainlink history pagination did not advance.");
            nextTimestamp = followingTimestamp;
        }
        result.Sort(static (left, right) =>
            left.ObservationTime.CompareTo(right.ObservationTime));
        return result;
    }

    private HistoryRange GetRange(MarketDataMessage message)
    {
        var limit = checked((int)Math.Min(message.Count ?? HistoryLimit,
            HistoryLimit).Max(1));
        var now = CurrentTime.EnsureUtc();
        var to = (message.To ?? now).EnsureUtc();
        if (to > now)
            to = now;
        if (to <= DateTime.UnixEpoch)
            throw new ArgumentOutOfRangeException(nameof(message), to,
                "Chainlink history end must be after the Unix epoch.");
        var from = message.From is DateTime requestedFrom
            ? requestedFrom.EnsureUtc()
            : SubtractClamped(to, HistoryLookback);
        if (from < DateTime.UnixEpoch)
            from = DateTime.UnixEpoch;
        if (from >= to)
            throw new ArgumentOutOfRangeException(nameof(message), from,
                "Chainlink history start must be earlier than its end.");
        return new(from, to, limit);
    }

    private static Level1ChangeMessage ToLevel1(ChainlinkDecodedReport report,
        long transactionId, SecurityId securityId)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.BestBidPrice is decimal bid &&
            report.BestAskPrice is decimal ask && bid > ask)
            throw new InvalidDataException(
                "Chainlink report contains a crossed best-price pair.");
        if (report.BestBidVolume is < 0 || report.BestAskVolume is < 0)
            throw new InvalidDataException(
                "Chainlink report contains negative best-price volume.");

        var isDedicatedTrade = (report.Schema is
            ChainlinkReportSchemas.RwaAdvanced or
            ChainlinkReportSchemas.BestPrices) && report.LastTradePrice is not null;
        var lastPrice = report.LastTradePrice ?? report.PrimaryPrice;
        DateTime? lastTime = lastPrice is null || isDedicatedTrade
            ? null
            : report.ValueTime ?? report.ObservationTime;
        return new Level1ChangeMessage
        {
            OriginalTransactionId = transactionId,
            SecurityId = securityId,
            ServerTime = report.ObservationTime,
        }
        .TryAdd(Level1Fields.LastTradePrice, lastPrice)
        .TryAdd(Level1Fields.LastTradeTime, lastTime)
        .TryAdd(Level1Fields.TheorPrice,
            isDedicatedTrade ? report.PrimaryPrice : null)
        .TryAdd(Level1Fields.BestBidPrice, report.BestBidPrice)
        .TryAdd(Level1Fields.BestBidVolume, report.BestBidVolume)
        .TryAdd(Level1Fields.BestAskPrice, report.BestAskPrice)
        .TryAdd(Level1Fields.BestAskVolume, report.BestAskVolume)
        .TryAdd(Level1Fields.State, report.ToSecurityState());
    }

    private static bool Matches(ChainlinkFeedInfo feed, string value)
    {
        if (value.IsEmpty())
            return true;
        return feed.FeedId.ContainsIgnoreCase(value) ||
            feed.Schema.GetSchemaName().ContainsIgnoreCase(value) ||
            ((ushort)feed.Schema).ToString(CultureInfo.InvariantCulture)
                .EqualsIgnoreCase(value.TrimStart('v', 'V'));
    }

    private static SecurityId ToSecurityId(ChainlinkFeedInfo feed)
        => new()
        {
            SecurityCode = feed.FeedId,
            BoardCode = BoardCodes.ChainlinkDataStreams,
            Native = feed.FeedId,
        };

    private static SecurityMessage ToSecurityMessage(ChainlinkFeedInfo feed,
        long originalTransactionId)
        => new()
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = ToSecurityId(feed),
            Name = $"Chainlink {feed.Schema.GetSchemaName()} " +
                feed.FeedId[^8..],
            ShortName = "v" + ((ushort)feed.Schema).ToString(
                CultureInfo.InvariantCulture),
            Class = feed.Schema.GetSchemaName().ToUpperInvariant(),
            SecurityType = feed.Schema.ToSecurityType(),
            PriceStep = 0.000000000000000001m,
            Decimals = 18,
        };

    private static DateTime SubtractClamped(DateTime value, TimeSpan interval)
    {
        value = value.EnsureUtc();
        var ticks = Math.Max(DateTime.MinValue.Ticks, value.Ticks - interval.Ticks);
        return new(ticks, DateTimeKind.Utc);
    }

    private async ValueTask FinishAsync(MarketDataMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
