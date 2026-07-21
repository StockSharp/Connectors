namespace StockSharp.CoinMarketCap.Native;

sealed class CoinMarketCapSocketClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly string _apiKey;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _connectionGate = new(1, 1);
	private readonly SemaphoreSlim _subscriptionGate = new(1, 1);
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly HashSet<int> _subscriptions = [];
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
	private TimeSpan _pingInterval = TimeSpan.FromSeconds(10);
	private bool _isDisposed;

	public CoinMarketCapSocketClient(string endpoint, SecureString apiKey,
		WorkingTime workingTime, int reconnectAttempts)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/'), UriKind.Absolute,
			out var uri) || uri.Scheme != "wss" || uri.Host.IsEmpty() ||
			!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"A valid secure CoinMarketCap WebSocket endpoint is required.",
				nameof(endpoint));
		if (apiKey.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
		_endpoint = uri;
		_apiKey = apiKey.UnSecure().Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = Math.Max(1, reconnectAttempts);
	}

	public override string Name => "CoinMarketCap_WS";

	public TimeSpan PingInterval
	{
		get
		{
			using (_sync.EnterScope())
				return _pingInterval;
		}
	}

	public event Func<CoinMarketCapSocketPrice, long, CancellationToken,
		ValueTask> PriceReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask SubscribeAsync(int cryptoId,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ArgumentOutOfRangeException.ThrowIfLessThan(cryptoId, 1);
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			using (_sync.EnterScope())
			{
				if (_subscriptions.Contains(cryptoId))
					return;
				if (_subscriptions.Count >= 100)
					throw new InvalidOperationException(
						"CoinMarketCap WebSocket allows at most 100 subscriptions per connection.");
				_subscriptions.Add(cryptoId);
			}
			try
			{
				await EnsureConnectedAsync(cancellationToken);
				await SendSubscriptionAsync(cryptoId, true, cancellationToken);
			}
			catch
			{
				using (_sync.EnterScope())
					_subscriptions.Remove(cryptoId);
				throw;
			}
		}
		finally
		{
			_subscriptionGate.Release();
		}
	}

	public async ValueTask UnsubscribeAsync(int cryptoId,
		CancellationToken cancellationToken)
	{
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			using (_sync.EnterScope())
				if (!_subscriptions.Remove(cryptoId))
					return;
			if (_client?.IsConnected == true)
				await SendSubscriptionAsync(cryptoId, false, cancellationToken);
		}
		finally
		{
			_subscriptionGate.Release();
		}
	}

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> SendAsync(new CoinMarketCapSocketRequest
		{
			Id = NextRequestId(),
			Method = CoinMarketCapSocketMethods.Ping,
		}, cancellationToken);

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
			socket.Options.SetRequestHeader("X-CMC_PRO_API_KEY", _apiKey);
			socket.Options.SetRequestHeader("User-Agent",
				"StockSharp-CoinMarketCap/1.0");
		};
		_client = client;
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		this.AddInfoLog("CoinMarketCap WebSocket state: {0}.", state);
		if (state != ConnectionStates.Restored)
			return;
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			int[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var cryptoId in subscriptions)
				await SendSubscriptionAsync(cryptoId, true, cancellationToken);
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
			var envelope =
				JsonConvert.DeserializeObject<CoinMarketCapSocketEnvelope>(json,
					_settings) ?? throw new InvalidDataException(
						"CoinMarketCap WebSocket returned an empty JSON message.");
			switch (envelope.Type)
			{
				case CoinMarketCapSocketMessageTypes.Welcome:
					if (envelope.PingIntervalMilliseconds is > 0 and <= 300000)
					{
						using (_sync.EnterScope())
							_pingInterval = TimeSpan.FromMilliseconds(
								envelope.PingIntervalMilliseconds.Value);
					}
					break;
				case CoinMarketCapSocketMessageTypes.Acknowledgement:
					if (envelope.Code is not null and not 0)
						throw new InvalidOperationException(
							$"CoinMarketCap WebSocket acknowledgement error " +
							$"{envelope.Code}: {envelope.Message.IsEmpty("request failed")}");
					break;
				case CoinMarketCapSocketMessageTypes.Data:
					if (envelope.Channel !=
						CoinMarketCapSocketChannels.LatestPrice ||
						envelope.Data is null || envelope.Data.CryptoId <= 0 ||
						envelope.Timestamp is null or < 0 or > 253402300799999)
						throw new InvalidDataException(
							"CoinMarketCap WebSocket returned an invalid price message.");
					if (PriceReceived is { } priceHandler)
						await priceHandler(envelope.Data,
							envelope.Timestamp.Value, cancellationToken);
					break;
				case CoinMarketCapSocketMessageTypes.Error:
					throw CreateSocketException(envelope.Status);
				case CoinMarketCapSocketMessageTypes.Pong:
					break;
				case null:
					throw new InvalidDataException(
						"CoinMarketCap WebSocket message has no type.");
				default:
					throw new InvalidDataException(
						"CoinMarketCap WebSocket returned an unsupported message type.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask SendSubscriptionAsync(int cryptoId, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(new CoinMarketCapSocketRequest
		{
			Id = NextRequestId(),
			Method = isSubscribe
				? CoinMarketCapSocketMethods.Subscribe
				: CoinMarketCapSocketMethods.Unsubscribe,
			Channel = CoinMarketCapSocketChannels.LatestPrice,
			Parameters = new()
			{
				CryptoIds = [cryptoId],
			},
		}, cancellationToken);

	private async ValueTask SendAsync(CoinMarketCapSocketRequest request,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"CoinMarketCap WebSocket is not connected.");
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

	private long NextRequestId() => Interlocked.Increment(ref _requestId);

	private static InvalidOperationException CreateSocketException(
		CoinMarketCapStatus status)
	{
		if (status is null)
			return new InvalidOperationException(
				"CoinMarketCap WebSocket rejected a request.");
		var message = status.ErrorMessage.IsEmpty("request failed");
		if (!status.ErrorDetail.IsEmpty())
			message += " " + status.ErrorDetail;
		return new InvalidOperationException(
			$"CoinMarketCap WebSocket error {status.ErrorCode}: {message}");
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
			_pingInterval = TimeSpan.FromSeconds(10);
		}
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
