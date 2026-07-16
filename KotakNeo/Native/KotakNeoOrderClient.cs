namespace StockSharp.KotakNeo.Native;

sealed class KotakNeoOrderClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly string _token;
	private readonly string _sid;

	public KotakNeoOrderClient(KotakNeoSession session, int reconnectAttempts, WorkingTime workingTime)
	{
		if (session == null)
			throw new ArgumentNullException(nameof(session));
		_token = session.Token.ThrowIfEmpty(nameof(session.Token));
		_sid = session.Sid.ThrowIfEmpty(nameof(session.Sid));

		_client = new(
			GetUrl(session.DataCenter),
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

	public override string Name => nameof(KotakNeo) + "_" + nameof(KotakNeoOrderClient);

	public event Func<KotakNeoOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public void Disconnect() => _client.Disconnect();

	public ValueTask SendHeartbeat(CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(new KotakNeoHeartbeat { Type = "HB" }), cancellationToken);

	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(new KotakNeoOrderSocketLogin
		{
			Authorization = _token,
			Sid = _sid,
		}), cancellationToken);

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var text = message.AsString();
		if (text.IsEmpty())
			return;

		KotakNeoOrderStreamMessage[] messages;
		if (text.TrimStart().StartsWith("[", StringComparison.Ordinal))
			messages = JsonConvert.DeserializeObject<KotakNeoOrderStreamMessage[]>(text) ?? [];
		else
			messages = [JsonConvert.DeserializeObject<KotakNeoOrderStreamMessage>(text)
				?? throw new InvalidDataException("Kotak Neo returned an empty order-feed message.")];

		foreach (var streamMessage in messages)
		{
			if (!streamMessage.Message.IsEmpty() && streamMessage.Data == null && streamMessage.OrderId.IsEmpty() &&
				!streamMessage.Type.EqualsIgnoreCase("cn") && !streamMessage.Type.EqualsIgnoreCase("HB"))
				throw new InvalidOperationException($"Kotak Neo order stream error: {streamMessage.Message}");

			var order = streamMessage.Data ?? streamMessage;
			if (!order.OrderId.IsEmpty() && OrderReceived is { } handler)
				await handler(order, cancellationToken);
		}
	}

	private static string GetUrl(string dataCenter)
		=> dataCenter?.ToLowerInvariant() switch
		{
			"adc" => "wss://cis.kotaksecurities.com/realtime",
			"e21" => "wss://e21.kotaksecurities.com/realtime",
			"e22" => "wss://e22.kotaksecurities.com/realtime",
			"e41" => "wss://e41.kotaksecurities.com/realtime",
			"e43" => "wss://e43.kotaksecurities.com/realtime",
			_ => "wss://mis.kotaksecurities.com/realtime",
		};
}
