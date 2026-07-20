namespace StockSharp.QFEX.Native;

readonly record struct QFEXMarketStreamKey(QFEXMarketChannels Channel,
	string Symbol, QFEXCandleIntervals? Interval);

sealed class QFEXMarketDataWebSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<QFEXMarketStreamKey> _subscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public QFEXMarketDataWebSocketClient(string endpoint,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "QFEX_MDS_WS";

	public event Func<QFEXMarketMessageTypes, QFEXMarketDataMessage,
		CancellationToken, ValueTask> DataReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"QFEX market-data WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			await DisconnectAsync(cancellationToken);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
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
			using (_sync.EnterScope())
				_subscriptions.Clear();
		}
	}

	public ValueTask SubscribeAsync(QFEXMarketChannels channel, string symbol,
		QFEXCandleIntervals? interval, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(channel,
			symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant(), interval),
			true, cancellationToken);

	public ValueTask UnsubscribeAsync(QFEXMarketChannels channel, string symbol,
		QFEXCandleIntervals? interval, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(channel,
			symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant(), interval),
			false, cancellationToken);

	private async ValueTask ChangeSubscriptionAsync(QFEXMarketStreamKey key,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		if (key.Channel == QFEXMarketChannels.Candle != key.Interval.HasValue)
			throw new ArgumentException(
				"QFEX candle subscriptions require exactly one interval.");
		bool changed;
		using (_sync.EnterScope())
			changed = isSubscribe
				? _subscriptions.Add(key)
				: _subscriptions.Remove(key);
		if (!changed || _client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAsync(_client, key, isSubscribe,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_subscriptions.Remove(key);
				else
					_subscriptions.Add(key);
			}
			throw;
		}
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(message, token),
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
		client.Init += static socket => socket.Options.SetRequestHeader(
			"User-Agent", "StockSharp-QFEX-Connector/1.0");
		return client;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			QFEXMarketStreamKey[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription, true,
					cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		QFEXMarketStreamKey key, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new QFEXMarketSubscriptionRequest
		{
			Action = isSubscribe
				? QFEXSubscriptionActions.Subscribe
				: QFEXSubscriptionActions.Unsubscribe,
			Channels = [key.Channel],
			Symbols = [key.Symbol],
			Intervals = key.Interval is QFEXCandleIntervals interval
				? [interval]
				: null,
		}, cancellationToken);

	private async ValueTask SendAsync<T>(WebSocketClient client, T message,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(message, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var first = payload.FirstOrDefault(static character =>
				!char.IsWhiteSpace(character));
			var messages = first == '['
				? Deserialize<QFEXMarketDataMessage[]>(payload)
				: [Deserialize<QFEXMarketDataMessage>(payload)];
			foreach (var item in messages.Where(static item => item is not null))
			{
				if (item.Error is not null)
					throw CreateError(item.Error);
				if (item.Type == QFEXMarketMessageTypes.Error)
					throw new InvalidOperationException(
						"QFEX market-data WebSocket returned an error.");
				if (item.Contents is { Length: > 0 })
				{
					foreach (var content in item.Contents.Where(
						static content => content is not null))
						await RaiseDataAsync(item.Type, content,
							cancellationToken);
				}
				else
				{
					await RaiseDataAsync(item.Type, item, cancellationToken);
				}
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask RaiseDataAsync(QFEXMarketMessageTypes type,
		QFEXMarketDataMessage message, CancellationToken cancellationToken)
		=> type is QFEXMarketMessageTypes.Subscribed or
			QFEXMarketMessageTypes.Unsubscribed
			? default
			: DataReceived is { } handler
				? handler(type, message, cancellationToken)
				: default;

	private T Deserialize<T>(string payload)
		where T : class
		=> JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
			throw new InvalidDataException(
				"QFEX market-data WebSocket returned an empty message.");

	private static Exception CreateError(QFEXError error)
		=> new InvalidOperationException(
			$"QFEX WebSocket error {error.ErrorCode}: " +
			(error.Message.IsEmpty() ? "No details." : error.Message));

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
