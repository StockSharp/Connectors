namespace StockSharp.TastyTrade.Native;

sealed class TastyTradeAccountStreamer : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly TastyTradeClient _httpClient;
	private readonly string[] _accounts;
	private CancellationTokenSource _heartbeatCts;
	private Task _heartbeatTask;
	private long _requestId;

	public TastyTradeAccountStreamer(TastyTradeClient httpClient, string address, IEnumerable<string> accounts, WorkingTime workingTime, int reconnectAttempts)
	{
		_httpClient = httpClient;
		_accounts = accounts.Where(a => !a.IsEmpty()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		_client = new WebSocketClient(
			address,
			(state, token) => StateChanged is { } stateHandler ? stateHandler(state, token) : default,
			(error, token) => Error is { } errorHandler ? errorHandler(error, token) : default,
			Process,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			SendSettings = new() { NullValueHandling = NullValueHandling.Ignore },
		};
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(TastyTrade) + "_" + nameof(TastyTradeAccountStreamer);

	public event Func<TastyOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<TastyPosition, CancellationToken, ValueTask> PositionReceived;
	public event Func<TastyBalance, CancellationToken, ValueTask> BalanceReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await _client.ConnectAsync(cancellationToken);
		_heartbeatCts = new CancellationTokenSource();
		_heartbeatTask = Heartbeat(_heartbeatCts.Token);
	}

	public void Disconnect()
	{
		_heartbeatCts?.Cancel();
		_heartbeatCts?.Dispose();
		_heartbeatCts = null;
		_heartbeatTask = null;
		_client.Disconnect();
	}

	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
		=> Send(TastyAccountActions.Connect, _accounts, cancellationToken);

	private async Task Heartbeat(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
				await Send(TastyAccountActions.Heartbeat, null, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				if (Error is { } handler)
					await handler(ex, CancellationToken.None);
			}
		}
	}

	private async ValueTask Send(TastyAccountActions action, string[] accounts, CancellationToken cancellationToken)
	{
		await _client.SendAsync(new TastyAccountRequest
		{
			Action = action,
			Value = accounts,
			AuthToken = await _httpClient.GetAuthorization(cancellationToken),
			RequestId = Interlocked.Increment(ref _requestId),
			Source = "StockSharp",
		}, cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var raw = message.AsString();
		if (raw.IsEmpty())
			return;

		var header = JsonConvert.DeserializeObject<TastyAccountHeader>(raw);
		if (header is not null && !header.Message.IsEmpty() && !header.Status.EqualsIgnoreCase("ok"))
		{
			if (Error is { } errorHandler)
				await errorHandler(new InvalidOperationException(header.Message), cancellationToken);
			return;
		}

		if (header?.Type == TastyAccountEventTypes.Order)
			await Dispatch(JsonConvert.DeserializeObject<TastyAccountEvent<TastyOrder>>(raw)?.Data, OrderReceived, cancellationToken);
		else if (header?.Type == TastyAccountEventTypes.CurrentPosition)
			await Dispatch(JsonConvert.DeserializeObject<TastyAccountEvent<TastyPosition>>(raw)?.Data, PositionReceived, cancellationToken);
		else if (header?.Type == TastyAccountEventTypes.AccountBalance)
			await Dispatch(JsonConvert.DeserializeObject<TastyAccountEvent<TastyBalance>>(raw)?.Data, BalanceReceived, cancellationToken);

		if (header?.Results?.Length > 0)
		{
			foreach (var item in JsonConvert.DeserializeObject<TastyAccountEvents<TastyOrder>>(raw)?.Results?.Where(r => r.Type == TastyAccountEventTypes.Order) ?? [])
				await Dispatch(item.Data, OrderReceived, cancellationToken);
			foreach (var item in JsonConvert.DeserializeObject<TastyAccountEvents<TastyPosition>>(raw)?.Results?.Where(r => r.Type == TastyAccountEventTypes.CurrentPosition) ?? [])
				await Dispatch(item.Data, PositionReceived, cancellationToken);
			foreach (var item in JsonConvert.DeserializeObject<TastyAccountEvents<TastyBalance>>(raw)?.Results?.Where(r => r.Type == TastyAccountEventTypes.AccountBalance) ?? [])
				await Dispatch(item.Data, BalanceReceived, cancellationToken);
		}
	}

	private static ValueTask Dispatch<T>(T data, Func<T, CancellationToken, ValueTask> handler, CancellationToken cancellationToken)
		=> data is null || handler is null ? default : handler(data, cancellationToken);

	protected override void DisposeManaged()
	{
		Disconnect();
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}
}
