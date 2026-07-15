namespace StockSharp.AngelOne.Native;

sealed class AngelOneOrderClient : BaseLogReceiver
{
	private const string _url = "wss://tns.angelone.in/smart-order-update";

	private readonly WebSocketClient _client;
	private readonly string _jwtToken;
	private readonly string _apiKey;
	private readonly string _clientCode;
	private readonly string _feedToken;

	public AngelOneOrderClient(string jwtToken, string apiKey, string clientCode, string feedToken,
		int reconnectAttempts, WorkingTime workingTime)
	{
		_jwtToken = jwtToken.ThrowIfEmpty(nameof(jwtToken));
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_clientCode = clientCode.ThrowIfEmpty(nameof(clientCode));
		_feedToken = feedToken.ThrowIfEmpty(nameof(feedToken));

		_client = new(
			_url,
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
		_client.Init += OnInit;
	}

	public override string Name => nameof(AngelOne) + "_" + nameof(AngelOneOrderClient);

	public event Func<AngelOneOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.Init -= OnInit;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public void Disconnect() => _client.Disconnect();
	public ValueTask SendHeartbeat(CancellationToken cancellationToken) => _client.SendAsync("ping", cancellationToken);

	private void OnInit(ClientWebSocket socket)
	{
		socket.Options.SetRequestHeader("Authorization", _jwtToken);
		socket.Options.SetRequestHeader("x-api-key", _apiKey);
		socket.Options.SetRequestHeader("x-client-code", _clientCode);
		socket.Options.SetRequestHeader("x-feed-token", _feedToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var text = message.AsString();
		if (text.IsEmpty() || text.EqualsIgnoreCase("pong"))
			return;

		var update = JsonConvert.DeserializeObject<AngelOneOrderUpdate>(text)
			?? throw new InvalidOperationException("Angel One returned an empty order update.");
		if (!update.ErrorMessage.IsEmpty())
			throw new InvalidOperationException($"Angel One order stream error {update.StatusCode}: {update.ErrorMessage}");

		if (update.Order != null && !update.Order.OrderId.IsEmpty() && OrderReceived is { } handler)
			await handler(update.Order, cancellationToken);
	}
}
