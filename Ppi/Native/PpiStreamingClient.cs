namespace StockSharp.Ppi.Native;

sealed class PpiStreamingClient : Disposable
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.DateTimeOffset,
        Culture = CultureInfo.InvariantCulture,
    };

    private readonly Uri _endpoint;
    private readonly PpiRestClient _rest;
    private readonly Func<PpiMarketUpdate, CancellationToken, ValueTask>
        _marketHandler;
    private readonly Func<PpiAccountUpdate, CancellationToken, ValueTask>
        _accountHandler;
    private readonly Func<Exception, CancellationToken, ValueTask>
        _errorHandler;
    private readonly SemaphoreSlim _marketSync = new(1, 1);
    private readonly SemaphoreSlim _accountSync = new(1, 1);
    private readonly SynchronizedDictionary<string, PpiInstrumentKey>
        _marketSubscriptions =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<string> _accountSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private HubConnection _market;
    private HubConnection _account;

    public PpiStreamingClient(
        Uri endpoint,
        PpiRestClient rest,
        Func<PpiMarketUpdate, CancellationToken, ValueTask> marketHandler,
        Func<PpiAccountUpdate, CancellationToken, ValueTask> accountHandler,
        Func<Exception, CancellationToken, ValueTask> errorHandler)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "PPI realtime endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(endpoint));
        }

        var address = endpoint.AbsoluteUri;
        if (!address.EndsWith('/'))
            address += "/";
        _endpoint = new(address);
        _rest = rest ?? throw new ArgumentNullException(nameof(rest));
        _marketHandler = marketHandler ??
            throw new ArgumentNullException(nameof(marketHandler));
        _accountHandler = accountHandler ??
            throw new ArgumentNullException(nameof(accountHandler));
        _errorHandler = errorHandler ??
            throw new ArgumentNullException(nameof(errorHandler));
    }

    public async Task SubscribeMarket(
        PpiInstrumentKey instrument,
        CancellationToken cancellationToken)
    {
        await _marketSync.WaitAsync(cancellationToken);
        try
        {
            _market ??= CreateMarketConnection();
            if (_market.State == HubConnectionState.Disconnected)
                await _market.StartAsync(cancellationToken);

            if (_marketSubscriptions.ContainsKey(instrument.SubscriptionKey))
                return;

            await SubscribeMarketCore(instrument, cancellationToken);
            _marketSubscriptions[instrument.SubscriptionKey] = instrument;
        }
        finally
        {
            _marketSync.Release();
        }
    }

    public async Task SubscribeAccount(
        string accountNumber,
        CancellationToken cancellationToken)
    {
        accountNumber.ThrowIfEmpty(nameof(accountNumber));
        await _accountSync.WaitAsync(cancellationToken);
        try
        {
            _account ??= CreateAccountConnection();
            if (_account.State == HubConnectionState.Disconnected)
                await _account.StartAsync(cancellationToken);

            if (_accountSubscriptions.Contains(accountNumber))
                return;

            await _account.InvokeCoreAsync(
                "AccountDataSubscribe",
                [accountNumber],
                cancellationToken);
            _accountSubscriptions.Add(accountNumber);
        }
        finally
        {
            _accountSync.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_market != null &&
            _market.State != HubConnectionState.Disconnected)
        {
            await _market.StopAsync(cancellationToken);
        }
        if (_account != null &&
            _account.State != HubConnectionState.Disconnected)
        {
            await _account.StopAsync(cancellationToken);
        }
    }

    private HubConnection CreateMarketConnection()
    {
        var connection = CreateConnection("MarketData");
        connection.On<string>(
            "marketdata",
            message => DispatchMarket(message));
        connection.On<string>(
            "scheduled-md-disconnection",
            message => DispatchScheduledDisconnect(message));
        connection.Reconnected += _ => ResubscribeMarket();
        connection.Closed += error => HandleClosed(error);
        return connection;
    }

    private HubConnection CreateAccountConnection()
    {
        var connection = CreateConnection("Account");
        connection.On<string>(
            "account",
            message => DispatchAccount(message));
        connection.On<string>(
            "scheduled-ac-disconnection",
            message => DispatchScheduledDisconnect(message));
        connection.Reconnected += _ => ResubscribeAccount();
        connection.Closed += error => HandleClosed(error);
        return connection;
    }

    private HubConnection CreateConnection(string hubName)
        => new HubConnectionBuilder()
            .WithUrl(
                new Uri(_endpoint, hubName),
                options => options.AccessTokenProvider = () =>
                    _rest.GetAccessToken(CancellationToken.None))
            .WithAutomaticReconnect(
                [
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                ])
            .Build();

    private Task DispatchMarket(string message)
    {
        if (message.IsEmpty())
            return Task.CompletedTask;
        try
        {
            var update = JsonConvert.DeserializeObject<PpiMarketUpdate>(
                message, _jsonSettings);
            return update is null
                ? Task.CompletedTask
                : _marketHandler(
                    update, CancellationToken.None).AsTask();
        }
        catch (JsonException error)
        {
            return _errorHandler(
                new InvalidDataException(
                    "PPI market-data stream returned invalid JSON.",
                    error),
                CancellationToken.None).AsTask();
        }
    }

    private Task DispatchAccount(string message)
    {
        if (message.IsEmpty())
            return Task.CompletedTask;
        try
        {
            var update = JsonConvert.DeserializeObject<PpiAccountUpdate>(
                message, _jsonSettings);
            return update is null
                ? Task.CompletedTask
                : _accountHandler(
                    update, CancellationToken.None).AsTask();
        }
        catch (JsonException error)
        {
            return _errorHandler(
                new InvalidDataException(
                    "PPI account stream returned invalid JSON.",
                    error),
                CancellationToken.None).AsTask();
        }
    }

    private Task DispatchScheduledDisconnect(string message)
        => _errorHandler(
            new IOException(
                "PPI scheduled a realtime disconnection" +
                (message.IsEmpty() ? "." : $": {message}")),
            CancellationToken.None).AsTask();

    private Task HandleClosed(Exception error)
        => error is null
            ? Task.CompletedTask
            : _errorHandler(error, CancellationToken.None).AsTask();

    private async Task ResubscribeMarket()
    {
        try
        {
            foreach (var instrument in _marketSubscriptions.Values)
                await SubscribeMarketCore(
                    instrument, CancellationToken.None);
        }
        catch (Exception error)
        {
            await _errorHandler(error, CancellationToken.None);
        }
    }

    private async Task ResubscribeAccount()
    {
        try
        {
            foreach (var accountNumber in _accountSubscriptions)
            {
                await _account.InvokeCoreAsync(
                    "AccountDataSubscribe",
                    [accountNumber],
                    CancellationToken.None);
            }
        }
        catch (Exception error)
        {
            await _errorHandler(error, CancellationToken.None);
        }
    }

    private Task SubscribeMarketCore(
        PpiInstrumentKey instrument,
        CancellationToken cancellationToken)
        => _market.InvokeCoreAsync(
            "MarketDataSubscribe",
            [
                new
                {
                    ticker = instrument.Ticker,
                    type = instrument.Type,
                    settlement = instrument.Settlement,
                },
            ],
            cancellationToken);

    protected override void DisposeManaged()
    {
        _market?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _account?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _marketSync.Dispose();
        _accountSync.Dispose();
        base.DisposeManaged();
    }
}
