namespace StockSharp.WooX.Native;

sealed class WooXPrivateWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WooXRestClient _restClient;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<WooXWsTopics> _subscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _authentication;
	private long _localId;
	private bool _isAuthenticated;

	public WooXPrivateWsClient(string endpoint, string applicationId,
		WooXRestClient restClient, WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/" +
			Uri.EscapeDataString(applicationId.ThrowIfEmpty(nameof(applicationId)));
		_restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(WooX) + "_PrivateWs";

	public event Func<WooXBalance, long, CancellationToken, ValueTask> BalanceReceived;
	public event Func<WooXWsExecutionReport, long, CancellationToken, ValueTask> ExecutionReceived;
	public event Func<WooXPosition, long, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException("WOO X private WebSocket is already initialized.");
		_authentication = new(TaskCreationOptions.RunContinuationsAsynchronously);
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			await AuthenticateAsync(client, cancellationToken);
			await _authentication.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeBalancesAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(WooXWsTopics.Balance, true, cancellationToken);

	public ValueTask UnsubscribeBalancesAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(WooXWsTopics.Balance, false, cancellationToken);

	public ValueTask SubscribeExecutionsAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(WooXWsTopics.ExecutionReport, true, cancellationToken);

	public ValueTask UnsubscribeExecutionsAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(WooXWsTopics.ExecutionReport, false, cancellationToken);

	public ValueTask SubscribePositionsAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(WooXWsTopics.Position, true, cancellationToken);

	public ValueTask UnsubscribePositionsAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(WooXWsTopics.Position, false, cancellationToken);

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendAsync(client, new WooXWsHeartbeat { Event = "ping" }, cancellationToken)
			: default;

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
			"StockSharp-WooX-Connector/1.0");
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

	private async ValueTask OnStateChangedAsync(WebSocketClient client, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			_isAuthenticated = false;
			_authentication = new(TaskCreationOptions.RunContinuationsAsynchronously);
			await AuthenticateAsync(client, cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private ValueTask AuthenticateAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			.ToString(CultureInfo.InvariantCulture);
		return SendAsync(client, new WooXWsAuth
		{
			Id = "auth-" + Interlocked.Increment(ref _localId)
				.ToString(CultureInfo.InvariantCulture),
			Parameters = new()
			{
				ApiKey = _restClient.ApiKey,
				Signature = _restClient.CreateWebSocketSignature(timestamp),
				Timestamp = timestamp,
			},
		}, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(WooXWsTopics topic, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (isSubscribe ? !_subscriptions.Add(topic) : !_subscriptions.Remove(topic))
				return;
		}
		if (_client?.IsConnected == true && _isAuthenticated)
			await SendSubscriptionAsync(_client, topic, isSubscribe, cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client, WooXWsTopics topic,
		bool isSubscribe, CancellationToken cancellationToken)
		=> SendAsync(client, new WooXWsCommand
		{
			Id = Interlocked.Increment(ref _localId).ToString(CultureInfo.InvariantCulture),
			Topic = GetTopic(topic),
			Event = isSubscribe ? "subscribe" : "unsubscribe",
		}, cancellationToken);

	private static string GetTopic(WooXWsTopics topic)
		=> topic switch
		{
			WooXWsTopics.Balance => "balance",
			WooXWsTopics.ExecutionReport => "executionreport",
			WooXWsTopics.Position => "position",
			_ => throw new ArgumentOutOfRangeException(nameof(topic), topic, null),
		};

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
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<WooXWsHeader>(payload);
			if (header.Event.EqualsIgnoreCase("ping"))
			{
				await SendAsync(client, new WooXWsHeartbeat { Event = "pong" }, cancellationToken);
				return;
			}
			if (header.Event.EqualsIgnoreCase("auth"))
			{
				if (header.IsSuccess != true)
				{
					var error = new InvalidOperationException(
						$"WOO X WebSocket authentication failed: {header.Message}".Trim());
					_authentication?.TrySetException(error);
					throw error;
				}
				_isAuthenticated = true;
				_authentication?.TrySetResult(true);
				WooXWsTopics[] subscriptions;
				using (_sync.EnterScope())
					subscriptions = [.. _subscriptions];
				foreach (var topic in subscriptions)
					await SendSubscriptionAsync(client, topic, true, cancellationToken);
				return;
			}
			if (!header.Event.IsEmpty())
			{
				if (header.IsSuccess == false)
					throw new InvalidOperationException(
						$"WOO X private WebSocket request failed: {header.Message}".Trim());
				return;
			}
			switch (header.Topic?.ToLowerInvariant())
			{
				case "balance":
				{
					var envelope = Deserialize<WooXWsEnvelope<WooXWsBalanceData>>(payload);
					if (BalanceReceived is { } balanceHandler)
						foreach (var balance in envelope.Data?.Balances?.Entries ?? [])
							await balanceHandler(balance, envelope.Timestamp, cancellationToken);
					break;
				}
				case "executionreport":
				{
					var envelope = Deserialize<WooXWsEnvelope<WooXWsExecutionReport>>(payload);
					if (envelope.Data is not null && ExecutionReceived is { } executionHandler)
						await executionHandler(envelope.Data, envelope.Timestamp, cancellationToken);
					break;
				}
				case "position":
				{
					var envelope = Deserialize<WooXWsEnvelope<WooXWsPositionData>>(payload);
					if (PositionReceived is { } positionHandler)
						foreach (var position in envelope.Data?.Positions?.Entries ?? [])
							await positionHandler(position, envelope.Timestamp, cancellationToken);
					break;
				}
				default:
					throw new InvalidDataException(
						$"Unknown WOO X private WebSocket topic '{header.Topic}'.");
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private TData Deserialize<TData>(string payload)
		=> JsonConvert.DeserializeObject<TData>(payload, _jsonSettings)
			?? throw new InvalidDataException("WOO X private WebSocket returned an empty message.");

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
