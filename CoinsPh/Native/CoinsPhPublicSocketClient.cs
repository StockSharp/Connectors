namespace StockSharp.CoinsPh.Native;

sealed class CoinsPhPublicSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _desiredStreams =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _serverStreams =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};
	private WebSocketClient _client;
	private CancellationTokenSource _pingCancellation;
	private Task _pingTask;
	private long _sequence;

	public CoinsPhPublicSocketClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = CreateEndpoint(endpoint);
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "CoinsPh_PublicSocket";

	public event Func<CoinsPhPublicSocketMessage, CancellationToken, ValueTask>
		TradeReceived;
	public event Func<CoinsPhPublicSocketMessage, CancellationToken, ValueTask>
		TickerReceived;
	public event Func<CoinsPhPublicSocketMessage, CancellationToken, ValueTask>
		DepthReceived;
	public event Func<CoinsPhPublicSocketMessage, CancellationToken, ValueTask>
		KlineReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_pingCancellation?.Cancel();
		_pingCancellation?.Dispose();
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Coins.ph public WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			_pingCancellation = new();
			_pingTask = RunPingAsync(_pingCancellation.Token);
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
		=> ChangeSubscriptionAsync(
			$"{symbol.ThrowIfEmpty(nameof(symbol)).ToWireSymbol()}@trade", true,
			cancellationToken);

	public ValueTask ReleaseTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{symbol.ThrowIfEmpty(nameof(symbol)).ToWireSymbol()}@trade", false,
			cancellationToken);

	public ValueTask SubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{symbol.ThrowIfEmpty(nameof(symbol)).ToWireSymbol()}@ticker", true,
			cancellationToken);

	public ValueTask ReleaseTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{symbol.ThrowIfEmpty(nameof(symbol)).ToWireSymbol()}@ticker", false,
			cancellationToken);

	public ValueTask SubscribeDepthAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{symbol.ThrowIfEmpty(nameof(symbol)).ToWireSymbol()}@depth200@100ms",
			true, cancellationToken);

	public ValueTask ReleaseDepthAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{symbol.ThrowIfEmpty(nameof(symbol)).ToWireSymbol()}@depth200@100ms",
			false, cancellationToken);

	public ValueTask SubscribeKlinesAsync(string symbol, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{symbol.ThrowIfEmpty(nameof(symbol)).ToWireSymbol()}@kline_" +
				interval.ThrowIfEmpty(nameof(interval)), true, cancellationToken);

	public ValueTask ReleaseKlinesAsync(string symbol, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{symbol.ThrowIfEmpty(nameof(symbol)).ToWireSymbol()}@kline_" +
				interval.ThrowIfEmpty(nameof(interval)), false, cancellationToken);

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
			"StockSharp-CoinsPh-Connector/1.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		var cancellation = _pingCancellation;
		_pingCancellation = null;
		if (cancellation is not null)
		{
			cancellation.Cancel();
			try
			{
				if (_pingTask is not null)
					await _pingTask;
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				cancellation.Dispose();
				_pingTask = null;
			}
		}

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
				_serverStreams.Clear();
		}
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			string[] streams;
			using (_sync.EnterScope())
			{
				_serverStreams.Clear();
				streams = [.. _desiredStreams];
				_serverStreams.UnionWith(streams);
			}
			if (streams.Length > 0)
				await SendCommandAsync(client, CoinsPhSocketCommands.Subscribe,
					streams, cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(string stream,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		var send = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				if (!_desiredStreams.Add(stream))
					return;
				send = _client?.IsConnected == true &&
					_serverStreams.Add(stream);
			}
			else
			{
				if (!_desiredStreams.Remove(stream))
					return;
				send = _client?.IsConnected == true &&
					_serverStreams.Remove(stream);
			}
		}
		if (!send)
			return;
		try
		{
			await SendCommandAsync(_client, isSubscribe
				? CoinsPhSocketCommands.Subscribe
				: CoinsPhSocketCommands.Unsubscribe, [stream], cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_serverStreams.Remove(stream);
				else
					_serverStreams.Add(stream);
			}
			throw;
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var value = Deserialize<CoinsPhPublicSocketMessage>(payload);
			if (value.ErrorCode is int code)
				throw new CoinsPhApiException(code,
					$"Coins.ph WebSocket: {value.ErrorMessage}");
			switch (value.Event)
			{
				case "trade":
					if (TradeReceived is { } tradeHandler)
						await tradeHandler(value, cancellationToken);
					break;
				case "24hrTicker":
					if (TickerReceived is { } tickerHandler)
						await tickerHandler(value, cancellationToken);
					break;
				case "depth":
					if (DepthReceived is { } depthHandler)
						await depthHandler(value, cancellationToken);
					break;
				case "kline":
					if (KlineReceived is { } klineHandler)
						await klineHandler(value, cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask SendCommandAsync(WebSocketClient client,
		CoinsPhSocketCommands command, string[] streams,
		CancellationToken cancellationToken)
		=> SendAsync(client, new CoinsPhSubscriptionCommand
		{
			Method = command,
			Streams = streams,
			Id = Interlocked.Increment(ref _sequence),
		}, cancellationToken);

	private async Task RunPingAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromMinutes(4), cancellationToken);
			try
			{
				await SendAsync(_client, new CoinsPhPingCommand
				{
					Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				}, cancellationToken);
			}
			catch (OperationCanceledException) when (
				cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception error)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
		}
	}

	private async ValueTask SendAsync<TMessage>(WebSocketClient client,
		TMessage message, CancellationToken cancellationToken)
	{
		if (client is null || !client.IsConnected)
			throw new InvalidOperationException(
				"Coins.ph public WebSocket is not connected.");
		var payload = JsonConvert.SerializeObject(message, _jsonSettings);
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

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings) ??
				throw new InvalidDataException(
					"Coins.ph WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Coins.ph WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static string CreateEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint.ThrowIfEmpty(nameof(endpoint)).Trim(),
			UriKind.Absolute, out var uri) || !uri.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"Coins.ph WebSocket endpoint must be an absolute WSS URI.",
				nameof(endpoint));
		return new UriBuilder(uri)
		{
			Path = "/openapi/quote/stream",
			Query = string.Empty,
		}.Uri.AbsoluteUri;
	}
}
