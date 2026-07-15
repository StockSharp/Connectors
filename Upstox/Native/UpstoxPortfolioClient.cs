namespace StockSharp.Upstox.Native;

sealed class UpstoxPortfolioClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;

	public UpstoxPortfolioClient(string url, int reconnectAttempts, WorkingTime workingTime)
	{
		_client = new(
			url.ThrowIfEmpty(nameof(url)),
			(state, token) => StateChanged is { } stateHandler ? stateHandler(state, token) : default,
			(error, token) => Error is { } errorHandler ? errorHandler(error, token) : default,
			Process,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
		};
	}

	public override string Name => nameof(Upstox) + "_" + nameof(UpstoxPortfolioClient);

	public event Func<UpstoxPortfolioUpdate, CancellationToken, ValueTask> UpdateReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public void Disconnect() => _client.Disconnect();

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var raw = message.AsString();
		if (raw.IsEmpty())
			return;

		var update = JsonConvert.DeserializeObject<UpstoxPortfolioUpdate>(raw)
			?? throw new InvalidOperationException("Upstox returned an empty portfolio update.");

		if (UpdateReceived is { } handler)
			await handler(update, cancellationToken);
	}
}
