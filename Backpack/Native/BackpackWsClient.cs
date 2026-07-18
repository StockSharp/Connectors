namespace StockSharp.Backpack.Native;

sealed class BackpackWsClient : BaseLogReceiver
{
	private const long _receiveWindow = 5000;
	private readonly string _endpoint;
	private readonly BackpackRestClient _restClient;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _publicSubscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _privateSubscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public BackpackWsClient(string endpoint, BackpackRestClient restClient,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/');
		_restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Backpack) + "_Ws";

	public event Func<BackpackWsBookTicker, CancellationToken, ValueTask> BookTickerReceived;
	public event Func<BackpackWsDepth, CancellationToken, ValueTask> DepthReceived;
	public event Func<BackpackWsTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<BackpackWsTicker, CancellationToken, ValueTask> TickerReceived;
	public event Func<string, BackpackWsKline, CancellationToken, ValueTask> KlineReceived;
	public event Func<BackpackWsMarkPrice, CancellationToken, ValueTask> MarkPriceReceived;
	public event Func<BackpackWsOrderUpdate, CancellationToken, ValueTask> OrderReceived;
	public event Func<BackpackWsPositionUpdate, CancellationToken, ValueTask> PositionReceived;
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
			throw new InvalidOperationException(
				"Backpack Exchange WebSocket is already initialized.");
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

	public ValueTask SubscribeBookTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("bookTicker." + symbol, true, false, cancellationToken);

	public ValueTask UnsubscribeBookTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("bookTicker." + symbol, false, false, cancellationToken);

	public ValueTask SubscribeDepthAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("depth.200ms." + symbol, true, false, cancellationToken);

	public ValueTask UnsubscribeDepthAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("depth.200ms." + symbol, false, false, cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("trade." + symbol, true, false, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("trade." + symbol, false, false, cancellationToken);

	public ValueTask SubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("ticker." + symbol, true, false, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("ticker." + symbol, false, false, cancellationToken);

	public ValueTask SubscribeKlinesAsync(string symbol, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"kline.{timeFrame.ToBackpackInterval()}.{symbol}", true, false,
			cancellationToken);

	public ValueTask UnsubscribeKlinesAsync(string symbol, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"kline.{timeFrame.ToBackpackInterval()}.{symbol}", false, false,
			cancellationToken);

	public ValueTask SubscribeMarkPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("markPrice." + symbol, true, false, cancellationToken);

	public ValueTask UnsubscribeMarkPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("markPrice." + symbol, false, false, cancellationToken);

	public ValueTask SubscribeOrdersAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("account.orderUpdate", true, true, cancellationToken);

	public ValueTask UnsubscribeOrdersAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("account.orderUpdate", false, true, cancellationToken);

	public ValueTask SubscribePositionsAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("account.positionUpdate", true, true, cancellationToken);

	public ValueTask UnsubscribePositionsAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("account.positionUpdate", false, true, cancellationToken);

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
			"StockSharp-Backpack-Connector/1.0");
		return client;
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

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			string[] publicStreams;
			string[] privateStreams;
			using (_sync.EnterScope())
			{
				publicStreams = [.. _publicSubscriptions];
				privateStreams = [.. _privateSubscriptions];
			}
			if (publicStreams.Length > 0)
				await SendSubscriptionAsync(client, publicStreams, true, false,
					cancellationToken);
			if (privateStreams.Length > 0)
				await SendSubscriptionAsync(client, privateStreams, true, true,
					cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(string stream, bool isSubscribe,
		bool isPrivate, CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			var subscriptions = isPrivate ? _privateSubscriptions : _publicSubscriptions;
			if (isSubscribe ? !subscriptions.Add(stream) : !subscriptions.Remove(stream))
				return;
		}
		if (_client?.IsConnected == true)
			await SendSubscriptionAsync(_client, [stream], isSubscribe, isPrivate,
				cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client, string[] streams,
		bool isSubscribe, bool isPrivate, CancellationToken cancellationToken)
	{
		string[] signature = null;
		if (isPrivate)
		{
			if (!_restClient.IsCredentialsAvailable)
				throw new InvalidOperationException(
					"Backpack Exchange API keys are required for private streams.");
			var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			signature =
			[
				_restClient.ApiKey,
				_restClient.CreateWebSocketSignature(timestamp, _receiveWindow),
				timestamp.ToString(CultureInfo.InvariantCulture),
				_receiveWindow.ToString(CultureInfo.InvariantCulture),
			];
		}
		return SendAsync(client, new BackpackWsCommand
		{
			Method = isSubscribe
				? BackpackWsMethods.Subscribe
				: BackpackWsMethods.Unsubscribe,
			Parameters = streams,
			Signature = signature,
		}, cancellationToken);
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
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<BackpackWsEnvelopeHeader>(payload);
			if (header.Error is { } wsError)
				throw new InvalidOperationException(
					$"Backpack Exchange WebSocket error {wsError.Code}: " +
					$"{wsError.Message ?? wsError.ShortMessage}".Trim());
			if (header.Stream.IsEmpty())
				return;
			if (header.Stream.StartsWith("bookTicker.", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<BackpackWsEnvelope<BackpackWsBookTicker>>(payload);
				if (envelope.Data is not null && BookTickerReceived is { } handler)
					await handler(envelope.Data, cancellationToken);
			}
			else if (header.Stream.StartsWith("depth.", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<BackpackWsEnvelope<BackpackWsDepth>>(payload);
				if (envelope.Data is not null && DepthReceived is { } handler)
					await handler(envelope.Data, cancellationToken);
			}
			else if (header.Stream.StartsWith("trade.", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<BackpackWsEnvelope<BackpackWsTrade>>(payload);
				if (envelope.Data is not null && TradeReceived is { } handler)
					await handler(envelope.Data, cancellationToken);
			}
			else if (header.Stream.StartsWith("ticker.", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<BackpackWsEnvelope<BackpackWsTicker>>(payload);
				if (envelope.Data is not null && TickerReceived is { } handler)
					await handler(envelope.Data, cancellationToken);
			}
			else if (header.Stream.StartsWith("kline.", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<BackpackWsEnvelope<BackpackWsKline>>(payload);
				if (envelope.Data is not null && KlineReceived is { } handler)
					await handler(header.Stream, envelope.Data, cancellationToken);
			}
			else if (header.Stream.StartsWith("markPrice.", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<BackpackWsEnvelope<BackpackWsMarkPrice>>(payload);
				if (envelope.Data is not null && MarkPriceReceived is { } handler)
					await handler(envelope.Data, cancellationToken);
			}
			else if (header.Stream.StartsWith("account.orderUpdate",
				StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<BackpackWsEnvelope<BackpackWsOrderUpdate>>(payload);
				if (envelope.Data is not null && OrderReceived is { } handler)
					await handler(envelope.Data, cancellationToken);
			}
			else if (header.Stream.StartsWith("account.positionUpdate",
				StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<BackpackWsEnvelope<BackpackWsPositionUpdate>>(payload);
				if (envelope.Data is not null && PositionReceived is { } handler)
					await handler(envelope.Data, cancellationToken);
			}
			else
				throw new InvalidDataException(
					$"Unknown Backpack Exchange WebSocket stream '{header.Stream}'.");
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private TData Deserialize<TData>(string payload)
		=> JsonConvert.DeserializeObject<TData>(payload, _jsonSettings)
			?? throw new InvalidDataException(
				"Backpack Exchange WebSocket returned an empty message.");

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
