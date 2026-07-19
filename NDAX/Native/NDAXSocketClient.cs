namespace StockSharp.NDAX.Native;

sealed class NDAXSocketClient : BaseLogReceiver
{
    private sealed class PendingRequest
    {
        public string Operation { get; init; }
        public TaskCompletionSource<string> Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly record struct SubscriptionKey(
        NdaxSubscriptionKinds Kind, int InstrumentId, int Parameter,
        long AccountId);

    private const int _maximumSubscriptions = 10;
    private const int _maximumMessageCharacters = 8 * 1024 * 1024;
    private readonly string _endpoint;
    private readonly WorkingTime _workingTime;
    private readonly int _reconnectAttempts;
    private readonly int _omsId;
    private readonly string _apiKey;
    private readonly byte[] _apiSecret;
    private readonly long _userId;
    private readonly Lock _sync = new();
    private readonly Dictionary<long, PendingRequest> _pending = [];
    private readonly HashSet<SubscriptionKey> _subscriptions = [];
    private readonly SemaphoreSlim _sendSync = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.DateTime,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };
    private WebSocketClient _client;
    private long _sequence;
    private bool _isAuthenticated;
    private long _defaultAccountId;

    public NDAXSocketClient(string endpoint, WorkingTime workingTime,
        int reconnectAttempts, int omsId, SecureString key,
        SecureString secret, long userId)
    {
        _endpoint = ValidateEndpoint(endpoint).ToString();
        _workingTime = workingTime ?? throw new ArgumentNullException(
            nameof(workingTime));
        _reconnectAttempts = reconnectAttempts;
        _omsId = omsId;
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretText = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        _userId = userId;
        var supplied = (_apiKey.IsEmpty() ? 0 : 1) +
            (secretText.IsEmpty() ? 0 : 1) + (userId > 0 ? 1 : 0);
        if (supplied is > 0 and < 3)
            throw new ArgumentException(
                "NDAX API key, secret, and user ID must be configured together.");
        if (!secretText.IsEmpty())
            _apiSecret = Encoding.UTF8.GetBytes(secretText);
    }

    public override string Name => "NDAX_WebSocket";

    public bool IsCredentialsAvailable => !_apiKey.IsEmpty();
    public bool IsAuthenticated => _isAuthenticated;
    public long DefaultAccountId => _defaultAccountId;

    public event Func<NdaxLevel1, bool, CancellationToken, ValueTask>
        Level1Received;
    public event Func<NdaxLevel2Entry[], bool, CancellationToken, ValueTask>
        Level2Received;
    public event Func<NdaxPublicTrade[], bool, CancellationToken, ValueTask>
        PublicTradesReceived;
    public event Func<NdaxCandle[], bool, CancellationToken, ValueTask>
        CandlesReceived;
    public event Func<NdaxAccountPosition, CancellationToken, ValueTask>
        PositionReceived;
    public event Func<NdaxOrder, CancellationToken, ValueTask> OrderReceived;
    public event Func<NdaxAccountTrade, CancellationToken, ValueTask>
        AccountTradeReceived;
    public event Func<NdaxNewOrderReject, CancellationToken, ValueTask>
        OrderRejected;
    public event Func<Exception, CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates, CancellationToken, ValueTask>
        StateChanged;

