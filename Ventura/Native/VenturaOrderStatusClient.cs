namespace StockSharp.Ventura.Native;

sealed class VenturaOrderStatusClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;

	public VenturaOrderStatusClient(
		Uri address,
		SecureString appKey,
		string clientId,
		SecureString authToken,
		int reconnectAttempts,
		WorkingTime workingTime)
	{
		var endpoint = VenturaMarketDataClient.AddCredentials(
			address ?? throw new ArgumentNullException(nameof(address)),
			appKey.ThrowIfEmpty(nameof(appKey)).UnSecure(),
			clientId.ThrowIfEmpty(nameof(clientId)),
			authToken.ThrowIfEmpty(nameof(authToken)).UnSecure());
		_client = new(
			endpoint.AbsoluteUri,
			(state, cancellationToken) =>
				StateChanged is { } stateHandler
					? stateHandler(state, cancellationToken)
					: default,
			(error, cancellationToken) =>
				Error is { } errorHandler
					? errorHandler(error, cancellationToken)
					: default,
			Process,
			(message, args) => this.AddInfoLog(message, args),
			(message, args) => this.AddErrorLog(message, args),
			(message, args) => this.AddVerboseLog(message, args))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			DisableAutoResend = true,
		};
		_client.InitAsync += OnInit;
	}

	public override string Name => nameof(Ventura) + "_" +
		nameof(VenturaOrderStatusClient);

	public event Func<VenturaOrderStatusUpdate, CancellationToken, ValueTask>
		OrderStatusReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;

	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	protected override void DisposeManaged()
	{
		_client.InitAsync -= OnInit;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public ValueTask Disconnect(CancellationToken cancellationToken)
		=> _client.DisconnectAsync(cancellationToken);

	private ValueTask OnInit(
		ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
		return default;
	}

	private async ValueTask Process(
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var text = message.AsString()?.Trim();
		if (text.IsEmpty() || text.EqualsIgnoreCase("pong"))
			return;
		VenturaOrderStatusUpdate[] updates;
		try
		{
			updates = Decode(text, DateTime.UtcNow);
		}
		catch (InvalidDataException error)
		{
			if (Error is { } errorHandler)
				await errorHandler(error, cancellationToken);
			return;
		}
		if (OrderStatusReceived is not { } handler)
			return;
		foreach (var update in updates)
			await handler(update, cancellationToken);
	}

	internal static VenturaOrderStatusUpdate[] Decode(
		string json,
		DateTime fallback)
	{
		JToken token;
		try
		{
			token = JToken.Parse(json.ThrowIfEmpty(nameof(json)));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Ventura EaseAPI order-status WebSocket returned invalid JSON.",
				error);
		}
		if (token is JObject status)
		{
			var error = VenturaRestClient.FindString(
				status,
				"error",
				"detail");
			if (!error.IsEmpty())
			{
				throw new InvalidDataException(
					$"Ventura EaseAPI order-status WebSocket: {error}");
			}
			return [];
		}
		if (token is not JArray array)
			return [];
		var rows = array.FirstOrDefault() is JArray
			? array.OfType<JArray>()
			: [array];
		return
		[
			..
				rows
					.Where(row => row.Count >= 8)
					.Select(row => new VenturaOrderStatusUpdate
					{
						Message = row[0]?.Value<string>(),
						SecurityId = row[1]?.Value<string>(),
						OrderId = row[2]?.Value<string>(),
						TradedQuantity =
							VenturaRestClient.DecimalAt(row, 3),
						TotalQuantity =
							VenturaRestClient.DecimalAt(row, 4),
						OrderPrice =
							VenturaRestClient.DecimalAt(row, 5),
						TradePrice =
							VenturaRestClient.DecimalAt(row, 6),
						ServerTime = VenturaRestClient.ParseTime(
							row[7],
							fallback),
					})
					.Where(update =>
						!update.Message.IsEmpty() &&
						!update.OrderId.IsEmpty())
		];
	}
}
