namespace StockSharp.CoinApi.Native;

sealed class CoinApiSocketClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly string _apiKey;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _connectionGate = new(1, 1);
	private readonly SemaphoreSlim _subscriptionGate = new(1, 1);
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly HashSet<CoinApiStreamKey> _subscriptions = [];
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private WebSocketClient _client;
	private bool _isDisposed;

	public CoinApiSocketClient(string endpoint, SecureString apiKey,
		WorkingTime workingTime, int reconnectAttempts)
	{
		if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
			uri.Scheme != "wss" || uri.Host.IsEmpty() || !uri.Query.IsEmpty() ||
			!uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"A valid secure CoinAPI WebSocket endpoint is required.",
				nameof(endpoint));
		if (apiKey.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
		_endpoint = uri;
		_apiKey = apiKey.UnSecure().Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = Math.Max(1, reconnectAttempts);
	}

	public override string Name => "CoinAPI_WS";

	public event Func<CoinApiSocketMessage, CancellationToken, ValueTask>
		MessageReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask SubscribeAsync(CoinApiStreamKey key,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ValidateKey(key);
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			bool isFirst;
			using (_sync.EnterScope())
			{
				if (_subscriptions.Contains(key))
					return;
				isFirst = _subscriptions.Count == 0;
				_subscriptions.Add(key);
			}
			try
			{
				await EnsureConnectedAsync(cancellationToken);
				await SendSubscriptionAsync(key, isFirst
					? CoinApiSocketRequestTypes.Hello
					: CoinApiSocketRequestTypes.Subscribe, cancellationToken);
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

	public async ValueTask UnsubscribeAsync(CoinApiStreamKey key,
		CancellationToken cancellationToken)
	{
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			using (_sync.EnterScope())
				if (!_subscriptions.Remove(key))
					return;
			if (_client?.IsConnected == true)
				await SendSubscriptionAsync(key,
					CoinApiSocketRequestTypes.Unsubscribe, cancellationToken);
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
			socket.Options.SetRequestHeader("X-CoinAPI-Key", _apiKey);
			socket.Options.SetRequestHeader("User-Agent", "StockSharp-CoinAPI/1.0");
		};
		_client = client;
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		this.AddInfoLog("CoinAPI WebSocket state: {0}.", state);
		if (state != ConnectionStates.Restored)
			return;
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			CoinApiStreamKey[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			for (var index = 0; index < subscriptions.Length; index++)
				await SendSubscriptionAsync(subscriptions[index], index == 0
					? CoinApiSocketRequestTypes.Hello
					: CoinApiSocketRequestTypes.Subscribe, cancellationToken);
		}
		finally
		{
			_subscriptionGate.Release();
		}
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
			var envelope = JsonConvert.DeserializeObject<CoinApiSocketMessage>(
				json, _settings) ?? throw new InvalidDataException(
					"CoinAPI WebSocket returned an empty JSON message.");
			switch (envelope.Type)
			{
				case CoinApiSocketMessageTypes.Trade:
				case CoinApiSocketMessageTypes.Quote:
				case CoinApiSocketMessageTypes.Book5:
				case CoinApiSocketMessageTypes.Book20:
				case CoinApiSocketMessageTypes.Book50:
				case CoinApiSocketMessageTypes.Ohlcv:
					if (envelope.SymbolId.IsEmpty())
						throw new InvalidDataException(
							"CoinAPI market-data message has no symbol_id.");
					if (MessageReceived is { } handler)
						await handler(envelope, cancellationToken);
					break;
				case CoinApiSocketMessageTypes.Heartbeat:
					break;
				case CoinApiSocketMessageTypes.Reconnect:
					this.AddInfoLog(
						"CoinAPI requested reconnect within {0} seconds before {1}.",
						envelope.WithinSeconds, envelope.BeforeTime);
					break;
				case CoinApiSocketMessageTypes.Error:
					throw new InvalidOperationException(
						$"CoinAPI WebSocket error: {envelope.Message.IsEmpty("request failed")}");
				default:
					throw new InvalidDataException(
						"CoinAPI WebSocket returned an unsupported message type.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask SendSubscriptionAsync(CoinApiStreamKey key,
		CoinApiSocketRequestTypes type, CancellationToken cancellationToken)
		=> SendAsync(new CoinApiSocketRequest
		{
			Type = type,
			ApiKey = _apiKey,
			IsHeartbeat = true,
			DataTypes = [key.DataType],
			SymbolIds = [key.SymbolId + "$"],
			PeriodIds = key.DataType == CoinApiSocketDataTypes.Ohlcv
				? [key.PeriodId]
				: null,
		}, cancellationToken);

	private async ValueTask SendAsync(CoinApiSocketRequest request,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"CoinAPI WebSocket is not connected.");
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

	private static void ValidateKey(CoinApiStreamKey key)
	{
		if (key.DataType == CoinApiSocketDataTypes.Unknown ||
			key.SymbolId.IsEmpty() || key.SymbolId.Contains('$') ||
			key.DataType == CoinApiSocketDataTypes.Ohlcv &&
			key.PeriodId == CoinApiPeriodIds.Unknown ||
			key.DataType != CoinApiSocketDataTypes.Ohlcv &&
			key.PeriodId != CoinApiPeriodIds.Unknown)
			throw new ArgumentException("Invalid CoinAPI stream subscription.",
				nameof(key));
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		using (_sync.EnterScope())
			_subscriptions.Clear();
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
