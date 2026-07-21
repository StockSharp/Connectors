namespace StockSharp.GainsNetwork.Native;

sealed class GainsNetworkSocketClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public GainsNetworkSocketClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "wss://" + endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("ws" or "wss"))
			throw new ArgumentException(
				"Gains price endpoint must use WS or WSS.", nameof(endpoint));
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "GainsNetwork_WS";

	public event Func<GainsPriceFrame, CancellationToken, ValueTask>
		PriceReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Gains WebSocket is already initialized.");
		WebSocketClient client = null;
		client = new WebSocketClient(_endpoint.ToString(),
			(state, token) => OnStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(_, message, token) => OnProcessAsync(message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a), static (s, a) => { })
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		client.Init += static socket => socket.Options.SetRequestHeader(
			"User-Agent", "StockSharp-GainsNetwork-Connector/1.0");
		_client = client;
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			await DisconnectAsync(cancellationToken);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
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

	private async ValueTask OnProcessAsync(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var frame = JsonConvert.DeserializeObject<GainsPriceFrame>(payload,
				_settings) ?? throw new InvalidDataException(
					"Gains returned an empty price frame.");
			if (PriceReceived is { } handler)
				await handler(frame, cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await RaiseErrorAsync(new InvalidDataException(
				"Failed to process a Gains price frame.", error),
				cancellationToken);
		}
	}

	private ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
		=> StateChanged is { } handler
			? handler(state, cancellationToken)
			: default;

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		base.DisposeManaged();
	}
}
