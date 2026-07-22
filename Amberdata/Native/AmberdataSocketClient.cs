namespace StockSharp.Amberdata.Native;

sealed class AmberdataSocketClient : BaseLogReceiver
{
	private sealed class PendingRequest
	{
		public AmberdataSocketMethods Method { get; init; }
		public AmberdataStreamKey Key { get; init; }
		public TaskCompletionSource<ControlResult> Completion { get; } = new(
			TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private sealed class ControlResult
	{
		public string SubscriptionId { get; init; }
		public bool? IsSuccess { get; init; }
		public string[] Metadata { get; init; }
	}

	private static readonly string[] _tradeMetadata =
	[
		"exchange",
		"pair",
		"timestamp",
		"timestampNanoseconds",
		"tradeId",
		"price",
		"volume",
		"isBuy",
	];

	private readonly Uri _endpoint;
	private readonly string _apiKey;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly TimeSpan _responseTimeout;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _connectionGate = new(1, 1);
	private readonly SemaphoreSlim _subscriptionGate = new(1, 1);
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly HashSet<AmberdataStreamKey> _subscriptions = [];
	private readonly Dictionary<long, PendingRequest> _pending = [];
	private readonly Dictionary<AmberdataStreamKey, string> _subscriptionIds = [];
	private readonly Dictionary<string, AmberdataStreamKey> _streams =
		new(StringComparer.Ordinal);
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private WebSocketClient _client;
	private long _requestId;
	private bool _isDisposed;

	public AmberdataSocketClient(string endpoint, SecureString apiKey,
		WorkingTime workingTime, int reconnectAttempts, TimeSpan responseTimeout)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/'), UriKind.Absolute,
			out var uri) || uri.Scheme != "wss" || uri.Host.IsEmpty() ||
			!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"A valid secure Amberdata WebSocket endpoint is required.",
				nameof(endpoint));
		if (apiKey.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
		if (responseTimeout < TimeSpan.FromSeconds(1) ||
			responseTimeout > TimeSpan.FromMinutes(5))
			throw new ArgumentOutOfRangeException(nameof(responseTimeout));
		_endpoint = uri;
		_apiKey = apiKey.UnSecure().Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = Math.Max(1, reconnectAttempts);
		_responseTimeout = responseTimeout;
	}

	public override string Name => "Amberdata_WS";

