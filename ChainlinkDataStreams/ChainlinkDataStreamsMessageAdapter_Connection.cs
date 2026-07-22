namespace StockSharp.ChainlinkDataStreams;

public partial class ChainlinkDataStreamsMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage message,
        CancellationToken cancellationToken)
    {
        if (_rest is not null)
            throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
        if (Key.IsEmpty())
            throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);
        if (Secret.IsEmpty())
            throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);

        var rest = new ChainlinkRestClient(RestEndpoint, Key, Secret,
            RequestInterval)
        { Parent = this };
        _rest = rest;
        try
        {
            CacheFeeds(await rest.GetFeedsAsync(cancellationToken));
            if (GetFeeds().Length == 0)
                throw new InvalidDataException(
                    "Chainlink returned no supported entitled feeds.");

            _origins = [];
            if (IsHighAvailability)
            {
                try
                {
                    _origins = await rest.GetAvailableOriginsAsync(
                        WebSocketEndpoint, cancellationToken);
                }
                catch (Exception error) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    this.AddWarningLog(
                        "Chainlink origin discovery failed; using the primary WebSocket endpoint: {0}",
                        error.Message);
                }
            }
            await base.ConnectAsync(message, cancellationToken);
        }
        catch
        {
            await DisposeClientsAsync();
            ClearState();
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(DisconnectMessage message,
        CancellationToken cancellationToken)
    {
        if (_rest is null)
            throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
        await DisposeClientsAsync();
        ClearState();
        await base.DisconnectAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(ResetMessage message,
        CancellationToken cancellationToken)
    {
        await DisposeClientsAsync();
        ClearState();
        await base.ResetAsync(message, cancellationToken);
    }

    private ChainlinkRestClient SafeRest()
        => _rest ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void CacheFeeds(IEnumerable<ChainlinkFeed> feeds)
    {
        var values = (feeds ?? []).ToArray();
        var unsupported = 0;
        using (_sync.EnterScope())
        {
            _feeds.Clear();
            foreach (var value in values.Take(MaximumFeeds))
            {
                if (value?.FeedId.IsEmpty() != false)
                {
                    unsupported++;
                    continue;
                }
                try
                {
                    var feed = value.FeedId.ParseFeed();
                    _feeds[feed.FeedId] = feed;
                }
                catch (Exception error) when (error is FormatException or
                    NotSupportedException)
                {
                    unsupported++;
                }
            }
        }
        if (unsupported > 0)
            this.AddWarningLog(
                "Skipped {0} malformed or unsupported Chainlink feed IDs.",
                unsupported);
        if (values.Length > MaximumFeeds)
            this.AddWarningLog(
                "Cached the first {0} of {1} entitled Chainlink feeds.",
                MaximumFeeds, values.Length);
    }

    private ChainlinkFeedInfo[] GetFeeds()
    {
        using (_sync.EnterScope())
            return [.. _feeds.Values];
    }

    private ChainlinkFeedInfo ResolveFeed(SecurityId securityId)
    {
        var value = (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)?.Trim();
        if (value.IsEmpty())
            throw new ArgumentException("Chainlink feed ID is not specified.",
                nameof(securityId));
        var feed = value.ParseFeed();
        using (_sync.EnterScope())
        {
            if (_feeds.TryGetValue(feed.FeedId, out var entitled))
                return entitled;
        }
        throw new InvalidOperationException(
            $"Chainlink feed '{feed.FeedId}' is not available to the configured API key.");
    }

    private void AddLiveSubscription(LiveSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        using (_sync.EnterScope())
        {
            if (!_liveSubscriptions.TryAdd(subscription.TransactionId, subscription))
                throw new InvalidOperationException(
                    $"Chainlink subscription {subscription.TransactionId} already exists.");
        }
    }

    private LiveSubscription RemoveLiveSubscription(long transactionId,
        bool isRetired)
    {
        LiveSubscription subscription;
        using (_sync.EnterScope())
        {
            if (!_liveSubscriptions.Remove(transactionId, out subscription))
                return null;
            if (isRetired)
                _retiredPools.Add(subscription.Pool);
        }
        if (isRetired)
            subscription.Pool.Stop();
        return subscription;
    }

    private async ValueTask DisposeSubscriptionAsync(long transactionId)
    {
        var subscription = RemoveLiveSubscription(transactionId, false);
        if (subscription is null)
            return;
        DetachPool(subscription.Pool);
        try
        {
            await subscription.Pool.DisconnectAsync();
        }
        finally
        {
            subscription.Pool.Dispose();
        }
    }

    private async ValueTask DisposeClientsAsync()
    {
        ChainlinkStreamPool[] pools;
        using (_sync.EnterScope())
        {
            pools = _liveSubscriptions.Values.Select(static item => item.Pool)
                .Concat(_retiredPools).Distinct().ToArray();
            _liveSubscriptions.Clear();
            _retiredPools.Clear();
        }
        foreach (var pool in pools)
            pool.Stop();
        foreach (var pool in pools)
        {
            DetachPool(pool);
            try
            {
                await pool.DisconnectAsync();
            }
            finally
            {
                pool.Dispose();
            }
        }
        _rest?.Dispose();
        _rest = null;
    }

    private void ClearState()
    {
        using (_sync.EnterScope())
        {
            _feeds.Clear();
            _liveSubscriptions.Clear();
            _retiredPools.Clear();
            _origins = [];
        }
    }

    private void AttachPool(ChainlinkStreamPool pool)
    {
        pool.ReportReceived += OnStreamReportAsync;
        pool.Error += OnStreamErrorAsync;
    }

    private void DetachPool(ChainlinkStreamPool pool)
    {
        pool.ReportReceived -= OnStreamReportAsync;
        pool.Error -= OnStreamErrorAsync;
    }

    private async ValueTask OnStreamReportAsync(ChainlinkStreamPool pool,
        ChainlinkReportEnvelope envelope, CancellationToken cancellationToken)
    {
        var report = ChainlinkReportDecoder.Decode(envelope);
        LiveSubscription subscription;
        var isFinished = false;
        using (_sync.EnterScope())
        {
            if (!_liveSubscriptions.TryGetValue(pool.TransactionId,
                out subscription) || !ReferenceEquals(subscription.Pool, pool))
                return;
            if (!subscription.Feed.FeedId.EqualsIgnoreCase(report.FeedId))
                throw new InvalidDataException(
                    "Chainlink stream returned an unexpected feed ID.");
            if ((subscription.LastObservationTime is DateTime lastObservation &&
                report.ObservationTime <= lastObservation) ||
                subscription.LastUpdateKey.EqualsIgnoreCase(report.UpdateKey))
                return;
            subscription.LastObservationTime = report.ObservationTime;
            subscription.LastUpdateKey = report.UpdateKey;
            if (subscription.Remaining is > 0 && --subscription.Remaining == 0)
            {
                _liveSubscriptions.Remove(subscription.TransactionId);
                _retiredPools.Add(pool);
                isFinished = true;
            }
        }

        await SendOutMessageAsync(ToLevel1(report, subscription.TransactionId,
            subscription.SecurityId), cancellationToken);
        if (isFinished)
        {
            pool.Stop();
            await SendSubscriptionFinishedAsync(subscription.TransactionId,
                cancellationToken);
        }
    }

    private async ValueTask OnStreamErrorAsync(ChainlinkStreamPool pool,
        Exception error, bool isTerminal, CancellationToken cancellationToken)
    {
        if (!isTerminal)
        {
            this.AddWarningLog("Chainlink WebSocket reconnect: {0}", error.Message);
            return;
        }
        var subscription = RemoveLiveSubscription(pool.TransactionId, true);
        if (subscription is null)
            return;
        await SendOutErrorAsync(error, cancellationToken);
        await SendSubscriptionFinishedAsync(subscription.TransactionId,
            cancellationToken);
    }
}
