namespace StockSharp.DeltaExchangeIndia.Native;

sealed class DeltaExchangeIndiaWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(
		string Channel,
		string Symbol);

	private readonly string _endpoint;
	private readonly string _key;
	private readonly string _secret;
	private readonly bool _isPrivate;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _subscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private WebSocketClient _client;

	public DeltaExchangeIndiaWsClient(
		string endpoint,
		SecureString key,
		SecureString secret,
		bool isPrivate,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(_endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("ws" or "wss"))
			throw new ArgumentException(
				"Delta Exchange India WebSocket endpoint must be " +
					"an absolute WS URL.",
				nameof(endpoint));
		_key = key.UnSecure();
		_secret = secret.UnSecure();
		_isPrivate = isPrivate;
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
		if (_isPrivate && (_key.IsEmpty() || _secret.IsEmpty()))
			throw new ArgumentException(
				"Private Delta Exchange India WebSocket requires " +
					"an API key and secret.");
	}

	public override string Name
		=> _isPrivate
			? "DeltaExchangeIndia_Private_WS"
			: "DeltaExchangeIndia_Public_WS";

	public event Func<DeltaWsMessage, CancellationToken, ValueTask>
		MessageReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;

	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Delta Exchange India WebSocket is already initialized.");
		_client = CreateClient();
		try
		{
			await _client.ConnectAsync(cancellationToken);
			await SendAsync(
				new JObject
				{
					["type"] = "enable_heartbeat",
				},
				cancellationToken);
			if (_isPrivate)
				await AuthenticateAsync(cancellationToken);
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
		}
	}

	public async ValueTask SubscribeAsync(
		string channel,
		string symbol,
		CancellationToken cancellationToken)
	{
		var subscription = Normalize(channel, symbol);
		var send = false;
		using (_sync.EnterScope())
			send = _subscriptions.Add(subscription) &&
				_client?.IsConnected == true;
		if (!send)
			return;
		try
		{
			await SendAsync(
				CreateSubscription(
					true,
					subscription.Channel,
					[subscription.Symbol]),
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_subscriptions.Remove(subscription);
			throw;
		}
	}

	public async ValueTask UnsubscribeAsync(
		string channel,
		string symbol,
		CancellationToken cancellationToken)
	{
		var subscription = Normalize(channel, symbol);
		var send = false;
		using (_sync.EnterScope())
			send = _subscriptions.Remove(subscription) &&
				_client?.IsConnected == true;
		if (send)
			await SendAsync(
				CreateSubscription(
					false,
					subscription.Channel,
					[subscription.Symbol]),
				cancellationToken);
	}

	public ValueTask PingAsync(
		CancellationToken cancellationToken)
		=> _client?.IsConnected == true
			? SendAsync(
				new JObject
				{
					["type"] = "ping",
				},
				cancellationToken)
			: default;

	internal static string CreateSubscription(
		bool subscribe,
		string channel,
		IEnumerable<string> symbols)
	{
		channel = channel.ThrowIfEmpty(nameof(channel)).Trim();
		var normalizedSymbols = (symbols ?? [])
			.Select(static symbol =>
				symbol.ThrowIfEmpty(nameof(symbol))
					.Trim().ToUpperInvariant())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (normalizedSymbols.Length == 0)
			throw new ArgumentException(
				"At least one Delta Exchange India symbol is required.",
				nameof(symbols));
		return new JObject
		{
			["type"] = subscribe ? "subscribe" : "unsubscribe",
			["payload"] = new JObject
			{
				["channels"] = new JArray(
					new JObject
					{
						["name"] = channel,
						["symbols"] = new JArray(
							normalizedSymbols),
					}),
			},
		}.ToString(Formatting.None);
	}

	internal static string CreateAuthentication(
		string key,
		string secret,
		long timestamp)
	{
		var timestampText = timestamp.ToString(
			CultureInfo.InvariantCulture);
		return new JObject
		{
			["type"] = "key-auth",
			["payload"] = new JObject
			{
				["api-key"] = key.ThrowIfEmpty(nameof(key)),
				["signature"] =
					DeltaExchangeIndiaRestClient.GenerateSignature(
						"GET",
						timestampText,
						"/live",
						null,
						null,
						secret),
				["timestamp"] = timestamp,
			},
		}.ToString(Formatting.None);
	}

	internal static DeltaWsMessage DeserializeMessage(string payload)
	{
		try
		{
			var root = JObject.Parse(
				payload.ThrowIfEmpty(nameof(payload)));
			var type = root.Value<string>("type");
			if (type.IsEmpty())
				return new();
			if (root.Value<string>("action")
				.EqualsIgnoreCase("error"))
				throw new InvalidDataException(
					"Delta Exchange India WebSocket error: " +
						(root.Value<string>("msg") ??
							root.Value<string>("message")));
			switch (type.ToLowerInvariant())
			{
				case "ticker":
					return new()
					{
						Type = type,
						Tickers = ParseWsTickers(root),
					};

				case "ob_l2":
				case "ob_updates":
					return new()
					{
						Type = type,
						Book =
							DeltaExchangeIndiaRestClient.ParseBook(
								root,
								int.MaxValue),
					};

				case "trades":
					return new()
					{
						Type = type,
						Trade =
							DeltaExchangeIndiaRestClient
								.ParseTrades(
									new JArray(root),
									root.Value<string>("sy"))
								.FirstOrDefault(),
					};

				case var candleType
					when candleType.StartsWith(
						"candlestick_",
						StringComparison.OrdinalIgnoreCase):
					var resolution =
						candleType["candlestick_".Length..];
					return new()
					{
						Type = type,
						Candle =
							DeltaExchangeIndiaRestClient
								.ParseCandles(
									new JArray(root),
									root.Value<string>("sy"),
									DeltaExchangeIndiaExtensions
										.FromResolution(resolution))
								.FirstOrDefault(),
					};

				case "orders":
					return new()
					{
						Type = type,
						Orders = ParseWsOrders(root),
					};

				case "positions":
					return new()
					{
						Type = type,
						Positions = ParseWsPositions(root),
					};

				case "v2/user_trades":
				case "user_trades":
					return new()
					{
						Type = type,
						Fill =
							DeltaExchangeIndiaRestClient
								.ParseFill(root),
					};

				default:
					return new()
					{
						Type = type,
					};
			}
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Delta Exchange India WebSocket returned " +
					"malformed JSON.",
				error);
		}
	}

	private WebSocketClient CreateClient()
	{
		var client = new WebSocketClient(
			_endpoint,
			OnStateChangedAsync,
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
		};
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent",
				"StockSharp-DeltaExchangeIndia-Connector/1.0");
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
			await SendAsync(
				new JObject
				{
					["type"] = "enable_heartbeat",
				},
				cancellationToken);
			if (_isPrivate)
				await AuthenticateAsync(cancellationToken);
			Subscription[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];

			foreach (var subscription in subscriptions)
				await SendAsync(
					CreateSubscription(
						true,
						subscription.Channel,
						[subscription.Symbol]),
					cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler.InvokeAsync(state, cancellationToken);
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
			var type = root.Value<string>("type");
			if (type is "heartbeat" or "pong" or "subscriptions")
				return;
			if (type.EqualsIgnoreCase("key-auth"))
			{
				if (root.Value<bool?>("success") == false)
					throw new InvalidDataException(
						"Delta Exchange India WebSocket " +
							$"authentication failed: " +
							(root.Value<string>("message") ??
								root.Value<string>("status")));
				return;
			}
			var parsed = DeserializeMessage(payload);
			if (parsed.Type.IsEmpty())
				return;
			if (MessageReceived is { } handler)
				await handler.InvokeAsync(parsed, cancellationToken);
		}
		catch (Exception error) when (
			error is JsonException or InvalidDataException or
				InvalidOperationException or FormatException or
				OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask AuthenticateAsync(
		CancellationToken cancellationToken)
		=> SendAsync(
			CreateAuthentication(
				_key,
				_secret,
				DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
			cancellationToken);

	private async ValueTask SendAsync(
		object value,
		CancellationToken cancellationToken)
	{
		var client = _client ?? throw new InvalidOperationException(
			"Delta Exchange India WebSocket is disconnected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(value, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
	{
		if (Error is { } handler)
			await handler.InvokeAsync(error, cancellationToken);
	}

	private static Subscription Normalize(
		string channel,
		string symbol)
		=> new(
			channel.ThrowIfEmpty(nameof(channel)).Trim(),
			symbol.ThrowIfEmpty(nameof(symbol))
				.Trim().ToUpperInvariant());

	private static DeltaTicker[] ParseWsTickers(JObject root)
	{
		var time =
			DeltaExchangeIndiaRestClient.Time(root["ts"]) ??
			DateTime.UtcNow;
		var spotPrice =
			DeltaExchangeIndiaRestClient.Decimal(root["sp"]);
		return [.. (root["d"] as JArray ?? [])
			.OfType<JObject>()
			.Select(item =>
			{
				var ohlc = item["ohlc"] as JArray;
				var quotes = item["q"] as JArray;
				var openInterest = item["oi"] as JArray;
				return new DeltaTicker
				{
					Symbol = item.Value<string>("s"),
					Time = time,
					Open = ValueAt(ohlc, 0),
					High = ValueAt(ohlc, 1),
					Low = ValueAt(ohlc, 2),
					Last = ValueAt(ohlc, 3),
					MarkPrice =
						DeltaExchangeIndiaRestClient
							.Decimal(item["m"]),
					SpotPrice = spotPrice,
					BestAsk = ValueAt(quotes, 0),
					AskVolume = ValueAt(quotes, 1),
					BestBid = ValueAt(quotes, 2),
					BidVolume = ValueAt(quotes, 3),
					OpenInterest =
						ValueAt(openInterest, 0),
				};
			})
			.Where(static ticker => !ticker.Symbol.IsEmpty())];
	}

	private static DeltaOrder[] ParseWsOrders(JObject root)
	{
		var symbol = root.Value<string>("symbol");
		var values = root["result"] is JArray array
			? array.OfType<JObject>()
			: [root];
		return [.. values
			.Select(item =>
			{
				if (!symbol.IsEmpty() &&
					item["symbol"] is null &&
					item["product_symbol"] is null)
				{
					item = (JObject)item.DeepClone();
					item["symbol"] = symbol;
				}
				return DeltaExchangeIndiaRestClient.ParseOrder(item);
			})
			.Where(static order => order is not null)];
	}

	private static DeltaPosition[] ParseWsPositions(JObject root)
	{
		var symbol = root.Value<string>("symbol");
		var values = root["result"] is JArray array
			? array.OfType<JObject>()
			: [root];
		return [.. values
			.Select(item =>
			{
				if (!symbol.IsEmpty() &&
					item["symbol"] is null &&
					item["product_symbol"] is null)
				{
					item = (JObject)item.DeepClone();
					item["symbol"] = symbol;
				}
				return DeltaExchangeIndiaRestClient
					.ParsePosition(item);
			})
			.Where(static position => position is not null)];
	}

	private static decimal? ValueAt(JArray array, int index)
		=> array is { Count: > 0 } && index < array.Count
			? DeltaExchangeIndiaRestClient.Decimal(array[index])
			: null;
}