	public event Func<AmberdataSocketUpdate, CancellationToken, ValueTask>
		MessageReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask SubscribeAsync(AmberdataStreamKey key,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		key = ValidateKey(key);
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			using (_sync.EnterScope())
			{
				if (_subscriptions.Contains(key))
					return;
				_subscriptions.Add(key);
			}
			try
			{
				await EnsureConnectedAsync(cancellationToken);
				await SubscribeCoreAsync(key, cancellationToken);
			}
			catch
			{
				using (_sync.EnterScope())
					_subscriptions.Remove(key);
				throw;
			}
		}
		finally
		{
			_subscriptionGate.Release();
		}
	}

	public async ValueTask UnsubscribeAsync(AmberdataStreamKey key,
		CancellationToken cancellationToken)
	{
		key = ValidateKey(key);
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			string subscriptionId;
			using (_sync.EnterScope())
			{
				if (!_subscriptions.Remove(key))
					return;
				_subscriptionIds.TryGetValue(key, out subscriptionId);
			}
			try
			{
				if (!subscriptionId.IsEmpty() && _client?.IsConnected == true)
					await UnsubscribeCoreAsync(key, subscriptionId,
						cancellationToken);
			}
			finally
			{
				RemoveServerSubscription(key, subscriptionId);
			}
		}
		finally
		{
			_subscriptionGate.Release();
		}
	}

	private async ValueTask EnsureConnectedAsync(
		CancellationToken cancellationToken)
	{
		await _connectionGate.WaitAsync(cancellationToken);
		try
		{
			EnsureClient();
			if (!_client.IsConnected)
				await _client.ConnectAsync(cancellationToken);
		}
		finally
		{
			_connectionGate.Release();
		}
	}

	private void EnsureClient()
	{
		if (_client is not null)
			return;
		var client = new WebSocketClient(_endpoint.AbsoluteUri,
			OnStateChangedAsync, RaiseErrorAsync, OnProcessAsync,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		client.Init += socket =>
		{
			socket.Options.SetRequestHeader("x-api-key", _apiKey);
			socket.Options.SetRequestHeader("User-Agent",
				"StockSharp-Amberdata/1.0");
		};
		_client = client;
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		this.AddInfoLog("Amberdata WebSocket state: {0}.", state);
		if (state == ConnectionStates.Failed)
		{
			FailPending(new IOException(
				"Amberdata WebSocket connection failed."));
			ClearServerSubscriptions();
			return;
		}
		if (state != ConnectionStates.Restored)
			return;

		FailPending(new IOException(
			"Amberdata WebSocket connection was restored."));
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			AmberdataStreamKey[] subscriptions;
			using (_sync.EnterScope())
			{
				_subscriptionIds.Clear();
				_streams.Clear();
				subscriptions = [.. _subscriptions];
			}
			foreach (var key in subscriptions)
				await SubscribeCoreAsync(key, cancellationToken);
		}
		finally
		{
			_subscriptionGate.Release();
		}
	}

	private async ValueTask SubscribeCoreAsync(AmberdataStreamKey key,
		CancellationToken cancellationToken)
	{
		var id = NextRequestId();
		var pending = AddPending(id, AmberdataSocketMethods.Subscribe, key);
		try
		{
			await SendAsync(new AmberdataSocketSubscribeRequest
			{
				Id = id,
				Parameters = new()
				{
					Channel = key.Channel,
					Options = new()
					{
						Exchange = key.Security.Exchange,
						Pair = key.Security.Instrument,
					},
				},
			}, cancellationToken);
			var result = await pending.Completion.Task.WaitAsync(_responseTimeout,
				cancellationToken);
			if (result.SubscriptionId.IsEmpty())
				throw new InvalidDataException(
					"Amberdata returned an empty WebSocket subscription id.");
			ValidateMetadata(key.Channel, result.Metadata);
			using (_sync.EnterScope())
			{
				_subscriptionIds[key] = result.SubscriptionId;
				_streams[result.SubscriptionId] = key;
			}
		}
		finally
		{
			using (_sync.EnterScope())
				_pending.Remove(id);
		}
	}

	private async ValueTask UnsubscribeCoreAsync(AmberdataStreamKey key,
		string subscriptionId, CancellationToken cancellationToken)
	{
		var id = NextRequestId();
		var pending = AddPending(id, AmberdataSocketMethods.Unsubscribe, key);
		try
		{
			await SendAsync(new AmberdataSocketUnsubscribeRequest
			{
				Id = id,
				Parameters = new()
				{
					SubscriptionId = subscriptionId,
				},
			}, cancellationToken);
			var result = await pending.Completion.Task.WaitAsync(_responseTimeout,
				cancellationToken);
			if (result.IsSuccess != true)
				throw new InvalidOperationException(
					"Amberdata rejected the WebSocket unsubscribe request.");
		}
		finally
		{
			using (_sync.EnterScope())
				_pending.Remove(id);
		}
	}

	private PendingRequest AddPending(long id, AmberdataSocketMethods method,
		AmberdataStreamKey key)
	{
		var pending = new PendingRequest
		{
			Method = method,
			Key = key,
		};
		using (_sync.EnterScope())
			_pending.Add(id, pending);
		return pending;
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var json = message.AsString();
		if (json.IsEmpty())
			return;
		try
		{
			var header = Deserialize<AmberdataSocketEnvelopeHeader>(json);
			ValidateJsonRpc(header.JsonRpc);
			if (header.Id is { } id)
			{
				CompleteControlRequest(id, header, json);
				return;
			}
			if (header.Error is not null)
				throw CreateSocketException(header.Error);
			if (header.Method != AmberdataSocketMethods.Subscription ||
				header.Parameters?.SubscriptionId.IsEmpty() != false)
				throw new InvalidDataException(
					"Amberdata WebSocket returned an unsupported message.");

			AmberdataStreamKey key;
			using (_sync.EnterScope())
				if (!_streams.TryGetValue(header.Parameters.SubscriptionId,
					out key))
					throw new InvalidDataException(
						"Amberdata WebSocket returned data for an unknown subscription.");
			var update = DeserializeUpdate(key, json);
			if (MessageReceived is { } handler)
				await handler(update, cancellationToken);
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			ArgumentException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private void CompleteControlRequest(long id,
		AmberdataSocketEnvelopeHeader header, string json)
	{
		PendingRequest pending;
		using (_sync.EnterScope())
		{
			_pending.TryGetValue(id, out pending);
			_pending.Remove(id);
		}
		if (pending is null)
			return;
		try
		{
			if (header.Error is not null)
				throw CreateSocketException(header.Error);
			ControlResult result;
			switch (pending.Method)
			{
				case AmberdataSocketMethods.Subscribe:
					var subscribe = Deserialize<
						AmberdataSocketSubscribeResponse>(json);
					ValidateJsonRpc(subscribe.JsonRpc);
					result = new()
					{
						SubscriptionId = subscribe.SubscriptionId,
						Metadata = subscribe.Metadata,
					};
					break;
				case AmberdataSocketMethods.Unsubscribe:
					var unsubscribe = Deserialize<
						AmberdataSocketUnsubscribeResponse>(json);
					ValidateJsonRpc(unsubscribe.JsonRpc);
					result = new()
					{
						IsSuccess = unsubscribe.IsResult,
					};
					break;
				default:
					throw new InvalidOperationException(
						"Amberdata control response has no pending operation.");
			}
			pending.Completion.TrySetResult(result);
		}
		catch (Exception error)
		{
			pending.Completion.TrySetException(error);
			throw;
		}
	}

	private AmberdataSocketUpdate DeserializeUpdate(AmberdataStreamKey key,
		string json)
	{
		switch (key.Channel)
		{
			case AmberdataSocketChannels.Trades:
				var trades = DeserializeNotification<AmberdataSocketTrade[]>(json,
					key);
				foreach (var trade in trades ?? [])
					ValidateIdentity(key, trade?.Exchange, trade?.Pair);
				return new() { Key = key, Trades = trades ?? [] };
			case AmberdataSocketChannels.TickerSnapshots:
				var ticker = DeserializeNotification<AmberdataSocketTicker>(json,
					key) ?? throw new InvalidDataException(
						"Amberdata ticker payload is missing.");
				ValidateIdentity(key, ticker.Exchange, ticker.Pair);
				return new() { Key = key, Ticker = ticker };
			case AmberdataSocketChannels.OrderSnapshots:
				var sides = DeserializeNotification<AmberdataSocketBookSide[]>(json,
					key) ?? [];
				foreach (var side in sides)
					ValidateIdentity(key, side?.Exchange, side?.Instrument);
				return new() { Key = key, BookSides = sides };
			case AmberdataSocketChannels.Ohlcv:
				var ohlcv = DeserializeNotification<AmberdataSocketOhlcv>(json,
					key) ?? throw new InvalidDataException(
						"Amberdata OHLCV payload is missing.");
				ValidateIdentity(key, ohlcv.Exchange, ohlcv.Pair);
				return new() { Key = key, Ohlcv = ohlcv };
			default:
				throw new InvalidDataException(
					"Amberdata stream channel is unsupported.");
		}
	}

	private T DeserializeNotification<T>(string json,
		AmberdataStreamKey key)
	{
		var notification = Deserialize<AmberdataSocketNotification<T>>(json);
		ValidateJsonRpc(notification.JsonRpc);
		if (notification.Method != AmberdataSocketMethods.Subscription ||
			notification.Parameters is null ||
			notification.Parameters.SubscriptionId.IsEmpty())
			throw new InvalidDataException(
				"Amberdata subscription notification is incomplete.");
		using (_sync.EnterScope())
			if (!_streams.TryGetValue(notification.Parameters.SubscriptionId,
				out var mapped) || mapped != key)
				throw new InvalidDataException(
					"Amberdata notification subscription does not match its stream.");
		return notification.Parameters.Result;
	}

	private T Deserialize<T>(string json)
	{
		var result = JsonConvert.DeserializeObject<T>(json, _settings);
		if (result is null)
			throw new InvalidDataException(
				"Amberdata WebSocket returned an empty JSON message.");
		return result;
	}

	private static void ValidateIdentity(AmberdataStreamKey key,
		string exchange, string instrument)
	{
		if (!key.Security.Exchange.EqualsIgnoreCase(exchange) ||
			!key.Security.Instrument.EqualsIgnoreCase(instrument))
			throw new InvalidDataException(
				"Amberdata WebSocket returned data for a different instrument.");
	}

	private static void ValidateMetadata(AmberdataSocketChannels channel,
		string[] metadata)
	{
		if (channel != AmberdataSocketChannels.Trades)
			return;
		if (metadata is null || metadata.Length != _tradeMetadata.Length ||
			!metadata.SequenceEqual(_tradeMetadata,
				StringComparer.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"Amberdata trade stream metadata does not match the supported schema.");
	}

	private async ValueTask SendAsync<TRequest>(TRequest request,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"Amberdata WebSocket is not connected.");
		await _sendGate.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(request, cancellationToken);
		}
		finally
		{
			_sendGate.Release();
		}
	}

	private static AmberdataStreamKey ValidateKey(AmberdataStreamKey key)
	{
		if (key.Channel == AmberdataSocketChannels.Unknown)
			throw new ArgumentException(
				"Amberdata stream channel is missing.", nameof(key));
		return new(key.Channel, key.Security.Normalize());
	}

	private static void ValidateJsonRpc(string value)
	{
		if (value != "2.0")
			throw new InvalidDataException(
				"Amberdata WebSocket returned an unsupported JSON-RPC version.");
	}

	private static InvalidOperationException CreateSocketException(
		AmberdataSocketError error)
		=> new($"Amberdata WebSocket error {error?.Code}: " +
			error?.Description.IsEmpty("request failed"));

	private long NextRequestId() => Interlocked.Increment(ref _requestId);

	private void RemoveServerSubscription(AmberdataStreamKey key,
		string subscriptionId)
	{
		using (_sync.EnterScope())
		{
			_subscriptionIds.Remove(key);
			if (!subscriptionId.IsEmpty())
				_streams.Remove(subscriptionId);
		}
	}

	private void ClearServerSubscriptions()
	{
		using (_sync.EnterScope())
		{
			_subscriptionIds.Clear();
			_streams.Clear();
		}
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

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		using (_sync.EnterScope())
		{
			_subscriptions.Clear();
			_subscriptionIds.Clear();
			_streams.Clear();
		}
		FailPending(new IOException(
			"Amberdata WebSocket was disconnected."));
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

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
		_connectionGate.Dispose();
		_subscriptionGate.Dispose();
		_sendGate.Dispose();
		base.DisposeManaged();
	}
}
