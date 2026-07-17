namespace StockSharp.AliceBlue.Native;

sealed class AliceBlueOrderClient : BaseLogReceiver
{
	private const string _url = "wss://a3.aliceblueonline.com/open-api/order-notify/websocket";
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly string _userId;
	private readonly string _orderToken;
	private TaskCompletionSource<bool> _loginCompletion;

	public AliceBlueOrderClient(string userId, string orderToken, int reconnectAttempts, WorkingTime workingTime)
	{
		_userId = userId.ThrowIfEmpty(nameof(userId));
		_orderToken = orderToken.ThrowIfEmpty(nameof(orderToken));

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

	public override string Name => nameof(AliceBlue) + "_" + nameof(AliceBlueOrderClient);

	public event Func<AliceBlueOrderUpdate, CancellationToken, ValueTask> OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask Connect(CancellationToken cancellationToken)
	{
		_loginCompletion = CreateCompletion();
		await _client.ConnectAsync(cancellationToken);
		await _loginCompletion.Task.WaitAsync(cancellationToken);
	}

	public ValueTask Disconnect(CancellationToken cancellationToken)
		=> _client.DisconnectAsync(cancellationToken);

	public ValueTask SendHeartbeat(CancellationToken cancellationToken)
		=> Send(new AliceBlueOrderSocketHeartbeat { UserId = _userId }, cancellationToken);

	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		if (reconnect || _loginCompletion == null || _loginCompletion.Task.IsCompleted)
			_loginCompletion = CreateCompletion();

		return Send(new AliceBlueOrderSocketLogin
		{
			UserId = _userId,
			OrderToken = _orderToken,
		}, cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var content = message.AsString()?.Trim();
		if (content.IsEmpty())
			return;

		var envelope = JsonConvert.DeserializeObject<AliceBlueOrderSocketEnvelope>(content, _jsonSettings)
			?? throw new InvalidDataException("Alice Blue returned an empty order WebSocket message.");
		if (envelope.Type.IsEmpty() && !envelope.Status.IsEmpty())
		{
			if (!envelope.Status.EqualsIgnoreCase("Ok"))
			{
				var error = new InvalidOperationException($"Alice Blue order WebSocket login failed: {envelope.Status}");
				_loginCompletion?.TrySetException(error);
				throw error;
			}
			_loginCompletion?.TrySetResult(true);
			return;
		}

		switch (envelope.Type?.ToLowerInvariant())
		{
			case "om":
			{
				var order = JsonConvert.DeserializeObject<AliceBlueOrderUpdate>(content, _jsonSettings)
					?? throw new InvalidDataException("Alice Blue returned an invalid order update.");
				if (!order.OrderId.IsEmpty() && OrderReceived is { } handler)
					await handler(order, cancellationToken);
				break;
			}

			case "h":
			case "ok":
				break;

			default:
				this.AddVerboseLog("Ignored Alice Blue order WebSocket message type {0}.", envelope.Type);
				break;
		}
	}

	private ValueTask Send<T>(T request, CancellationToken cancellationToken)
		where T : class
		=> _client.SendAsync(JsonConvert.SerializeObject(request, Formatting.None, _jsonSettings), cancellationToken);

	private static TaskCompletionSource<bool> CreateCompletion()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);
}
