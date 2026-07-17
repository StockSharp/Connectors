namespace StockSharp.FivePaisa.Native;

sealed class FivePaisaDepthClient : BaseLogReceiver
{
	private const string _url = "wss://gateway.5paisa.com/openapi/20depth";

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly SynchronizedSet<string> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

	public FivePaisaDepthClient(string token, int reconnectAttempts, WorkingTime workingTime)
	{
		token.ThrowIfEmpty(nameof(token));
		var url = $"{_url}?access_token={Uri.EscapeDataString(token)}";
		_client = new(
			url,
			(state, cancellationToken) => default,
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

	public override string Name => nameof(FivePaisa) + "_" + nameof(FivePaisaDepthClient);

	public event Func<FivePaisaDepthUpdate, CancellationToken, ValueTask> DepthReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public ValueTask Disconnect(CancellationToken cancellationToken) => _client.DisconnectAsync(cancellationToken);

	public async ValueTask Subscribe(string instrumentKey, CancellationToken cancellationToken)
	{
		instrumentKey.ToDepthInstrument();
		if (!_subscriptions.TryAdd(instrumentKey))
			return;
		await Send(true, [instrumentKey], cancellationToken);
	}

	public async ValueTask Unsubscribe(string instrumentKey, CancellationToken cancellationToken)
	{
		if (!_subscriptions.Remove(instrumentKey))
			return;
		await Send(false, [instrumentKey], cancellationToken);
	}

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		var subscriptions = _subscriptions.ToArray();
		for (var i = 0; i < subscriptions.Length; i += 50)
			await Send(true, subscriptions.Skip(i).Take(50).ToArray(), cancellationToken);
	}

	private ValueTask Send(bool subscribe, string[] instrumentKeys, CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(new FivePaisaDepthRequest
		{
			Method = subscribe ? "Subscribe" : "Unsubscribe",
			Operation = "20Depth",
			Instruments = [.. instrumentKeys.Select(key => key.ToDepthInstrument())],
		}, _jsonSettings), cancellationToken);

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var text = message.AsString()?.Trim();
		if (text.IsEmpty())
			return;

		FivePaisaDepthUpdate[] updates;
		if (text[0] == '[')
			updates = JsonConvert.DeserializeObject<FivePaisaDepthUpdate[]>(text);
		else if (text[0] == '{')
			updates = [JsonConvert.DeserializeObject<FivePaisaDepthUpdate>(text)];
		else
			throw new InvalidDataException($"Unexpected 5paisa depth message: {text}");

		if (updates == null || DepthReceived is not { } handler)
			return;
		foreach (var update in updates)
		{
			if (update != null && (update.Token > 0 || update.ScripCode > 0) && update.Details != null)
				await handler(update, cancellationToken);
		}
	}
}
