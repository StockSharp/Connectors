namespace StockSharp.WooX.Native;

using Nito.AsyncEx;

sealed class WooXPrivateWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WooXRestClient _restClient;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<WooXWsTopics> _subscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly AsyncLock _connectionSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private long _localId;
	private long _refreshAt;
	private bool _isRunning;

	public WooXPrivateWsClient(string endpoint,
		WooXRestClient restClient, WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint));
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
		using var connection = await _connectionSync.LockAsync(cancellationToken);
		if (_isRunning)
			throw new InvalidOperationException("WOO X private WebSocket is already initialized.");
		await OpenClientAsync(cancellationToken);
		_isRunning = true;
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		using var connection = await _connectionSync.LockAsync(cancellationToken);
		_isRunning = false;
		await DisposeClientAsync(cancellationToken);
	}

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

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		using var connection = await _connectionSync.LockAsync(cancellationToken);
		if (!_isRunning)
			return;
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		if (now >= _refreshAt)
		{
			await OpenClientAsync(cancellationToken);
			await OnStateChangedAsync(_client, ConnectionStates.Restored, cancellationToken);
		}
		if (_client is { IsConnected: true } client)
			await SendAsync(client, new WooXWsHeartbeat { Command = "PING", Timestamp = now }, cancellationToken);
	}

	private async ValueTask OpenClientAsync(CancellationToken cancellationToken)
	{
		var listenKey = (await _restClient.GetListenKeyAsync(cancellationToken)).Data;
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		if (listenKey is null || listenKey.Key.IsEmpty() || listenKey.ExpiresAt <= now)
			throw new InvalidDataException("WOO X did not return a valid WebSocket listen key.");

		// Generating a listen key invalidates the previous one.
		await DisposeClientAsync(cancellationToken);
		var client = _client = CreateClient(listenKey.Key);
		try
		{
			await client.ConnectAsync(cancellationToken);
			_refreshAt = Math.Max(now + 10_000, listenKey.ExpiresAt - 60_000);
		}
		catch (Exception error)
		{
			await DisposeClientAsync(cancellationToken);
			var safeError = SanitizeError(error, listenKey.Key);
			if (safeError != error)
				throw safeError;
			throw;
		}
	}

	private WebSocketClient CreateClient(string listenKey)
	{
		var address = new UriBuilder(_endpoint);
		var parameter = "key=" + Uri.EscapeDataString(listenKey);
		address.Query = address.Query.IsEmpty() ? parameter : address.Query[1..] + "&" + parameter;
		WebSocketClient client = null;
		client = new WebSocketClient(
			address.Uri.AbsoluteUri,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(SanitizeError(error, listenKey), token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			// WebSocketClient logs the full URI, which contains the listen key.
			static (_, _) => { },
			static (_, _) => { },
			static (_, _) => { })
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-WooX-Connector/1.0");
			return default;
		};
		return client;
	}

	private static Exception SanitizeError(Exception error, string listenKey)
	{
		var details = error.ToString();
		return details.Contains(listenKey, StringComparison.Ordinal) || details.Contains(Uri.EscapeDataString(listenKey), StringComparison.OrdinalIgnoreCase)
			? new InvalidOperationException("WOO X private WebSocket connection failed.")
			: error;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
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
		if (client != _client)
			return;
		if (state == ConnectionStates.Restored)
		{
			WooXWsTopics[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var topic in subscriptions)
				await SendSubscriptionAsync(client, topic, true, cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler.InvokeAsync(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(WooXWsTopics topic, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (isSubscribe ? !_subscriptions.Add(topic) : !_subscriptions.Remove(topic))
				return;
		}
		if (_client is { IsConnected: true } client)
			await SendSubscriptionAsync(client, topic, isSubscribe, cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client, WooXWsTopics topic,
		bool isSubscribe, CancellationToken cancellationToken)
		=> SendAsync(client, new WooXWsCommand
		{
			Id = Interlocked.Increment(ref _localId).ToString(CultureInfo.InvariantCulture),
			Parameters = [GetTopic(topic)],
			Command = isSubscribe ? "SUBSCRIBE" : "UN_SUBSCRIBE",
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
			if (header.Command.EqualsIgnoreCase("PING"))
			{
				await SendAsync(client, new WooXWsHeartbeat { Command = "PONG", Timestamp = header.Timestamp }, cancellationToken);
				return;
			}
			if (!header.Command.IsEmpty())
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
						foreach (var balance in envelope.Data?.Balances ?? [])
							await balanceHandler.InvokeAsync(new WooXBalance
							{
								Token = balance.Token,
								Holding = balance.Holding,
								Frozen = balance.Frozen,
								PnL24Hours = balance.PnL24Hours,
								Timestamp = balance.Timestamp,
							}, envelope.Timestamp, cancellationToken);
					break;
				}
				case "executionreport":
				{
					var envelope = Deserialize<WooXWsEnvelope<WooXWsExecutionReport>>(payload);
					if (envelope.Data is not null && ExecutionReceived is { } executionHandler)
						await executionHandler.InvokeAsync(envelope.Data, envelope.Timestamp, cancellationToken);
					break;
				}
				case "position":
				{
					var envelope = Deserialize<WooXWsEnvelope<WooXWsPositionData>>(payload);
					if (PositionReceived is { } positionHandler)
						foreach (var position in envelope.Data?.Positions ?? [])
							await positionHandler.InvokeAsync(new WooXPosition
							{
								Symbol = position.Symbol,
								Holding = position.Holding,
								AverageOpenPrice = position.AverageOpenPrice,
								PnL24Hours = position.PnL24Hours,
								MarkPrice = position.MarkPrice,
								Leverage = position.Leverage,
								MarginMode = position.MarginMode,
								PositionSide = position.PositionSide,
								Timestamp = position.Timestamp / 1000m,
							}, envelope.Timestamp, cancellationToken);
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
		=> Error is { } handler ? handler.InvokeAsync(error, cancellationToken) : default;
}
