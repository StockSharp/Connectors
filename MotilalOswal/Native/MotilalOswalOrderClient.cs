namespace StockSharp.MotilalOswal.Native;

sealed class MotilalOswalOrderClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly string _clientCode;
	private readonly string _authToken;
	private readonly string _apiKey;

	public MotilalOswalOrderClient(bool isDemo, string clientCode, SecureString authToken, SecureString apiKey,
		int reconnectAttempts, WorkingTime workingTime)
	{
		_clientCode = clientCode.ThrowIfEmpty(nameof(clientCode));
		_authToken = authToken.ThrowIfEmpty(nameof(authToken)).UnSecure();
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure();
		var url = isDemo ? "wss://openapi.motilaloswaluat.com/ws" : "wss://openapi.motilaloswal.com/ws";

		_client = new(
			url,
			(state, cancellationToken) => StateChanged is { } stateHandler ? stateHandler(state, cancellationToken) : default,
			(error, cancellationToken) => Error is { } errorHandler ? errorHandler(error, cancellationToken) : default,
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

	public override string Name => nameof(MotilalOswal) + "_" + nameof(MotilalOswalOrderClient);

	public event Func<MotilalOswalTradeStreamEvent, CancellationToken, ValueTask> OrderReceived;
	public event Func<MotilalOswalTradeStreamEvent, CancellationToken, ValueTask> TradeReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);

	public async ValueTask Disconnect(CancellationToken cancellationToken)
	{
		try
		{
			await SendAction("TradeUnsubscribe", cancellationToken);
			await SendAction("OrderUnsubscribe", cancellationToken);
			await SendAction("logout", cancellationToken);
		}
		catch (InvalidOperationException)
		{
		}
		await _client.DisconnectAsync(cancellationToken);
	}

	public ValueTask SendHeartbeat(CancellationToken cancellationToken)
		=> SendAction("heartbeat", cancellationToken);

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		await _client.SendAsync(JsonConvert.SerializeObject(new MotilalOswalTradeSocketLogin
		{
			ClientId = _clientCode,
			AuthToken = _authToken,
			ApiKey = _apiKey,
		}, _jsonSettings), cancellationToken);
		await SendAction("TradeSubscribe", cancellationToken);
		await SendAction("OrderSubscribe", cancellationToken);
	}

	private ValueTask SendAction(string action, CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(new MotilalOswalTradeSocketAction
		{
			ClientId = _clientCode,
			Action = action,
		}, _jsonSettings), cancellationToken);

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var text = message.AsString()?.Trim();
		if (text.IsEmpty())
			return;
		if (text[0] != '{')
			throw new InvalidDataException($"Unexpected Motilal Oswal order-stream message: {text}");

		var update = JsonConvert.DeserializeObject<MotilalOswalTradeStreamEvent>(text)
			?? throw new InvalidOperationException("Motilal Oswal returned an empty order-stream event.");
		if (!update.ErrorCode.IsEmpty() || update.Status?.Equals("ERROR", StringComparison.OrdinalIgnoreCase) == true ||
			update.Status?.Equals("FAILURE", StringComparison.OrdinalIgnoreCase) == true)
			throw new InvalidOperationException($"Motilal Oswal order-stream error {update.ErrorCode.IsEmpty("UNKNOWN")}: {update.Message.IsEmpty("No error message was returned.")}");

		if (!update.TradeNumber.IsEmpty())
		{
			if (TradeReceived is { } tradeHandler)
				await tradeHandler(update, cancellationToken);
			return;
		}

		if (!update.UniqueOrderId.IsEmpty() && !update.OrderStatus.IsEmpty() && OrderReceived is { } orderHandler)
			await orderHandler(update, cancellationToken);
	}
}
