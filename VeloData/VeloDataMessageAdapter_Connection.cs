namespace StockSharp.VeloData;

public partial class VeloDataMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage message,
        CancellationToken cancellationToken)
    {
        if (_rest is not null || _newsSocket is not null)
            throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
        if (Token.IsEmpty())
            throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);

        var rest = new VeloDataRestClient(ApiEndpoint, NewsEndpoint, Token,
            RequestInterval)
        { Parent = this };
        _rest = rest;
        try
        {
            CacheInstruments(await rest.GetInstrumentsAsync(IsIncludeDelisted,
                cancellationToken));
            if (GetInstruments().Length == 0)
                throw new InvalidDataException(
                    "Velo Data returned an empty instrument catalogue.");
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
        if (_rest is null && _newsSocket is null)
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

    private VeloDataRestClient SafeRest()
        => _rest ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void CacheInstruments(IEnumerable<VeloDataInstrument> instruments)
    {
        using (_sync.EnterScope())
        {
            _instruments.Clear();
            foreach (var instrument in instruments ?? [])
            {
                if (!IsValidInstrument(instrument))
                    continue;
                _instruments[instrument.Key] = instrument;
            }
        }
    }

    private VeloDataInstrument[] GetInstruments()
    {
        using (_sync.EnterScope())
            return [.. _instruments.Values];
    }

    private VeloDataInstrument ResolveInstrument(SecurityId securityId)
    {
        var native = (securityId.Native as string)?.Trim();
        var code = securityId.SecurityCode?.Trim();
        using (_sync.EnterScope())
        {
            if (!native.IsEmpty() && _instruments.TryGetValue(native, out var exact))
                return exact;
            if (code.IsEmpty())
                throw new ArgumentException(
                    "Velo Data security code is not specified.", nameof(securityId));
            var matches = _instruments.Values.Where(instrument =>
                instrument.Code.EqualsIgnoreCase(code) ||
                instrument.Key.EqualsIgnoreCase(code)).Take(2).ToArray();
            if (matches.Length == 1)
                return matches[0];
        }
        throw new InvalidOperationException(
            $"Velo Data instrument '{code}' is unknown or ambiguous. Use security lookup to preserve its catalogue identity.");
    }

    private async ValueTask EnsureNewsSocketAsync(
        CancellationToken cancellationToken)
    {
        await _streamGate.WaitAsync(cancellationToken);
        try
        {
            if (_newsSocket?.IsStopped == false)
                return;
            if (_newsSocket is not null)
            {
                DetachSocket(_newsSocket);
                _newsSocket.Dispose();
                _newsSocket = null;
            }

            var socket = new VeloDataNewsSocketClient(WebSocketEndpoint, Token,
                Math.Max(1, ReConnectionSettings.ReAttemptCount))
            { Parent = this };
            socket.NewsReceived += OnNewsReceivedAsync;
            socket.Error += OnNewsErrorAsync;
            _newsSocket = socket;
            try
            {
                await socket.ConnectAsync(cancellationToken);
            }
            catch
            {
                DetachSocket(socket);
                _newsSocket = null;
                socket.Dispose();
                throw;
            }
        }
        finally
        {
            _streamGate.Release();
        }
    }

    private async ValueTask DisposeClientsAsync()
    {
        var socket = _newsSocket;
        _newsSocket = null;
        if (socket is not null)
        {
            DetachSocket(socket);
            try
            {
                await socket.DisconnectAsync();
            }
            finally
            {
                socket.Dispose();
            }
        }
        _rest?.Dispose();
        _rest = null;
    }

    private void DetachSocket(VeloDataNewsSocketClient socket)
    {
        socket.NewsReceived -= OnNewsReceivedAsync;
        socket.Error -= OnNewsErrorAsync;
    }

    private async ValueTask OnNewsErrorAsync(Exception error, bool isTerminal,
        CancellationToken cancellationToken)
    {
        await SendOutErrorAsync(error, cancellationToken);
        if (!isTerminal)
            return;

        long[] finished;
        using (_sync.EnterScope())
        {
            finished = [.. _liveNews.Keys];
            _liveNews.Clear();
        }
        foreach (var transactionId in finished)
            await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
    }

    private async ValueTask OnNewsReceivedAsync(VeloDataNewsStory story,
        CancellationToken cancellationToken)
    {
        if (story?.IsDeleted == true)
            return;
        if (!TryGetNewsTime(story, out var time))
            return;

        LiveNewsSubscription[] subscriptions;
        var finished = new HashSet<long>();
        using (_sync.EnterScope())
        {
            subscriptions = _liveNews.Values.Where(subscription =>
                MatchesNews(story, subscription.Coin)).ToArray();
            foreach (var subscription in subscriptions)
            {
                if (subscription.Remaining is > 0 && --subscription.Remaining == 0)
                {
                    _liveNews.Remove(subscription.TransactionId);
                    finished.Add(subscription.TransactionId);
                }
            }
        }

        foreach (var subscription in subscriptions)
        {
            await SendNewsAsync(subscription.TransactionId,
                subscription.SecurityId, story, time, cancellationToken);
            if (finished.Contains(subscription.TransactionId))
                await SendSubscriptionFinishedAsync(subscription.TransactionId,
                    cancellationToken);
        }
    }

    private void AddLiveNews(LiveNewsSubscription subscription)
    {
        using (_sync.EnterScope())
        {
            if (!_liveNews.TryAdd(subscription.TransactionId, subscription))
                throw new InvalidOperationException(
                    $"Velo Data news subscription {subscription.TransactionId} already exists.");
        }
    }

    private void RemoveLiveNews(long transactionId)
    {
        using (_sync.EnterScope())
            _liveNews.Remove(transactionId);
    }

    private void ClearState()
    {
        using (_sync.EnterScope())
        {
            _instruments.Clear();
            _liveNews.Clear();
        }
    }

    private static bool IsValidInstrument(VeloDataInstrument instrument)
    {
        if (instrument is null || instrument.MarketType ==
            VeloDataMarketTypes.Unknown || instrument.Exchange.IsEmpty() ||
            instrument.Coin.IsEmpty() || instrument.Product.IsEmpty())
            return false;
        try
        {
            _ = VeloDataExtensions.NormalizeVeloIdentifier(instrument.Exchange,
                nameof(instrument.Exchange));
            _ = VeloDataExtensions.NormalizeVeloIdentifier(instrument.Coin,
                nameof(instrument.Coin));
            _ = VeloDataExtensions.NormalizeVeloIdentifier(instrument.Product,
                nameof(instrument.Product));
            _ = instrument.Begin;
            return true;
        }
        catch (Exception error) when (error is ArgumentException or
            InvalidDataException)
        {
            return false;
        }
    }
}
