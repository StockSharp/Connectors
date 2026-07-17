namespace StockSharp.Fugle.Native;

sealed class FugleSocketClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly FugleAssetKinds _kind;
	private readonly string _apiKey;
	private readonly WebSocketClient _client;
	private readonly SynchronizedDictionary<string, FugleSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, FugleSubscription> _serverSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly Queue<string> _pendingSubscriptions = new();
	private readonly object _pendingSync = new();
	private TaskCompletionSource<bool> _authenticationCompletion;
	private bool _isConnected;

	public FugleSocketClient(FugleAssetKinds kind, SecureString apiKey, int reconnectAttempts, WorkingTime workingTime)
	{
		_kind = kind;
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure();
		var url = kind == FugleAssetKinds.Stock
			? "wss://api.fugle.tw/marketdata/v1.0/stock/streaming"
			: "wss://api.fugle.tw/marketdata/v1.0/futopt/streaming";

		_client = new(
			url,
			(_, _) => default,
			(error, cancellationToken) => Error is { } handler ? handler(error, cancellationToken) : default,
			Process,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			DisableAutoResend = true,
		};
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(Fugle) + "_" + _kind + "_" + nameof(FugleSocketClient);

	public event Func<FugleSubscription, FugleStreamData, CancellationToken, ValueTask> DataReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask Connect(CancellationToken cancellationToken)
	{
		if (_isConnected)
			return;

		_authenticationCompletion = CreateCompletion();
		await _client.ConnectAsync(cancellationToken);
		await _authenticationCompletion.Task.WaitAsync(cancellationToken);
		_isConnected = true;
	}

	public async ValueTask Disconnect(CancellationToken cancellationToken)
	{
		if (!_isConnected)
			return;
		_isConnected = false;
		await _client.DisconnectAsync(cancellationToken);
	}

	public ValueTask SendHeartbeat(CancellationToken cancellationToken)
		=> Send(new FugleSocketRequest<FuglePingData>
		{
			Event = "ping",
			Data = new() { State = "StockSharp" },
		}, cancellationToken);

	public async ValueTask Subscribe(FugleSubscription subscription, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(subscription);
		subscription.Channel.ThrowIfEmpty(nameof(subscription.Channel));
		subscription.Symbol.ThrowIfEmpty(nameof(subscription.Symbol));

		if (_subscriptions.TryGetValue(subscription.Key, out var existing))
		{
			existing.TransactionId = subscription.TransactionId;
			existing.SecurityId = subscription.SecurityId;
			return;
		}

		_subscriptions[subscription.Key] = subscription;
		await SendSubscription(subscription, cancellationToken);
	}

	public async ValueTask Unsubscribe(FugleSubscription subscription, CancellationToken cancellationToken)
	{
		if (!_subscriptions.TryGetAndRemove(subscription.Key, out var existing))
			return;

		if (!existing.ServerId.IsEmpty())
		{
			_serverSubscriptions.Remove(existing.ServerId);
			await SendUnsubscribe(existing.ServerId, cancellationToken);
		}
	}

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		if (reconnect || _authenticationCompletion == null || _authenticationCompletion.Task.IsCompleted)
			_authenticationCompletion = CreateCompletion();

		_serverSubscriptions.Clear();
		lock (_pendingSync)
			_pendingSubscriptions.Clear();
		foreach (var subscription in _subscriptions.Values)
			subscription.ServerId = null;

		await Send(new FugleSocketRequest<FugleAuthData>
		{
			Event = "auth",
			Data = new() { ApiKey = _apiKey },
		}, cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var text = message.AsString()?.Trim();
		if (text.IsEmpty())
			return;

		var envelope = JsonConvert.DeserializeObject<FugleSocketEnvelope>(text, _jsonSettings)
			?? throw new InvalidDataException("Fugle returned an empty WebSocket message.");
		switch (envelope.Event?.ToLowerInvariant())
		{
			case "authenticated":
				foreach (var subscription in _subscriptions.Values)
					await SendSubscription(subscription, cancellationToken);
				_authenticationCompletion?.TrySetResult(true);
				break;

			case "subscribed":
				await ProcessSubscribed(text, cancellationToken);
				break;

			case "data":
			{
				var stream = JsonConvert.DeserializeObject<FugleStreamMessage>(text, _jsonSettings)
					?? throw new InvalidDataException("Fugle returned invalid streaming data.");
				if (stream.Data != null && _serverSubscriptions.TryGetValue(stream.Id, out var subscription) && DataReceived is { } handler)
					await handler(subscription, stream.Data, cancellationToken);
				break;
			}

			case "error":
			{
				var status = JsonConvert.DeserializeObject<FugleSocketStatusMessage>(text, _jsonSettings);
				var error = new InvalidOperationException($"Fugle WebSocket error: {status?.Data?.Message.IsEmpty("Unknown error")}");
				_authenticationCompletion?.TrySetException(error);
				if (Error is { } handler)
					await handler(error, cancellationToken);
				break;
			}

			case "heartbeat":
			case "pong":
			case "unsubscribed":
			case "subscriptions":
				break;

			default:
				this.AddVerboseLog("Ignored Fugle WebSocket event {0}.", envelope.Event);
				break;
		}
	}

	private async ValueTask ProcessSubscribed(string text, CancellationToken cancellationToken)
	{
		var acknowledgement = JsonConvert.DeserializeObject<FugleSubscriptionMessage>(text, _jsonSettings)
			?? throw new InvalidDataException("Fugle returned an invalid subscription acknowledgement.");
		var data = acknowledgement.Data;
		if (data?.Id.IsEmpty() != false)
			throw new InvalidDataException("Fugle subscription acknowledgement has no channel id.");

		string pendingKey = null;
		lock (_pendingSync)
		{
			if (_pendingSubscriptions.Count > 0)
				pendingKey = _pendingSubscriptions.Dequeue();
		}

		FugleSubscription subscription = null;
		if (!pendingKey.IsEmpty())
			_subscriptions.TryGetValue(pendingKey, out subscription);
		if (subscription == null || !subscription.Channel.EqualsIgnoreCase(data.Channel) || !subscription.Symbol.EqualsIgnoreCase(data.Symbol))
		{
			subscription = _subscriptions.Values.FirstOrDefault(item =>
				item.ServerId.IsEmpty() && item.Channel.EqualsIgnoreCase(data.Channel) && item.Symbol.EqualsIgnoreCase(data.Symbol));
		}

		if (subscription == null)
		{
			await SendUnsubscribe(data.Id, cancellationToken);
			return;
		}

		subscription.ServerId = data.Id;
		_serverSubscriptions[data.Id] = subscription;
	}

	private ValueTask SendSubscription(FugleSubscription subscription, CancellationToken cancellationToken)
	{
		lock (_pendingSync)
			_pendingSubscriptions.Enqueue(subscription.Key);

		return Send(new FugleSocketRequest<FugleSubscribeData>
		{
			Event = "subscribe",
			Data = new()
			{
				Channel = subscription.Channel,
				Symbol = subscription.Symbol,
				IsIntradayOddLot = subscription.IsIntradayOddLot,
				IsAfterHours = subscription.IsAfterHours,
			},
		}, cancellationToken);
	}

	private ValueTask SendUnsubscribe(string serverId, CancellationToken cancellationToken)
		=> Send(new FugleSocketRequest<FugleUnsubscribeData>
		{
			Event = "unsubscribe",
			Data = new() { Id = serverId },
		}, cancellationToken);

	private ValueTask Send<T>(T request, CancellationToken cancellationToken)
		where T : class
		=> _client.SendAsync(JsonConvert.SerializeObject(request, Formatting.None, _jsonSettings), cancellationToken);

	private static TaskCompletionSource<bool> CreateCompletion()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);
}
