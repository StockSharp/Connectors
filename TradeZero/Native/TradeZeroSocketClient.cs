namespace StockSharp.TradeZero.Native;

sealed class TradeZeroSocketClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly TradeZeroStreamKinds _kind;
	private readonly string _apiKey;
	private readonly SecureString _apiSecret;
	private readonly SynchronizedSet<string> _accounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly TaskCompletionSource _authorization = AsyncHelper.CreateTaskCompletionSource();

	public TradeZeroSocketClient(TradeZeroStreamKinds kind, string apiKey, SecureString apiSecret, int reconnectAttempts, WorkingTime workingTime)
	{
		_kind = kind;
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_apiSecret = apiSecret.ThrowIfEmpty(nameof(apiSecret));
		var stream = kind == TradeZeroStreamKinds.Portfolio ? "portfolio" : "pnl";
		_client = new(
			$"wss://webapi.tradezero.com/stream/{stream}",
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
	}

	public override string Name => nameof(TradeZero) + "_" + _kind;

	public event Func<TradeZeroPortfolioMessage, CancellationToken, ValueTask> PortfolioReceived;
	public event Func<string, TradeZeroPnlMessage, CancellationToken, ValueTask> PnlReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await _client.ConnectAsync(cancellationToken);
		await _authorization.Task.WaitAsync(cancellationToken);
	}

	public void Disconnect() => _client.Disconnect();

	public async ValueTask Subscribe(string accountId, CancellationToken cancellationToken)
	{
		if (_accounts.Contains(accountId))
			return;
		_accounts.Add(accountId);

		if (_authorization.Task.IsCompletedSuccessfully)
			await SendSubscription(accountId, cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var raw = message.AsString();
		if (raw.IsEmpty())
			return;

		var system = JsonConvert.DeserializeObject<TradeZeroSocketSystemMessage>(raw);
		if (system?.IsSystem == true)
		{
			await ProcessSystem(system, cancellationToken);
			return;
		}

		if (_kind == TradeZeroStreamKinds.Portfolio)
		{
			var portfolio = JsonConvert.DeserializeObject<TradeZeroPortfolioMessage>(raw);
			if (portfolio != null && PortfolioReceived is { } portfolioHandler)
				await portfolioHandler(portfolio, cancellationToken);
		}
		else
		{
			var pnl = JsonConvert.DeserializeObject<TradeZeroPnlMessage>(raw);
			if (pnl != null && PnlReceived is { } pnlHandler)
				await pnlHandler(ResolveAccount(pnl.Account), pnl, cancellationToken);
		}
	}

	private async ValueTask ProcessSystem(TradeZeroSocketSystemMessage message, CancellationToken cancellationToken)
	{
		switch (message.Status)
		{
			case TradeZeroSystemStatuses.PENDING_AUTH:
				await Send(new TradeZeroSocketAuthRequest { Key = _apiKey, Secret = _apiSecret.UnSecure() }, cancellationToken);
				break;
			case TradeZeroSystemStatuses.CONNECTED:
				_authorization.TrySetResult();
				foreach (var account in _accounts.ToArray())
					await SendSubscription(account, cancellationToken);
				break;
			case TradeZeroSystemStatuses.FAILED_AUTH:
				var authError = new InvalidOperationException(message.Message.IsEmpty("TradeZero WebSocket authentication failed."));
				_authorization.TrySetException(authError);
				if (Error is { } authErrorHandler)
					await authErrorHandler(authError, cancellationToken);
				break;
			case TradeZeroSystemStatuses.TERMINATED:
			case TradeZeroSystemStatuses.INVALID_DATA:
				if (Error is { } errorHandler)
					await errorHandler(new InvalidOperationException(message.Message.IsEmpty($"TradeZero {_kind} subscription failed.")), cancellationToken);
				break;
		}
	}

	private ValueTask SendSubscription(string accountId, CancellationToken cancellationToken)
		=> _kind == TradeZeroStreamKinds.Portfolio
			? Send(new TradeZeroPortfolioSubscribeRequest
			{
				AccountId = accountId,
				Subscriptions = [TradeZeroPortfolioSubscriptions.Order, TradeZeroPortfolioSubscriptions.Position],
			}, cancellationToken)
			: Send(new TradeZeroPnlSubscribeRequest { Account = accountId }, cancellationToken);

	private ValueTask Send(object payload, CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(payload, _jsonSettings), cancellationToken);

	private string ResolveAccount(string accountId)
	{
		if (!accountId.IsEmpty())
			return accountId;
		var accounts = _accounts.ToArray();
		return accounts.Length == 1 ? accounts[0] : null;
	}
}
