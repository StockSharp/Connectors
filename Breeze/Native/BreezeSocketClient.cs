namespace StockSharp.Breeze.Native;

abstract class BreezeSocketClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly string _user;
	private readonly string _token;
	private readonly SynchronizedSet<string> _rooms = new(StringComparer.OrdinalIgnoreCase);
	private bool _authenticated;

	protected BreezeSocketClient(string url, string user, string token, int reconnectAttempts, WorkingTime workingTime)
	{
		_user = user.ThrowIfEmpty(nameof(user));
		_token = token.ThrowIfEmpty(nameof(token));
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
	public ValueTask SendHeartbeat(CancellationToken cancellationToken) => default;

	protected async ValueTask AddRoom(string symbol, CancellationToken cancellationToken)
	{
		if (_rooms.Contains(symbol))
			return;
		_rooms.Add(symbol);
		if (!_authenticated)
			return;
		await SendRoom("join", [symbol], cancellationToken);
	}

	protected async ValueTask RemoveRoom(string symbol, CancellationToken cancellationToken)
	{
		if (!_rooms.Remove(symbol) || !_authenticated)
			return;
		await SendRoom("leave", [symbol], cancellationToken);
	}

	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		_authenticated = false;
		return default;
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var text = message.AsString();
		if (text.IsEmpty())
			return;
		if (text == "2")
		{
			await _client.SendAsync("3", cancellationToken);
			return;
		}
		if (text[0] == '0')
		{
			await _client.SendAsync(BreezeSocketCodec.CreateConnect(_user, _token), cancellationToken);
			return;
		}
		if (text.StartsWith("40", StringComparison.Ordinal))
		{
			_authenticated = true;
			var rooms = _rooms.ToArray();
			for (var i = 0; i < rooms.Length; i += 100)
				await SendRoom("join", rooms.Skip(i).Take(100).ToArray(), cancellationToken);
			return;
		}
		if (text.StartsWith("44", StringComparison.Ordinal))
			throw new InvalidOperationException($"Breeze Socket.IO authentication error: {text[2..]}");
		if (text.StartsWith("42", StringComparison.Ordinal))
			await ProcessEvent(text, cancellationToken);
	}

	private ValueTask SendRoom(string eventName, string[] symbols, CancellationToken cancellationToken)
		=> _client.SendAsync(BreezeSocketCodec.CreateRoomRequest(new BreezeRoomRequest { Event = eventName, Symbols = symbols }), cancellationToken);

	protected abstract ValueTask ProcessEvent(string message, CancellationToken cancellationToken);
}
