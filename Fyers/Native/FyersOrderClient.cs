namespace StockSharp.Fyers.Native;

sealed class FyersOrderClient : BaseLogReceiver
{
	private const string _url = "wss://socket.fyers.in/trade/v3";

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly string _authorization;

	public FyersOrderClient(string clientId, string token, int reconnectAttempts, WorkingTime workingTime)
	{
		_authorization = $"{clientId.ThrowIfEmpty(nameof(clientId))}:{token.ThrowIfEmpty(nameof(token))}";
		_client = new(
			_url,
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
		_client.Init += OnInit;
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(Fyers) + "_" + nameof(FyersOrderClient);

	public event Func<FyersOrderStreamData, CancellationToken, ValueTask> OrderReceived;
	public event Func<FyersTradeStreamData, CancellationToken, ValueTask> TradeReceived;
	public event Func<FyersPositionStreamData, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.Init -= OnInit;
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public void Disconnect() => _client.Disconnect();
	public ValueTask SendHeartbeat(CancellationToken cancellationToken) => _client.SendAsync("ping", cancellationToken);

	private void OnInit(ClientWebSocket socket)
		=> socket.Options.SetRequestHeader("authorization", _authorization);

	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(new FyersOrderSubscriptionRequest
		{
			Type = "SUB_ORD",
			Streams = ["orders", "trades", "positions"],
			SubscriptionType = 1,
		}, _jsonSettings), cancellationToken);

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var text = message.AsString();
		if (text.IsEmpty() || text.EqualsIgnoreCase("pong"))
			return;

		var update = JsonConvert.DeserializeObject<FyersOrderStreamMessage>(text)
			?? throw new InvalidOperationException("FYERS returned an empty order-stream message.");
		if (update.Status == FyersResponseStatuses.Error)
			throw new InvalidOperationException($"FYERS order stream error {update.Code}: {update.Message}");

		if (update.Order != null && OrderReceived is { } orderHandler)
			await orderHandler(update.Order, cancellationToken);
		if (update.Trade != null && TradeReceived is { } tradeHandler)
			await tradeHandler(update.Trade, cancellationToken);
		if (update.Position != null && PositionReceived is { } positionHandler)
			await positionHandler(update.Position, cancellationToken);
	}
}
