namespace StockSharp.CoinsPh.Native;

sealed class CoinsPhPrivateSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly CoinsPhRestClient _restClient;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};
	private WebSocketClient _client;
	private CancellationTokenSource _keepAliveCancellation;
	private Task _keepAliveTask;
	private string _listenKey;

	public CoinsPhPrivateSocketClient(string endpoint,
		CoinsPhRestClient restClient, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_restClient = restClient ?? throw new ArgumentNullException(
			nameof(restClient));
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "CoinsPh_PrivateSocket";

	public event Func<CoinsPhUserStreamMessage, CancellationToken, ValueTask>
		MessageReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_keepAliveCancellation?.Cancel();
		_keepAliveCancellation?.Dispose();
		_client?.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Coins.ph private WebSocket is already initialized.");
		var listenKey = await _restClient.CreateListenKeyAsync(cancellationToken);
		_listenKey = listenKey?.Value.ThrowIfEmpty(nameof(listenKey));
		var client = _client = CreateClient(CreateEndpoint(_endpoint, _listenKey));
		try
		{
			await client.ConnectAsync(cancellationToken);
			_keepAliveCancellation = new();
			_keepAliveTask = RunKeepAliveAsync(_keepAliveCancellation.Token);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	private WebSocketClient CreateClient(string endpoint)
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			endpoint,
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
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-CoinsPh-Connector/1.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		var cancellation = _keepAliveCancellation;
		_keepAliveCancellation = null;
		if (cancellation is not null)
		{
			cancellation.Cancel();
			try
			{
				if (_keepAliveTask is not null)
					await _keepAliveTask;
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				cancellation.Dispose();
				_keepAliveTask = null;
			}
		}

		var client = _client;
		_client = null;
		if (client is not null)
		{
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

		var listenKey = _listenKey;
		_listenKey = null;
		if (!listenKey.IsEmpty())
		{
			try
			{
				await _restClient.CloseListenKeyAsync(new()
				{
					ListenKey = listenKey,
				}, cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
		}
	}

	private ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		_ = client;
		return StateChanged is { } handler
			? handler(state, cancellationToken)
			: default;
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var value = JsonConvert.DeserializeObject<CoinsPhUserStreamMessage>(
				payload, _jsonSettings) ?? throw new InvalidDataException(
					"Coins.ph user stream returned an empty message.");
			if (!value.Event.IsEmpty() && MessageReceived is { } handler)
				await handler(value, cancellationToken);
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async Task RunKeepAliveAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromMinutes(25), cancellationToken);
			try
			{
				await _restClient.KeepAliveListenKeyAsync(new()
				{
					ListenKey = _listenKey,
				}, cancellationToken);
			}
			catch (OperationCanceledException) when (
				cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception error)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static string CreateEndpoint(string endpoint, string listenKey)
	{
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			!uri.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"Coins.ph WebSocket endpoint must be an absolute WSS URI.",
				nameof(endpoint));
		return new UriBuilder(uri)
		{
			Path = "/openapi/ws/" + Uri.EscapeDataString(
				listenKey.ThrowIfEmpty(nameof(listenKey))),
			Query = string.Empty,
		}.Uri.AbsoluteUri;
	}
}