    protected override void DisposeManaged()
    {
        FailPending(new ObjectDisposedException(nameof(NDAXSocketClient)));
        _client?.Dispose();
        _sendSync.Dispose();
        if (_apiSecret is not null)
            CryptographicOperations.ZeroMemory(_apiSecret);
        base.DisposeManaged();
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            throw new InvalidOperationException(
                "NDAX WebSocket is already initialized.");
        var client = _client = CreateClient();
        try
        {
            await client.ConnectAsync(cancellationToken);
            await AuthenticateAsync(cancellationToken);
            await RestoreAsync(client, cancellationToken);
        }
        catch
        {
            await DisposeClientAsync(cancellationToken);
            throw;
        }
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        => DisposeClientAsync(cancellationToken);

    public ValueTask SubscribeLevel1Async(int instrumentId,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(new(NdaxSubscriptionKinds.Level1,
            instrumentId, 0, 0), true, cancellationToken);

    public ValueTask UnsubscribeLevel1Async(int instrumentId,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(new(NdaxSubscriptionKinds.Level1,
            instrumentId, 0, 0), false, cancellationToken);

    public ValueTask SubscribeLevel2Async(int instrumentId, int depth,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(new(NdaxSubscriptionKinds.Level2,
            instrumentId, depth.Min(500).Max(1), 0), true,
            cancellationToken);

    public ValueTask UnsubscribeLevel2Async(int instrumentId, int depth,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(new(NdaxSubscriptionKinds.Level2,
            instrumentId, depth.Min(500).Max(1), 0), false,
            cancellationToken);

    public ValueTask SubscribeTradesAsync(int instrumentId, int historyCount,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(new(NdaxSubscriptionKinds.Trades,
            instrumentId, historyCount.Min(1000).Max(0), 0), true,
            cancellationToken);

    public ValueTask UnsubscribeTradesAsync(int instrumentId, int historyCount,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(new(NdaxSubscriptionKinds.Trades,
            instrumentId, historyCount.Min(1000).Max(0), 0), false,
            cancellationToken);

    public ValueTask SubscribeTickerAsync(int instrumentId,
        TimeSpan timeFrame, int historyCount,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(new(NdaxSubscriptionKinds.Ticker,
            instrumentId, timeFrame.ToInterval(), 0), true,
            cancellationToken, historyCount.Min(1000).Max(0));

    public ValueTask UnsubscribeTickerAsync(int instrumentId,
        TimeSpan timeFrame, CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(new(NdaxSubscriptionKinds.Ticker,
            instrumentId, timeFrame.ToInterval(), 0), false,
            cancellationToken);

    public ValueTask SubscribeAccountEventsAsync(long accountId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        return ChangeSubscriptionAsync(new(
            NdaxSubscriptionKinds.AccountEvents, 0, 0, accountId), true,
            cancellationToken);
    }

    public ValueTask UnsubscribeAccountEventsAsync(long accountId,
        CancellationToken cancellationToken)
        => ChangeSubscriptionAsync(new(
            NdaxSubscriptionKinds.AccountEvents, 0, 0, accountId), false,
            cancellationToken);

    public ValueTask<NdaxAccountPosition[]> GetPositionsAsync(long accountId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        return RequestAsync<NdaxAccountRequest, NdaxAccountPosition[]>(
            "GetAccountPositions", new()
            {
                OmsId = _omsId,
                AccountId = accountId,
            }, cancellationToken);
    }

    public ValueTask<NdaxOrder[]> GetOpenOrdersAsync(long accountId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        return RequestAsync<NdaxAccountRequest, NdaxOrder[]>("GetOpenOrders",
            new() { OmsId = _omsId, AccountId = accountId },
            cancellationToken);
    }

    public ValueTask<NdaxOrder[]> GetOrderHistoryAsync(long accountId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        return RequestAsync<NdaxAccountRequest, NdaxOrder[]>(
            "GetOrderHistory",
            new() { OmsId = _omsId, AccountId = accountId },
            cancellationToken);
    }

    public ValueTask<NdaxAccountTrade[]> GetAccountTradesAsync(long accountId,
        int startIndex, int count, CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        return RequestAsync<NdaxAccountTradesRequest, NdaxAccountTrade[]>(
            "GetAccountTrades", new()
            {
                OmsId = _omsId,
                AccountId = accountId,
                StartIndex = startIndex.Max(0),
                Count = count.Min(5000).Max(1),
            }, cancellationToken);
    }

    public ValueTask<NdaxSendOrderResponse> SendOrderAsync(
        NdaxSendOrderRequest request, CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        return RequestAsync<NdaxSendOrderRequest, NdaxSendOrderResponse>(
            "SendOrder", request ?? throw new ArgumentNullException(
                nameof(request)), cancellationToken);
    }

    public async ValueTask CancelOrderAsync(NdaxCancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        var response = await RequestAsync<NdaxCancelOrderRequest,
            NdaxGenericResponse>("CancelOrder",
            request ?? throw new ArgumentNullException(nameof(request)),
            cancellationToken);
        EnsureSuccess("CancelOrder", response);
    }

    public async ValueTask CancelAllOrdersAsync(long accountId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        var response = await RequestAsync<NdaxAccountRequest,
            NdaxGenericResponse>("CancelAllOrders",
            new() { OmsId = _omsId, AccountId = accountId },
            cancellationToken);
        EnsureSuccess("CancelAllOrders", response);
    }

    public async ValueTask PingAsync(CancellationToken cancellationToken)
    {
        _ = await RequestAsync<NdaxOmsRequest, string>("Ping",
            new() { OmsId = _omsId }, cancellationToken);
    }

    private WebSocketClient CreateClient()
    {
        WebSocketClient client = null;
        client = new WebSocketClient(
            _endpoint,
            (state, token) => OnStateChangedAsync(client, state, token),
            (error, token) => RaiseErrorAsync(error, token),
            (socket, message, token) => OnProcessAsync(socket, message, token),
            (s, a) => this.AddInfoLog(s, a),
            (s, a) => this.AddErrorLog(s, a),
            (s, a) => this.AddVerboseLog(s, a))
        {
            ReconnectAttempts = _reconnectAttempts,
            WorkingTime = _workingTime,
            DisableAutoResend = true,
            Indent = false,
            SendSettings = _jsonSettings,
        };
        return client;
    }

    private async ValueTask DisposeClientAsync(
        CancellationToken cancellationToken)
    {
        var client = _client;
        _client = null;
        _isAuthenticated = false;
        FailPending(new InvalidOperationException(
            "NDAX WebSocket was disconnected."));
        if (client is null)
            return;
        try
        {
            if (client.IsConnected)
                await client.DisconnectAsync(cancellationToken);
        }
        finally
        {
            client.Dispose();
        }
    }

    private async ValueTask OnStateChangedAsync(WebSocketClient client,
        ConnectionStates state, CancellationToken cancellationToken)
    {
        if (state == ConnectionStates.Reconnecting)
        {
            _isAuthenticated = false;
            FailPending(new HttpRequestException(
                "NDAX WebSocket connection was interrupted."));
        }
        else if (state == ConnectionStates.Restored)
        {
            try
            {
                await AuthenticateAsync(cancellationToken);
                await RestoreAsync(client, cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                await RaiseErrorAsync(error, cancellationToken);
                if (StateChanged is { } failedHandler)
                    await failedHandler(ConnectionStates.Failed,
                        cancellationToken);
                return;
            }
        }
        if (StateChanged is { } handler)
            await handler(state, cancellationToken);
    }

    private async ValueTask AuthenticateAsync(
        CancellationToken cancellationToken)
    {
        _isAuthenticated = false;
        _defaultAccountId = 0;
        if (!IsCredentialsAvailable)
            return;
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(
            CultureInfo.InvariantCulture);
        var payload = nonce + _userId.ToString(CultureInfo.InvariantCulture) +
            _apiKey;
        using var hmac = new HMACSHA256(_apiSecret);
        var signature = Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var response = await RequestAsync<NdaxAuthenticationRequest,
            NdaxAuthenticationResponse>("AuthenticateUser", new()
            {
                ApiKey = _apiKey,
                Signature = signature,
                UserId = _userId.ToString(CultureInfo.InvariantCulture),
                Nonce = nonce,
            }, cancellationToken);
        if (!response.IsAuthenticated)
            throw new NdaxApiException("AuthenticateUser", null,
                response.IsTwoFactorRequired
                    ? "two-factor authentication is required; use an API key that is enabled for API trading"
                    : response.ErrorMessage ?? (response.IsLocked
                        ? "the user is locked"
                        : "authentication failed"));
        _isAuthenticated = true;
        _defaultAccountId = response.User?.AccountId ?? 0;
    }

    private async ValueTask RestoreAsync(WebSocketClient client,
        CancellationToken cancellationToken)
    {
        _ = client;
        SubscriptionKey[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _subscriptions.OrderBy(static value =>
                value.Kind).ThenBy(static value => value.InstrumentId)
                .ThenBy(static value => value.Parameter)];
        foreach (var subscription in subscriptions)
            await SubscribeCoreAsync(subscription, 0, cancellationToken);
    }

    private async ValueTask ChangeSubscriptionAsync(SubscriptionKey key,
        bool isSubscribe, CancellationToken cancellationToken,
        int historyCount = 0)
    {
        if (!isSubscribe && key.Kind == NdaxSubscriptionKinds.AccountEvents)
            return;
        using (_sync.EnterScope())
        {
            if (isSubscribe)
            {
                if (_subscriptions.Contains(key))
                    return;
                if (_subscriptions.Count >= _maximumSubscriptions)
                    throw new InvalidOperationException(
                        "NDAX permits at most 10 subscriptions per WebSocket connection.");
                _subscriptions.Add(key);
            }
            else if (!_subscriptions.Remove(key))
                return;
        }
        var client = _client;
        if (client?.IsConnected != true)
            return;
        try
        {
            if (isSubscribe)
                await SubscribeCoreAsync(key, historyCount,
                    cancellationToken);
            else
            {
                await UnsubscribeCoreAsync(key, cancellationToken);
                if (key.Kind == NdaxSubscriptionKinds.Ticker)
                {
                    SubscriptionKey[] remaining;
                    using (_sync.EnterScope())
                        remaining = [.. _subscriptions.Where(value =>
                            value.Kind == NdaxSubscriptionKinds.Ticker &&
                            value.InstrumentId == key.InstrumentId)];
                    foreach (var item in remaining)
                        await SubscribeCoreAsync(item, 0, cancellationToken);
                }
            }
        }
        catch
        {
            using (_sync.EnterScope())
                if (isSubscribe)
                    _subscriptions.Remove(key);
                else
                    _subscriptions.Add(key);
            throw;
        }
    }

    private async ValueTask SubscribeCoreAsync(SubscriptionKey key,
        int historyCount, CancellationToken cancellationToken)
    {
        switch (key.Kind)
        {
            case NdaxSubscriptionKinds.Level1:
                var level1 = await RequestAsync<NdaxInstrumentRequest,
                    NdaxLevel1>("SubscribeLevel1", new()
                    {
                        OmsId = _omsId,
                        InstrumentId = key.InstrumentId,
                    }, cancellationToken);
                await RaiseAsync(Level1Received, level1, true,
                    cancellationToken);
                break;
            case NdaxSubscriptionKinds.Level2:
                var level2 = await RequestAsync<NdaxLevel2Request,
                    NdaxLevel2Entry[]>("SubscribeLevel2", new()
                    {
                        OmsId = _omsId,
                        InstrumentId = key.InstrumentId,
                        Depth = key.Parameter,
                    }, cancellationToken);
                await RaiseAsync(Level2Received, level2 ?? [], true,
                    cancellationToken);
                break;
            case NdaxSubscriptionKinds.Trades:
                var trades = await RequestAsync<NdaxTradesRequest,
                    NdaxPublicTrade[]>("SubscribeTrades", new()
                    {
                        OmsId = _omsId,
                        InstrumentId = key.InstrumentId,
                        IncludeLastCount = key.Parameter,
                    }, cancellationToken);
                await RaiseAsync(PublicTradesReceived, trades ?? [], true,
                    cancellationToken);
                break;
            case NdaxSubscriptionKinds.Ticker:
                var candles = await RequestAsync<NdaxTickerRequest,
                    NdaxCandle[]>("SubscribeTicker", new()
                    {
                        OmsId = _omsId,
                        InstrumentId = key.InstrumentId,
                        Interval = key.Parameter,
                        IncludeLastCount = historyCount,
                    }, cancellationToken);
                await RaiseAsync(CandlesReceived, candles ?? [], true,
                    cancellationToken);
                break;
            case NdaxSubscriptionKinds.AccountEvents:
                EnsureAuthenticated();
                var response = await RequestAsync<
                    NdaxAccountEventSubscription, NdaxSubscribeResponse>(
                    "SubscribeAccountEvents", new()
                    {
                        OmsId = _omsId,
                        AccountId = key.AccountId,
                    }, cancellationToken);
                if (!response.IsSubscribed)
                    throw new NdaxApiException("SubscribeAccountEvents", null,
                        "the subscription was rejected");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, null);
        }
    }

    private async ValueTask UnsubscribeCoreAsync(SubscriptionKey key,
        CancellationToken cancellationToken)
    {
        string operation;
        if (key.Kind == NdaxSubscriptionKinds.AccountEvents)
            return;
        operation = key.Kind switch
        {
            NdaxSubscriptionKinds.Level1 => "UnsubscribeLevel1",
            NdaxSubscriptionKinds.Level2 => "UnsubscribeLevel2",
            NdaxSubscriptionKinds.Trades => "UnsubscribeTrades",
            NdaxSubscriptionKinds.Ticker => "UnsubscribeTicker",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };
        var response = await RequestAsync<NdaxInstrumentRequest,
            NdaxGenericResponse>(operation, new()
            {
                OmsId = _omsId,
                InstrumentId = key.InstrumentId,
            }, cancellationToken);
        EnsureSuccess(operation, response);
    }

    private async ValueTask<TResponse> RequestAsync<TRequest, TResponse>(
        string operation, TRequest request, CancellationToken cancellationToken)
        where TRequest : class
    {
        var payload = JsonConvert.SerializeObject(request, _jsonSettings);
        var response = await RequestRawAsync(operation, payload,
            cancellationToken);
        if (typeof(TResponse) == typeof(string) &&
            !response.TrimStart().StartsWith('{') &&
            !response.TrimStart().StartsWith('['))
            return (TResponse)(object)response.Trim('"');
        try
        {
            return JsonConvert.DeserializeObject<TResponse>(response,
                _jsonSettings) ?? throw new InvalidDataException(
                $"NDAX {operation} returned an empty JSON value.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"NDAX {operation} returned an unexpected response shape.",
                error);
        }
    }

    private async ValueTask<string> RequestRawAsync(string operation,
        string payload, CancellationToken cancellationToken)
    {
        var client = _client;
        if (client?.IsConnected != true)
            throw new InvalidOperationException(
                "NDAX WebSocket is not connected.");
        var sequence = Interlocked.Add(ref _sequence, 2);
        var pending = new PendingRequest { Operation = operation };
        using (_sync.EnterScope())
            _pending.Add(sequence, pending);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(20));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);
        using var registration = linked.Token.Register(() =>
            pending.Completion.TrySetCanceled(linked.Token));
        try
        {
            await SendAsync(client, new()
            {
                MessageType = NdaxMessageTypes.Request,
                Sequence = sequence,
                Name = operation,
                Payload = payload,
            }, cancellationToken);
            return await pending.Completion.Task;
        }
        finally
        {
            using (_sync.EnterScope())
                _pending.Remove(sequence);
        }
    }

    private async ValueTask SendAsync(WebSocketClient client, NdaxFrame frame,
        CancellationToken cancellationToken)
    {
        await _sendSync.WaitAsync(cancellationToken);
        try
        {
            await client.SendAsync(frame, cancellationToken);
        }
        finally
        {
            _sendSync.Release();
        }
    }

    private async ValueTask OnProcessAsync(WebSocketClient client,
        WebSocketMessage message, CancellationToken cancellationToken)
    {
        _ = client;
        var raw = message.AsString();
        if (raw.IsEmpty())
            return;
        if (raw.Length > _maximumMessageCharacters)
        {
            await RaiseErrorAsync(new InvalidDataException(
                "NDAX WebSocket message exceeds the 8 MiB safety limit."),
                cancellationToken);
            return;
        }
        try
        {
            var frame = Deserialize<NdaxFrame>(raw, "frame");
            if (frame.MessageType is NdaxMessageTypes.Reply or
                NdaxMessageTypes.Error)
            {
                PendingRequest pending;
                using (_sync.EnterScope())
                    _pending.TryGetValue(frame.Sequence, out pending);
                if (pending is null)
                    return;
                if (frame.MessageType == NdaxMessageTypes.Error)
                    pending.Completion.TrySetException(CreateError(
                        pending.Operation, frame.Payload));
                else
                    pending.Completion.TrySetResult(frame.Payload ??
                        string.Empty);
                return;
            }
            if (frame.MessageType != NdaxMessageTypes.Event)
                return;
            await ProcessEventAsync(frame.Name, frame.Payload,
                cancellationToken);
        }
        catch (Exception error)
        {
            await RaiseErrorAsync(error, cancellationToken);
        }
    }

    private async ValueTask ProcessEventAsync(string name, string payload,
        CancellationToken cancellationToken)
    {
        if (name.EqualsIgnoreCase("Level1UpdateEvent"))
            await RaiseAsync(Level1Received,
                Deserialize<NdaxLevel1>(payload, name), false,
                cancellationToken);
        else if (name.EqualsIgnoreCase("Level2UpdateEvent"))
            await RaiseAsync(Level2Received,
                Deserialize<NdaxLevel2Entry[]>(payload, name) ?? [], false,
                cancellationToken);
        else if (name.EqualsIgnoreCase("TickerDataUpdateEvent"))
            await RaiseAsync(CandlesReceived,
                Deserialize<NdaxCandle[]>(payload, name) ?? [], false,
                cancellationToken);
        else if (name.EqualsIgnoreCase("TradeDataUpdateEvent"))
            await RaiseAsync(PublicTradesReceived,
                Deserialize<NdaxPublicTrade[]>(payload, name) ?? [], false,
                cancellationToken);
        else if (name.EqualsIgnoreCase("AccountPositionEvent"))
            await RaiseAsync(PositionReceived,
                Deserialize<NdaxAccountPosition>(payload, name),
                cancellationToken);
        else if (name.EqualsIgnoreCase("OrderStateEvent"))
            await RaiseAsync(OrderReceived,
                Deserialize<NdaxOrder>(payload, name), cancellationToken);
        else if (name.EqualsIgnoreCase("NewOrderRejectEvent"))
            await RaiseAsync(OrderRejected,
                Deserialize<NdaxNewOrderReject>(payload, name),
                cancellationToken);
        else if (name.EqualsIgnoreCase("OrderTradeEvent"))
        {
            if (payload.TrimStart().StartsWith('['))
                await RaiseAsync(PublicTradesReceived,
                    Deserialize<NdaxPublicTrade[]>(payload, name) ?? [], false,
                    cancellationToken);
            else
                await RaiseAsync(AccountTradeReceived,
                    Deserialize<NdaxAccountTrade>(payload, name),
                    cancellationToken);
        }
    }

    private TPayload Deserialize<TPayload>(string payload, string operation)
    {
        if (payload.IsEmpty())
            throw new InvalidDataException(
                $"NDAX {operation} returned an empty payload.");
        return JsonConvert.DeserializeObject<TPayload>(payload,
            _jsonSettings) ?? throw new InvalidDataException(
            $"NDAX {operation} returned an empty JSON value.");
    }

    private Exception CreateError(string operation, string payload)
    {
        NdaxGenericResponse response = null;
        try
        {
            response = JsonConvert.DeserializeObject<NdaxGenericResponse>(
                payload, _jsonSettings);
        }
        catch (JsonException)
        {
        }
        var message = response?.ErrorMessage ?? response?.Detail ??
            payload?.Trim();
        if (message?.Length > 512)
            message = message[..512];
        return new NdaxApiException(operation, response?.ErrorCode,
            message.IsEmpty() ? "request failed" : message);
    }

    private static void EnsureSuccess(string operation,
        NdaxGenericResponse response)
    {
        if (response?.Result == true)
            return;
        throw new NdaxApiException(operation, response?.ErrorCode,
            response?.ErrorMessage ?? response?.Detail ??
                "operation failed");
    }

    private void EnsureAuthenticated()
    {
        if (!_isAuthenticated)
            throw new InvalidOperationException(
                "NDAX API key, secret, and user ID are required for private operations.");
    }

    private void FailPending(Exception error)
    {
        PendingRequest[] pending;
        using (_sync.EnterScope())
        {
            pending = [.. _pending.Values];
            _pending.Clear();
        }
        foreach (var request in pending)
            request.Completion.TrySetException(error);
    }

    private ValueTask RaiseErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler ? handler(error, cancellationToken) : default;

    private static async ValueTask RaiseAsync<TPayload>(
        Func<TPayload, CancellationToken, ValueTask> handler,
        TPayload payload, CancellationToken cancellationToken)
        where TPayload : class
    {
        if (handler is not null && payload is not null)
            await handler(payload, cancellationToken);
    }

    private static async ValueTask RaiseAsync<TPayload>(
        Func<TPayload, bool, CancellationToken, ValueTask> handler,
        TPayload payload, bool isSnapshot,
        CancellationToken cancellationToken)
        where TPayload : class
    {
        if (handler is not null && payload is not null)
            await handler(payload, isSnapshot, cancellationToken);
    }

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase("wss"))
            throw new ArgumentException(
                "NDAX WebSocket endpoint must be an absolute WSS URI.",
                nameof(value));
        return endpoint;
    }
}
