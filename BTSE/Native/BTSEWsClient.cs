namespace StockSharp.BTSE.Native;

sealed class BTSEWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly BTSESections _section;
	private readonly bool _isOrderBook;
	private readonly BTSERestClient _restClient;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _publicSubscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _privateSubscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly SemaphoreSlim _authSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private bool _isAuthenticated;

	public BTSEWsClient(string endpoint, BTSESections section, bool isOrderBook,
		BTSERestClient restClient, WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/');
		_section = section;
		_isOrderBook = isOrderBook;
		_restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(BTSE) + "_" + _section +
		(_isOrderBook ? "_OrderBookWs" : "_Ws");

	public BTSESections Section => _section;

	public bool IsOrderBook => _isOrderBook;

	public event Func<BTSESections, BTSEPublicTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<BTSESections, BTSEWsBook, CancellationToken, ValueTask> BookReceived;
	public event Func<BTSESections, BTSEWsOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<BTSESections, BTSEWsFill, CancellationToken, ValueTask> FillReceived;
	public event Func<BTSESections, BTSEWsPosition, CancellationToken, ValueTask> PositionReceived;
	public event Func<BTSEWsClient, Exception, CancellationToken, ValueTask> Error;
	public event Func<BTSEWsClient, ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		_authSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException("BTSE WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(GetTradesTopic(symbol), true, false, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(GetTradesTopic(symbol), false, false, cancellationToken);

	public ValueTask SubscribeLevel1Async(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("snapshotL1:" + symbol, true, false, cancellationToken);

	public ValueTask UnsubscribeLevel1Async(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("snapshotL1:" + symbol, false, false, cancellationToken);

	public ValueTask SubscribeDepthAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("update:" + symbol + "_0", true, false,
			cancellationToken);

	public ValueTask UnsubscribeDepthAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("update:" + symbol + "_0", false, false,
			cancellationToken);

	public async ValueTask ResubscribeDepthAsync(string symbol,
		CancellationToken cancellationToken)
	{
		if (_client?.IsConnected != true)
			return;
		var topic = "update:" + symbol + "_0";
		await SendSubscriptionAsync(_client, [topic], false, cancellationToken);
		await SendSubscriptionAsync(_client, [topic], true, cancellationToken);
	}

	public ValueTask SubscribeOrdersAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(_section == BTSESections.Spot
			? "notificationApiV3"
			: "notificationApiV4", true, true, cancellationToken);

	public ValueTask UnsubscribeOrdersAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(_section == BTSESections.Spot
			? "notificationApiV3"
			: "notificationApiV4", false, true, cancellationToken);

	public ValueTask SubscribeFillsAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(_section == BTSESections.Spot ? "fills" : "fillsV2",
			true, true, cancellationToken);

	public ValueTask UnsubscribeFillsAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(_section == BTSESections.Spot ? "fills" : "fillsV2",
			false, true, cancellationToken);

	public ValueTask SubscribePositionsAsync(CancellationToken cancellationToken)
	{
		if (_section != BTSESections.Futures)
			throw new InvalidOperationException("BTSE position stream is available for futures only.");
		return ChangeSubscriptionAsync("allPositionV4", true, true, cancellationToken);
	}

	public ValueTask UnsubscribePositionsAsync(CancellationToken cancellationToken)
		=> _section == BTSESections.Futures
			? ChangeSubscriptionAsync("allPositionV4", false, true, cancellationToken)
			: default;

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendAsync(client, "ping", cancellationToken)
			: default;

	private string GetTradesTopic(string symbol)
		=> (_section == BTSESections.Spot
			? "tradeHistoryApi:"
			: "tradeHistoryApiV3:") + symbol;

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-BTSE-Connector/1.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		_isAuthenticated = false;
		if (client is null)
			return;
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
		}
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			_isAuthenticated = false;
			string[] publicTopics;
			string[] privateTopics;
			using (_sync.EnterScope())
			{
				publicTopics = [.. _publicSubscriptions];
				privateTopics = [.. _privateSubscriptions];
			}
			if (publicTopics.Length > 0)
				await SendSubscriptionAsync(client, publicTopics, true, cancellationToken);
			if (privateTopics.Length > 0)
			{
				await EnsureAuthenticatedAsync(client, cancellationToken);
				await SendSubscriptionAsync(client, privateTopics, true, cancellationToken);
			}
		}

		if (StateChanged is { } handler)
			await handler(this, state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(string topic, bool isSubscribe,
		bool isPrivate, CancellationToken cancellationToken)
	{
		if (_isOrderBook && isPrivate)
			throw new InvalidOperationException("BTSE private topics require the general WebSocket.");

		using (_sync.EnterScope())
		{
			var subscriptions = isPrivate ? _privateSubscriptions : _publicSubscriptions;
			if (isSubscribe ? !subscriptions.Add(topic) : !subscriptions.Remove(topic))
				return;
		}

		if (_client?.IsConnected != true)
			return;
		if (isPrivate && isSubscribe)
			await EnsureAuthenticatedAsync(_client, cancellationToken);
		await SendSubscriptionAsync(_client, [topic], isSubscribe, cancellationToken);
	}

	private async ValueTask EnsureAuthenticatedAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		if (_isAuthenticated)
			return;
		if (!_restClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"BTSE API key and secret are required for private WebSocket topics.");

		await _authSync.WaitAsync(cancellationToken);
		try
		{
			if (_isAuthenticated)
				return;
			var nonce = _restClient.CreateWebSocketNonce();
			await SendAsync(client, new BTSEWsCommand
			{
				Operation = BTSEWsOperations.Authenticate,
				Arguments =
				[
					_restClient.ApiKey,
					nonce.ToString(CultureInfo.InvariantCulture),
					_restClient.CreateWebSocketSignature(nonce),
				],
			}, cancellationToken);
			_isAuthenticated = true;
		}
		finally
		{
			_authSync.Release();
		}
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client, string[] topics,
		bool isSubscribe, CancellationToken cancellationToken)
		=> SendAsync(client, new BTSEWsCommand
		{
			Operation = isSubscribe
				? BTSEWsOperations.Subscribe
				: BTSEWsOperations.Unsubscribe,
			Arguments = topics,
		}, cancellationToken);

	private async ValueTask SendAsync(WebSocketClient client, string payload,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(payload, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask SendAsync<TPayload>(WebSocketClient client, TPayload payload,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(payload, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong"))
			return;

		try
		{
			var header = Deserialize<BTSEWsHeader>(payload);
			if (header.Code is not null and not 0 || header.IsSuccess == false)
				throw new InvalidOperationException(
					$"BTSE WebSocket error {header.Code}: {header.Message ?? header.ShortMessage}".Trim());
			if (header.Topic.IsEmpty())
				return;

			if (header.Topic.StartsWith("tradeHistoryApi", StringComparison.OrdinalIgnoreCase))
				await ProcessTradesAsync(payload, cancellationToken);
			else if (header.Topic.StartsWith("snapshotL1:", StringComparison.OrdinalIgnoreCase) ||
				header.Topic.StartsWith("update:", StringComparison.OrdinalIgnoreCase))
				await ProcessBookAsync(payload, cancellationToken);
			else if (header.Topic.EqualsIgnoreCase("notificationApiV3"))
				await ProcessSpotOrderAsync(payload, cancellationToken);
			else if (header.Topic.EqualsIgnoreCase("notificationApiV4"))
				await ProcessFuturesOrdersAsync(payload, cancellationToken);
			else if (header.Topic.EqualsIgnoreCase("fills") ||
				header.Topic.EqualsIgnoreCase("fillsV2"))
				await ProcessFillsAsync(payload, cancellationToken);
			else if (header.Topic.EqualsIgnoreCase("allPositionV4"))
				await ProcessPositionsAsync(payload, cancellationToken);
			else
				throw new InvalidDataException($"Unknown BTSE WebSocket topic '{header.Topic}'.");
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessTradesAsync(string payload,
		CancellationToken cancellationToken)
	{
		if (TradeReceived is not { } handler)
			return;
		var envelope = Deserialize<BTSEWsEnvelope<BTSEPublicTrade[]>>(payload);
		foreach (var trade in envelope.Data ?? [])
			if (trade is not null)
				await handler(_section, trade, cancellationToken);
	}

	private async ValueTask ProcessBookAsync(string payload,
		CancellationToken cancellationToken)
	{
		var envelope = Deserialize<BTSEWsEnvelope<BTSEWsBook>>(payload);
		if (envelope.Data is not null && BookReceived is { } handler)
			await handler(_section, envelope.Data, cancellationToken);
	}

	private async ValueTask ProcessSpotOrderAsync(string payload,
		CancellationToken cancellationToken)
	{
		var envelope = Deserialize<BTSEWsEnvelope<BTSEWsOrder>>(payload);
		if (envelope.Data is not null && OrderReceived is { } handler)
			await handler(_section, envelope.Data, cancellationToken);
	}

	private async ValueTask ProcessFuturesOrdersAsync(string payload,
		CancellationToken cancellationToken)
	{
		if (OrderReceived is not { } handler)
			return;
		var envelope = Deserialize<BTSEWsEnvelope<BTSEWsOrder[]>>(payload);
		foreach (var order in envelope.Data ?? [])
			if (order is not null)
				await handler(_section, order, cancellationToken);
	}

	private async ValueTask ProcessFillsAsync(string payload,
		CancellationToken cancellationToken)
	{
		if (FillReceived is not { } handler)
			return;
		var envelope = Deserialize<BTSEWsEnvelope<BTSEWsFill[]>>(payload);
		foreach (var fill in envelope.Data ?? [])
			if (fill is not null)
				await handler(_section, fill, cancellationToken);
	}

	private async ValueTask ProcessPositionsAsync(string payload,
		CancellationToken cancellationToken)
	{
		if (PositionReceived is not { } handler)
			return;
		var envelope = Deserialize<BTSEWsEnvelope<BTSEWsPosition[]>>(payload);
		foreach (var position in envelope.Data ?? [])
			if (position is not null)
				await handler(_section, position, cancellationToken);
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings)
				?? throw new InvalidDataException("BTSE WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("BTSE WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(this, error, cancellationToken) : default;
}
