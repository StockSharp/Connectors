namespace StockSharp.Tokocrypto.Native;

sealed class TokocryptoWsClient : BaseLogReceiver
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
	};
	private WebSocketClient _client;
	private long _requestId;

	public TokocryptoWsClient(
		string endpoint,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Tokocrypto_WS";

	public event Func<TokocryptoTicker,
		CancellationToken, ValueTask> TickerReceived;
	public event Func<TokocryptoOrderBook,
		CancellationToken, ValueTask> OrderBookReceived;
	public event Func<TokocryptoTradePush,
		CancellationToken, ValueTask> TradesReceived;
	public event Func<TokocryptoTrade,
		CancellationToken, ValueTask> TradeReceived;
	public event Func<TokocryptoKlineEvent,
		CancellationToken, ValueTask> KlineReceived;
	public event Func<TokocryptoKlineEvent,
		CancellationToken, ValueTask> CandleReceived;
	public event Func<Exception,
		CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates,
		CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Tokocrypto WebSocket is already initialized.");
		_client = CreateClient();
		try
		{
			await _client.ConnectAsync(cancellationToken);
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
		try
		{
			if (client?.IsConnected == true)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client?.Dispose();
			using (_sync.EnterScope())
			{
				_desiredStreams.Clear();
				_serverStreams.Clear();
			}
		}
	}

	public ValueTask SubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{NormalizeSymbol(symbol)}@miniTicker",
			true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{NormalizeSymbol(symbol)}@miniTicker",
			false,
			cancellationToken);

	public ValueTask SubscribeOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{NormalizeSymbol(symbol)}@depth" +
				$"{TokocryptoRestClient.NormalizeDepth(depth)}@100ms",
			true,
			cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{NormalizeSymbol(symbol)}@depth" +
				$"{TokocryptoRestClient.NormalizeDepth(depth)}@100ms",
			false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{NormalizeSymbol(symbol)}@trade",
			true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			$"{NormalizeSymbol(symbol)}@trade",
			false,
			cancellationToken);

	public ValueTask SubscribeKlineAsync(
		string symbol,
		string resolution,
		CancellationToken cancellationToken)
	{
		ValidateResolution(resolution);
		return ChangeSubscriptionAsync(
			$"{NormalizeSymbol(symbol)}@kline_{resolution}",
			true,
			cancellationToken);
	}

	public ValueTask UnsubscribeKlineAsync(
		string symbol,
		string resolution,
		CancellationToken cancellationToken)
	{
		ValidateResolution(resolution);
		return ChangeSubscriptionAsync(
			$"{NormalizeSymbol(symbol)}@kline_{resolution}",
			false,
			cancellationToken);
	}

	public ValueTask SubscribeCandleAsync(
		string symbol,
		string resolution,
		CancellationToken cancellationToken)
		=> SubscribeKlineAsync(
			symbol, resolution, cancellationToken);

	public ValueTask UnsubscribeCandleAsync(
		string symbol,
		string resolution,
		CancellationToken cancellationToken)
		=> UnsubscribeKlineAsync(
			symbol, resolution, cancellationToken);

	internal static TokocryptoWsEnvelope<TData>
		DeserializeEnvelope<TData>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<
				TokocryptoWsEnvelope<TData>>(
				payload.ThrowIfEmpty(nameof(payload)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				}) ?? throw new InvalidDataException(
					"Tokocrypto WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Tokocrypto WebSocket returned malformed JSON.",
				error);
		}
	}

	private WebSocketClient CreateClient()
	{
		var client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(_, message, token) =>
				OnProcessAsync(message, token),
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
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent",
				"StockSharp-Tokocrypto-Connector/1.0");
			return default;
		};
		return client;
	}

	private async ValueTask OnStateChangedAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			string[] streams;
			using (_sync.EnterScope())
			{
				_serverStreams.Clear();
				streams = [.. _desiredStreams];
				_serverStreams.AddRange(streams);
			}
			foreach (var stream in streams)
			{
				try
				{
					await SendSubscriptionAsync(
						stream, true, cancellationToken);
				}
				catch
				{
					using (_sync.EnterScope())
						_serverStreams.Remove(stream);
					throw;
				}
			}
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(
		string stream,
		bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var client = _client ??
			throw new InvalidOperationException(
				"Tokocrypto WebSocket is disconnected.");
		var send = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				_desiredStreams.Add(stream);
				send = client.IsConnected &&
					_serverStreams.Add(stream);
			}
			else
			{
				_desiredStreams.Remove(stream);
				send = client.IsConnected &&
					_serverStreams.Remove(stream);
			}
		}
		if (!send)
			return;
		try
		{
			await SendSubscriptionAsync(
				stream, isSubscribe, cancellationToken);
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

	private ValueTask SendSubscriptionAsync(
		string stream,
		bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(new
		{
			method = isSubscribe ? "SUBSCRIBE" : "UNSUBSCRIBE",
			@params = new[] { stream },
			id = Interlocked.Increment(ref _requestId),
		}, cancellationToken);

	private async ValueTask SendAsync(
		object body,
		CancellationToken cancellationToken)
	{
		var client = _client ??
			throw new InvalidOperationException(
				"Tokocrypto WebSocket is disconnected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(body, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var root = JObject.Parse(payload);
			if (root["stream"] is null)
			{
				var code = root.Value<int?>("code");
				if (root["error"] is not null ||
					code is not null)
					throw new InvalidDataException(
						$"Tokocrypto WebSocket error " +
							$"{code}: {root["msg"] ?? root["error"]}");
				return;
			}

			var stream = root.Value<string>("stream");
			if (stream.EndsWithIgnoreCase("@miniTicker"))
			{
				var envelope = DeserializeEnvelope<
					TokocryptoMiniTicker>(payload);
				var ticker = envelope.Data?.ToTicker();
				if (ticker is not null &&
					TickerReceived is { } tickerHandler)
					await tickerHandler(
						ticker, cancellationToken);
				return;
			}

			if (stream.ContainsIgnoreCase("@depth"))
			{
				var envelope = DeserializeEnvelope<
					TokocryptoOrderBook>(payload);
				if (envelope.Data is null)
					return;
				envelope.Data.Pair = GetStreamSymbol(stream);
				envelope.Data.Timestamp =
					DateTime.UtcNow.ToTokocryptoMilliseconds();
				envelope.Data.Limit = GetDepth(stream);
				if (OrderBookReceived is { } bookHandler)
					await bookHandler(
						envelope.Data, cancellationToken);
				return;
			}

			if (stream.EndsWithIgnoreCase("@trade"))
			{
				var envelope = DeserializeEnvelope<
					TokocryptoStreamTrade>(payload);
				var trade = envelope.Data?.ToTrade();
				if (trade is null)
					return;
				var push = new TokocryptoTradePush
				{
					Pair = envelope.Data.Symbol,
					EventId = envelope.Data.Id.ToString(
						CultureInfo.InvariantCulture),
					Data = [trade],
				};
				if (TradesReceived is { } tradesHandler)
					await tradesHandler(
						push, cancellationToken);
				if (TradeReceived is { } tradeHandler)
					await tradeHandler(
						trade, cancellationToken);
				return;
			}

			if (stream.ContainsIgnoreCase("@kline_"))
			{
				var envelope = DeserializeEnvelope<
					TokocryptoKlineEvent>(payload);
				if (envelope.Data is null)
					return;
				if (KlineReceived is { } klineHandler)
					await klineHandler(
						envelope.Data, cancellationToken);
				if (CandleReceived is { } candleHandler)
					await candleHandler(
						envelope.Data, cancellationToken);
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or
			FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	private static string NormalizeSymbol(string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim();
		if (symbol.Contains('/') ||
			symbol.Contains('_') ||
			symbol.Contains('-'))
			symbol = symbol.ToTokocryptoMarketSymbol();
		return symbol.ToLowerInvariant();
	}

	private static string GetStreamSymbol(string stream)
		=> stream.ThrowIfEmpty(nameof(stream))
			.Split('@')[0]
			.ToUpperInvariant();

	private static int GetDepth(string stream)
	{
		var marker = stream.IndexOf(
			"@depth", StringComparison.OrdinalIgnoreCase);
		if (marker < 0)
			return 0;
		var value = stream[(marker + 6)..]
			.Split('@')[0];
		return int.TryParse(
			value,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var depth)
				? depth
				: 0;
	}

	private static void ValidateResolution(string resolution)
	{
		if (resolution.IsEmpty() ||
			!TokocryptoExtensions.TimeFrames.Any(
				timeFrame => timeFrame.ToTokocryptoInterval()
					.EqualsIgnoreCase(resolution)))
			throw new ArgumentOutOfRangeException(
				nameof(resolution), resolution,
				"Unsupported Tokocrypto candle interval.");
	}
}
