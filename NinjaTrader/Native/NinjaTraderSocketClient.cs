namespace StockSharp.NinjaTrader.Native;

sealed class NinjaTraderSocketClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly SecureString _accessToken;
	private readonly SynchronizedDictionary<string, (string endpoint, object payload)> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, TaskCompletionSource<NinjaTraderSocketEnvelope>> _responses = [];
	private readonly TaskCompletionSource _authorization = AsyncHelper.CreateTaskCompletionSource();
	private long _requestId;
	private long _authorizationRequestId;

	public NinjaTraderSocketClient(string url, SecureString accessToken, int reconnectAttempts, WorkingTime workingTime)
	{
		_accessToken = accessToken.ThrowIfEmpty(nameof(accessToken));
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
			DisableAutoResend = true,
		};
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(NinjaTrader) + "_" + nameof(NinjaTraderSocketClient);

	public event Func<NinjaTraderQuote, CancellationToken, ValueTask> QuoteReceived;
	public event Func<NinjaTraderDom, CancellationToken, ValueTask> DomReceived;
	public event Func<NinjaTraderChart, CancellationToken, ValueTask> ChartReceived;
	public event Func<string, NinjaTraderEntity, CancellationToken, ValueTask> EntityReceived;
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
		await _authorization.Task.WaitAsync(cancellationToken);
	}

	public void Disconnect() => _client.Disconnect();

	public ValueTask SendHeartbeat(CancellationToken cancellationToken)
		=> _client.SendAsync("[]", cancellationToken);

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		_authorizationRequestId = Interlocked.Increment(ref _requestId);
		await _client.SendAsync($"authorize\n{_authorizationRequestId}\n\n{_accessToken.UnSecure()}", cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var raw = message.AsString();
		if (raw.IsEmpty() || raw == "o")
			return;

		if (raw == "h")
		{
			await _client.SendAsync("[]", cancellationToken);
			return;
		}

		if (raw[0] == 'c')
		{
			if (Error is { } closeHandler)
				await closeHandler(new InvalidOperationException($"NinjaTrader closed the WebSocket connection: {raw}"), cancellationToken);
			return;
		}

		if (raw[0] != 'a')
			return;

		foreach (var envelope in JsonConvert.DeserializeObject<NinjaTraderSocketEnvelope[]>(raw[1..]) ?? [])
		{
			if (envelope.RequestId is long requestId)
			{
				if (requestId == _authorizationRequestId)
				{
					if (envelope.Status == 200)
					{
						_authorization.TrySetResult();
						foreach (var subscription in _subscriptions.ToArray())
							await Send(subscription.Value.endpoint, subscription.Value.payload, cancellationToken);
					}
					else
					{
						_authorization.TrySetException(CreateError(envelope));
					}
				}

				if (_responses.Remove(requestId, out var response))
				{
					if (envelope.Status == 200)
						response.TrySetResult(envelope);
					else
						response.TrySetException(CreateError(envelope));
				}
			}

			switch (envelope.Event)
			{
				case "md":
					foreach (var quote in envelope.Data?.Quotes ?? [])
						if (QuoteReceived is { } quoteHandler)
							await quoteHandler(quote, cancellationToken);
					foreach (var dom in envelope.Data?.Doms ?? [])
						if (DomReceived is { } domHandler)
							await domHandler(dom, cancellationToken);
					break;
				case "chart":
					foreach (var chart in envelope.Data?.Charts ?? [])
						if (ChartReceived is { } chartHandler)
							await chartHandler(chart, cancellationToken);
					break;
				case "props":
					if (envelope.Data?.Entity is { } entity && EntityReceived is { } entityHandler)
						await entityHandler(envelope.Data.EntityType, entity, cancellationToken);
					break;
			}
		}
	}

	private static Exception CreateError(NinjaTraderSocketEnvelope envelope)
		=> new InvalidOperationException(envelope.Data?.ErrorText ?? $"NinjaTrader WebSocket request failed with status {envelope.Status}.");

	private async Task<NinjaTraderSocketEnvelope> Request(string endpoint, object payload, CancellationToken cancellationToken)
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var response = AsyncHelper.CreateTaskCompletionSource<NinjaTraderSocketEnvelope>();
		_responses.Add(requestId, response);
		try
		{
			await Send(endpoint, payload, requestId, cancellationToken);
			return await response.Task.WaitAsync(cancellationToken);
		}
		finally
		{
			_responses.Remove(requestId);
		}
	}

	private ValueTask Send(string endpoint, object payload, CancellationToken cancellationToken)
		=> Send(endpoint, payload, Interlocked.Increment(ref _requestId), cancellationToken);

	private ValueTask Send(string endpoint, object payload, long requestId, CancellationToken cancellationToken)
		=> _client.SendAsync($"{endpoint}\n{requestId}\n\n{JsonConvert.SerializeObject(payload, _jsonSettings)}", cancellationToken);

	private async ValueTask Subscribe(string key, string endpoint, object payload, CancellationToken cancellationToken)
	{
		if (_subscriptions.ContainsKey(key))
			return;
		_subscriptions.Add(key, (endpoint, payload));
		await Send(endpoint, payload, cancellationToken);
	}

	private async ValueTask Unsubscribe(string key, string endpoint, object payload, CancellationToken cancellationToken)
	{
		_subscriptions.Remove(key);
		await Send(endpoint, payload, cancellationToken);
	}

	public ValueTask SubscribeQuote(string symbol, CancellationToken cancellationToken)
		=> Subscribe($"quote:{symbol}", "md/subscribeQuote", new NinjaTraderSymbolRequest { Symbol = symbol }, cancellationToken);

	public ValueTask UnsubscribeQuote(string symbol, CancellationToken cancellationToken)
		=> Unsubscribe($"quote:{symbol}", "md/unsubscribeQuote", new NinjaTraderSymbolRequest { Symbol = symbol }, cancellationToken);

	public ValueTask SubscribeDom(string symbol, CancellationToken cancellationToken)
		=> Subscribe($"dom:{symbol}", "md/subscribeDOM", new NinjaTraderSymbolRequest { Symbol = symbol }, cancellationToken);

	public ValueTask UnsubscribeDom(string symbol, CancellationToken cancellationToken)
		=> Unsubscribe($"dom:{symbol}", "md/unsubscribeDOM", new NinjaTraderSymbolRequest { Symbol = symbol }, cancellationToken);

	public async Task<(long? historicalId, long? realtimeId)> SubscribeChart(long transactionId, NinjaTraderChartRequest request, CancellationToken cancellationToken)
	{
		var key = $"chart:{transactionId}";
		_subscriptions[key] = ("md/getChart", request);
		var response = await Request("md/getChart", request, cancellationToken);
		return (response.Data?.HistoricalId, response.Data?.RealtimeId);
	}

	public async ValueTask CancelChart(long transactionId, IEnumerable<long> subscriptionIds, CancellationToken cancellationToken)
	{
		_subscriptions.Remove($"chart:{transactionId}");
		foreach (var id in subscriptionIds)
			await Send("md/cancelChart", new NinjaTraderCancelChartRequest { SubscriptionId = id }, cancellationToken);
	}

	public ValueTask Synchronize(long userId, IEnumerable<long> accountIds, CancellationToken cancellationToken)
		=> Subscribe("user-sync", "user/syncrequest", new NinjaTraderSyncRequest
		{
			Users = [userId],
			Accounts = [.. accountIds],
			IsSplitResponses = true,
		}, cancellationToken);
}
