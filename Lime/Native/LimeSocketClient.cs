namespace StockSharp.Lime.Native;

sealed class LimeSocketClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly SecureString _accessToken;
	private readonly SynchronizedSet<(LimeFeedActions action, string account)> _subscriptions = [];

	public LimeSocketClient(SecureString accessToken, int reconnectAttempts, WorkingTime workingTime)
	{
		_accessToken = accessToken.ThrowIfEmpty(nameof(accessToken));
		_client = new(
			"wss://api.lime.co/accounts",
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
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(Lime) + "_" + nameof(LimeSocketClient);

	public event Func<LimeAccount, CancellationToken, ValueTask> BalanceReceived;
	public event Func<LimeAccountPositions, CancellationToken, ValueTask> PositionsReceived;
	public event Func<LimeOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<LimeTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.Init -= OnInit;
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	private void OnInit(ClientWebSocket socket)
		=> socket.Options.SetRequestHeader("Authorization", $"Bearer {_accessToken.UnSecure()}");

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public void Disconnect() => _client.Disconnect();

	public ValueTask SendHeartbeat(CancellationToken cancellationToken)
	{
		_client.SendOpCode(0x9);
		return default;
	}

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		foreach (var subscription in _subscriptions.ToArray())
			await Send(subscription.action, subscription.account, cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var raw = message.AsString();
		if (raw.IsEmpty())
			return;

		var header = JsonConvert.DeserializeObject<LimeFeedHeader>(raw, _jsonSettings);
		if (header == null)
			throw new InvalidOperationException("Lime returned an empty account-feed message.");

		switch (header.Type)
		{
			case LimeFeedTypes.Balance:
				foreach (var account in JsonConvert.DeserializeObject<LimeBalanceFeed>(raw, _jsonSettings)?.Data ?? [])
					if (BalanceReceived is { } balanceHandler)
						await balanceHandler(account, cancellationToken);
				break;
			case LimeFeedTypes.Position:
				foreach (var positions in JsonConvert.DeserializeObject<LimePositionFeed>(raw, _jsonSettings)?.Data ?? [])
					if (PositionsReceived is { } positionsHandler)
						await positionsHandler(positions, cancellationToken);
				break;
			case LimeFeedTypes.Order:
				foreach (var order in JsonConvert.DeserializeObject<LimeOrderFeed>(raw, _jsonSettings)?.Data ?? [])
					if (OrderReceived is { } orderHandler)
						await orderHandler(order, cancellationToken);
				break;
			case LimeFeedTypes.Trade:
				foreach (var trade in JsonConvert.DeserializeObject<LimeTradeFeed>(raw, _jsonSettings)?.Data ?? [])
					if (TradeReceived is { } tradeHandler)
						await tradeHandler(trade, cancellationToken);
				break;
			case LimeFeedTypes.Error:
				if (Error is { } errorHandler)
					await errorHandler(new InvalidOperationException($"Lime account feed error {header.Code}: {header.Description}"), cancellationToken);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(header.Type), header.Type, "Unknown Lime account-feed message type.");
		}
	}

	public async ValueTask Subscribe(LimeFeedActions action, string account, CancellationToken cancellationToken)
	{
		var key = (action, account);
		if (_subscriptions.Contains(key))
			return;
		_subscriptions.Add(key);
		await Send(action, account, cancellationToken);
	}

	private ValueTask Send(LimeFeedActions action, string account, CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(new LimeFeedAction
		{
			Action = action,
			Account = account,
		}, _jsonSettings), cancellationToken);
}
