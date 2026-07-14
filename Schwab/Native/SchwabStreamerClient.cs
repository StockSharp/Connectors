namespace StockSharp.Schwab.Native;

using Ecng.Net;

sealed class SchwabStreamerClient : BaseLogReceiver
{
	private const string _levelOneFields = "0,1,2,3,4,5,8,9,10,11,12,13,15,17,18,19,20,25,32,33,34,35,48,49,50,51";
	private const string _bookFields = "0,1,2,3";

	private readonly WebSocketClient _client;
	private readonly SecureString _accessToken;
	private readonly string _customerId;
	private readonly string _correlId;
	private readonly string _channel;
	private readonly string _functionId;
	private readonly SynchronizedDictionary<string, HashSet<string>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly TaskCompletionSource _loginSource = AsyncHelper.CreateTaskCompletionSource();
	private long _requestId;
	private string _loginRequestId;

	public SchwabStreamerClient(UserPreferences preferences, SecureString accessToken, int reconnectAttempts, WorkingTime workingTime)
	{
		var info = preferences?.StreamerInfo?.FirstOrDefault()
			?? throw new InvalidOperationException("Schwab streamer information is missing.");

		_accessToken = accessToken;
		_customerId = info.CustomerId.ThrowIfEmpty("schwabClientCustomerId");
		_correlId = info.CorrelId.ThrowIfEmpty("schwabClientCorrelId");
		_channel = info.Channel.ThrowIfEmpty("schwabClientChannel");
		_functionId = info.FunctionId.ThrowIfEmpty("schwabClientFunctionId");
		var address = info.SocketUrl.ThrowIfEmpty("streamerSocketUrl");

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
		};
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(Schwab) + "_" + nameof(SchwabStreamerClient);

	public event Func<string, LevelOneContent, CancellationToken, ValueTask> LevelOneReceived;
	public event Func<string, string, BookContent, CancellationToken, ValueTask> BookReceived;
	public event Func<AccountActivityContent, CancellationToken, ValueTask> AccountActivityReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await _client.ConnectAsync(cancellationToken);
		await _loginSource.Task.WaitAsync(cancellationToken);
	}

	public void Disconnect() => _client.Disconnect();

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		_loginRequestId = await SendRequest("ADMIN", "LOGIN", new LoginParameters
		{
			Authorization = _accessToken.UnSecure(),
			Channel = _channel,
			FunctionId = _functionId,
		}, cancellationToken);

		if (!reconnect)
			return;

		foreach (var pair in _subscriptions.ToArray())
			await SendSubscription(pair.Key, "SUBS", pair.Value, cancellationToken);
	}

	private async ValueTask<string> SendRequest(string service, string command, object parameters, CancellationToken cancellationToken)
	{
		var requestId = Interlocked.Increment(ref _requestId).ToString(CultureInfo.InvariantCulture);
		await _client.SendAsync(new StreamerRequestEnvelope
		{
			Requests = [new()
			{
				Service = service,
				RequestId = requestId,
				Command = command,
				CustomerId = _customerId,
				CorrelId = _correlId,
				Parameters = parameters,
			}],
		}, cancellationToken);
		return requestId;
	}

	private ValueTask SendSubscription(string service, string command, IEnumerable<string> symbols, CancellationToken cancellationToken)
	{
		var parameters = new SubscriptionParameters
		{
			Keys = symbols.Join(","),
			Fields = command == "UNSUBS" ? null : service.EndsWith("_BOOK", StringComparison.Ordinal) ? _bookFields : _levelOneFields,
		};
		return new(SendRequest(service, command, parameters, cancellationToken).AsTask());
	}

	public async ValueTask Subscribe(string service, string symbol, CancellationToken cancellationToken)
	{
		var symbols = _subscriptions.SafeAdd(service, _ => new(StringComparer.OrdinalIgnoreCase));
		var command = symbols.Count == 0 ? "SUBS" : "ADD";
		if (symbols.Add(symbol))
			await SendSubscription(service, command, [symbol], cancellationToken);
	}

	public async ValueTask Unsubscribe(string service, string symbol, CancellationToken cancellationToken)
	{
		if (_subscriptions.TryGetValue(service, out var symbols) && symbols.Remove(symbol))
			await SendSubscription(service, "UNSUBS", [symbol], cancellationToken);
	}

	public ValueTask SubscribeAccountActivity(CancellationToken cancellationToken)
		=> Subscribe("ACCT_ACTIVITY", _correlId, cancellationToken);

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var raw = message.AsString();
		if (raw.IsEmpty())
			return;

		var responseEnvelope = JsonConvert.DeserializeObject<StreamerResponseEnvelope>(raw);
		foreach (var response in responseEnvelope?.Responses ?? [])
		{
			var code = response.Content?.Code ?? -1;
			if (code != 0)
			{
				var error = new InvalidOperationException(response.Content?.Message ?? raw);
				if (response.RequestId == _loginRequestId)
					_loginSource.TrySetException(error);
				if (Error is { } errorHandler)
					await errorHandler(error, cancellationToken);
			}
			else if (response.RequestId == _loginRequestId)
			{
				_loginSource.TrySetResult();
			}
		}

		if (raw.Contains("LEVELONE_EQUITIES", StringComparison.Ordinal))
		{
			foreach (var data in JsonConvert.DeserializeObject<LevelOneEnvelope>(raw)?.Data?.Where(d => d.Service == "LEVELONE_EQUITIES") ?? [])
			{
				foreach (var item in data.Content ?? [])
					if (LevelOneReceived is { } handler)
						await handler(item.Key ?? item.Symbol, item, cancellationToken);
			}
		}

		if (raw.Contains("_BOOK", StringComparison.Ordinal))
		{
			foreach (var data in JsonConvert.DeserializeObject<BookEnvelope>(raw)?.Data?.Where(d => d.Service.EndsWith("_BOOK", StringComparison.Ordinal)) ?? [])
			{
				foreach (var item in data.Content ?? [])
					if (BookReceived is { } handler)
						await handler(data.Service, item.Key ?? item.Symbol, item, cancellationToken);
			}
		}

		if (raw.Contains("ACCT_ACTIVITY", StringComparison.Ordinal))
		{
			foreach (var data in JsonConvert.DeserializeObject<AccountActivityEnvelope>(raw)?.Data?.Where(d => d.Service == "ACCT_ACTIVITY") ?? [])
			{
				foreach (var item in data.Content ?? [])
					if (AccountActivityReceived is { } handler)
						await handler(item, cancellationToken);
			}
		}
	}
}
