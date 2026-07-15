namespace StockSharp.Dhan.Native;

sealed class DhanOrderClient : BaseLogReceiver
{
	private const string _url = "wss://api-order-update.dhan.co";

	private readonly WebSocketClient _client;
	private readonly string _clientId;
	private readonly string _token;

	public DhanOrderClient(string clientId, string token, int reconnectAttempts, WorkingTime workingTime)
	{
		_clientId = clientId.ThrowIfEmpty(nameof(clientId));
		_token = token.ThrowIfEmpty(nameof(token));

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
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(Dhan) + "_" + nameof(DhanOrderClient);

	public event Func<DhanOrderUpdateData, CancellationToken, ValueTask> OrderReceived;
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

	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(new DhanOrderLogin
		{
			Login = new DhanOrderLoginRequest
			{
				MessageCode = 42,
				ClientId = _clientId,
				Token = _token,
			},
			UserType = "SELF",
		}), cancellationToken);

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var text = message.AsString();
		if (text.IsEmpty())
			return;

		var update = JsonConvert.DeserializeObject<DhanOrderUpdate>(text)
			?? throw new InvalidOperationException("Dhan returned an empty order update.");
		if (!update.Message.IsEmpty() && !update.Type.EqualsIgnoreCase("order_alert"))
			throw new InvalidOperationException($"Dhan order stream error: {update.Message}");
		if (update.Type.EqualsIgnoreCase("order_alert") && update.Data != null && !update.Data.OrderId.IsEmpty() && OrderReceived is { } handler)
			await handler(update.Data, cancellationToken);
	}
}
