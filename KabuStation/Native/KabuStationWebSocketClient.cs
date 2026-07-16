namespace StockSharp.KabuStation.Native;

internal sealed class KabuStationWebSocketClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;

	public KabuStationWebSocketClient(bool isDemo, int reconnectAttempts, WorkingTime workingTime)
	{
		_client = new(
			$"ws://localhost:{(isDemo ? 18081 : 18080)}/kabusapi/websocket",
			(state, token) => StateChanged is { } stateHandler ? stateHandler(state, token) : default,
			(error, token) => Error is { } errorHandler ? errorHandler(error, token) : default,
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

	public override string Name => nameof(KabuStation) + "_WebSocket";

	public event Func<KabuStationBoard, CancellationToken, ValueTask> BoardReceived;
	public event Func<bool, CancellationToken, ValueTask> Connected;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public void Disconnect() => _client.Disconnect();

	public ValueTask SendHeartbeat(CancellationToken cancellationToken)
	{
		_client.SendOpCode(0x9);
		return default;
	}

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
		=> Connected is { } handler ? handler(reconnect, cancellationToken) : default;

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var content = message.AsString();
		if (content.IsEmpty())
			return;
		var board = JsonConvert.DeserializeObject<KabuStationBoard>(content)
			?? throw new InvalidDataException("kabu Station returned an invalid PUSH message.");
		if (BoardReceived is { } handler)
			await handler(board, cancellationToken);
	}
}
