namespace StockSharp.LCX.Native;

sealed class LcxWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly string _key;
	private readonly string _secret;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, WebSocketClient> _clients =
		new(StringComparer.OrdinalIgnoreCase);
	private bool _isConnected;

	public LcxWsClient(
		string endpoint,
		SecureString key,
		SecureString secret,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = ValidateEndpoint(endpoint);
		_key = key.IsEmpty() ? null : key.UnSecure().Trim();
		_secret = secret.IsEmpty()
			? null
			: secret.UnSecure().Trim();
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "LCX_WS";

	public event Func<
		LcxWsMessage,
		CancellationToken,
		ValueTask> MessageReceived;

	public event Func<
		Exception,
		CancellationToken,
		ValueTask> Error;

	public event Func<
		ConnectionStates,
		CancellationToken,
		ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		WebSocketClient[] clients;
		using (_sync.EnterScope())
		{
			_isConnected = false;
			clients = [.. _clients.Values];
			_clients.Clear();
		}
		foreach (var client in clients)
			client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using (_sync.EnterScope())
		{
			if (_isConnected)
				throw new InvalidOperationException(
					"LCX WebSocket manager is already connected.");
			_isConnected = true;
		}
		return default;
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		WebSocketClient[] clients;
		using (_sync.EnterScope())
		{
			_isConnected = false;
			clients = [.. _clients.Values];
			_clients.Clear();
		}
		foreach (var client in clients)
			await DisconnectClientAsync(client, cancellationToken);
	}

	public async ValueTask SubscribeAsync(
		string type,
		string pair,
		bool isPrivate,
		CancellationToken cancellationToken)
	{
		type = NormalizeType(type);
		pair = pair?.Trim().ToUpperInvariant();
		if (isPrivate &&
			(_key.IsEmpty() || _secret.IsEmpty()))
			throw new InvalidOperationException(
				"LCX API key and secret are required for private " +
					"WebSocket subscriptions.");
		var key = CreateKey(type, pair, isPrivate);
		var subscription = CreateSubscription(
			type, pair, true);
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			if (!_isConnected)
				throw new InvalidOperationException(
					"LCX WebSocket manager is disconnected.");
			if (_clients.ContainsKey(key))
				return;
			client = CreateClient(
				key,
				subscription,
				isPrivate);
			_clients.Add(key, client);
		}
		try
		{
			await client.ConnectAsync(cancellationToken);
			await client.SendAsync(
				subscription, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_clients.Remove(key);
			client.Dispose();
			throw;
		}
	}

	public async ValueTask UnsubscribeAsync(
		string type,
		string pair,
		bool isPrivate,
		CancellationToken cancellationToken)
	{
		type = NormalizeType(type);
		pair = pair?.Trim().ToUpperInvariant();
		var key = CreateKey(type, pair, isPrivate);
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			if (!_clients.Remove(key, out client))
				return;
		}
		try
		{
			if (client.IsConnected)
				await client.SendAsync(
					CreateSubscription(type, pair, false),
					cancellationToken);
		}
		finally
		{
			await DisconnectClientAsync(
				client, cancellationToken);
		}
	}

	public async ValueTask PingAsync(
		CancellationToken cancellationToken)
	{
		WebSocketClient[] clients;
		using (_sync.EnterScope())
			clients = [.. _clients.Values];
		foreach (var client in clients)
		{
			if (client.IsConnected)
				await client.SendAsync("ping", cancellationToken);
		}
	}

	internal static string CreateSubscription(
		string type,
		string pair,
		bool isSubscribe)
	{
		var body = new JObject
		{
			["Topic"] = isSubscribe
				? "subscribe"
				: "unsubscribe",
			["Type"] = NormalizeType(type),
		};
		if (!pair.IsEmpty())
			body["Pair"] = pair.Trim().ToUpperInvariant();
		return body.ToString(Formatting.None);
	}

	internal static string CreatePrivateEndpoint(
		string endpoint,
		string key,
		string signature,
		long timestamp)
		=> ValidateEndpoint(endpoint) +
			"/api/auth/ws?x-access-key=" +
			Uri.EscapeDataString(
				key.ThrowIfEmpty(nameof(key))) +
			"&x-access-sign=" +
			Uri.EscapeDataString(
				signature.ThrowIfEmpty(nameof(signature))) +
			"&x-access-timestamp=" +
			timestamp.ToString(CultureInfo.InvariantCulture);

	internal static LcxWsMessage DeserializeMessage(
		string payload)
	{
		try
		{
			var root = JObject.Parse(
				payload.ThrowIfEmpty(nameof(payload)));
			var type = root.Value<string>("type")
				?.Trim().ToLowerInvariant();
			var topic = root.Value<string>("topic")
				?.Trim().ToLowerInvariant();
			var pair = root.Value<string>("pair")
				?.ToUpperInvariant();
			if (type.EqualsIgnoreCase("error") ||
				topic.EqualsIgnoreCase("error") ||
				root["error"] is not null)
				throw new InvalidDataException(
					"LCX WebSocket error: " +
						(root["error"]?.ToString() ??
							root.Value<string>("message")));
			var data = root["data"];
			return type switch
			{
				"ticker" => new()
				{
					Type = type,
					Topic = topic,
					Pair = pair,
					Tickers = ParseTickers(data, pair),
				},
				"orderbook" => new()
				{
					Type = type,
					Topic = topic,
					Pair = pair,
					Book = ParseBook(
						data,
						pair,
						topic.EqualsIgnoreCase("snapshot")),
				},
				"trade" => new()
				{
					Type = type,
					Topic = topic,
					Pair = pair,
					Trades = LcxRestClient.ParsePublicTrades(
						data, pair),
				},
				"user_wallets" => new()
				{
					Type = type,
					Topic = topic,
					Balances =
						LcxRestClient.ParseBalances(data),
				},
				"user_orders" => new()
				{
					Type = type,
					Topic = topic,
					Order = LcxRestClient.ParseOrder(
						data as JObject),
				},
				"user_trades" => new()
				{
					Type = type,
					Topic = topic,
					UserTrade =
						LcxRestClient.ParseUserTrade(
							data as JObject),
				},
				_ => new()
				{
					Type = type,
					Topic = topic,
					Pair = pair,
				},
			};
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"LCX WebSocket returned malformed JSON.",
				error);
		}
	}

	private WebSocketClient CreateClient(
		string key,
		string subscription,
		bool isPrivate)
	{
		var endpoint = _endpoint + "/ws";
		if (isPrivate)
		{
			var signature = LcxRestClient.GenerateSignature(
				"GET",
				"/api/auth/ws",
				"{}",
				_secret);
			endpoint = CreatePrivateEndpoint(
				_endpoint,
				_key,
				signature,
				DateTimeOffset.UtcNow
					.ToUnixTimeMilliseconds());
		}
		WebSocketClient client = null;
		client = new WebSocketClient(
			endpoint,
			async (state, cancellationToken) =>
			{
				if (state == ConnectionStates.Restored &&
					client?.IsConnected == true)
					await client.SendAsync(
						subscription, cancellationToken);
				await RaiseStateAsync(
					state, cancellationToken);
			},
			(error, cancellationToken) =>
				RaiseErrorAsync(error, cancellationToken),
			(_, message, cancellationToken) =>
				OnProcessAsync(
					key, message, cancellationToken),
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
				"StockSharp-LCX-Connector/1.0");
			return default;
		};
		return client;
	}

	private async ValueTask OnProcessAsync(
		string key,
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty() ||
			payload.EqualsIgnoreCase("pong") ||
			payload.EqualsIgnoreCase("ping"))
			return;
		try
		{
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
			this.AddErrorLog(
				"LCX stream {0}: {1}", key, error.Message);
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private static LcxTicker[] ParseTickers(
		JToken token,
		string pair)
	{
		if (token is not JObject data)
			return [];
		if (!pair.IsEmpty() &&
			LcxRestClient.GetToken(data, pair) is JObject nested)
			return [LcxRestClient.ParseTicker(nested, pair)];
		var symbol = LcxRestClient.GetToken(
			data, "symbol")?.ToString();
		if (!symbol.IsEmpty())
			return [LcxRestClient.ParseTicker(data, pair)];
		return [.. data.Properties()
			.Where(static property =>
				property.Value is JObject)
			.Select(static property =>
				LcxRestClient.ParseTicker(
					(JObject)property.Value,
					property.Name))
			.Where(static ticker => ticker is not null)];
	}

	private static LcxBook ParseBook(
		JToken token,
		string pair,
		bool isSnapshot)
	{
		if (token is JObject data)
			return LcxRestClient.ParseBook(
				data, pair, isSnapshot);
		if (token is not JArray updates)
			return null;
		var bids = new List<LcxQuote>();
		var asks = new List<LcxQuote>();
		foreach (var value in updates.OfType<JArray>())
		{
			if (value.Count < 3)
				continue;
			var quote = new LcxQuote
			{
				Price = LcxRestClient.ReadDecimal(value[0]),
				Volume = LcxRestClient.ReadDecimal(value[1]),
				Side = value[2]?.ToString().ToLcxSide() ??
					Sides.Buy,
			};
			if (quote.Price <= 0)
				continue;
			(quote.Side == Sides.Buy ? bids : asks)
				.Add(quote);
		}
		return new()
		{
			Symbol = pair,
			IsSnapshot = false,
			Bids = [.. bids],
			Asks = [.. asks],
		};
	}

	private static string CreateKey(
		string type,
		string pair,
		bool isPrivate)
		=> (isPrivate ? "private:" : "public:") +
			type + ":" + pair;

	private static string NormalizeType(string value)
		=> value.ThrowIfEmpty(nameof(value))
			.Trim().ToLowerInvariant();

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler.InvokeAsync(error, cancellationToken)
			: default;

	private ValueTask RaiseStateAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
		=> StateChanged is { } handler
			? handler.InvokeAsync(state, cancellationToken)
			: default;

	private static async ValueTask DisconnectClientAsync(
		WebSocketClient client,
		CancellationToken cancellationToken)
	{
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

	private static string ValidateEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(
			nameof(endpoint)).Trim().TrimEnd('/');
		if (!Uri.TryCreate(
			endpoint,
			UriKind.Absolute,
			out var value) ||
			!value.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"LCX WebSocket endpoint must be an absolute " +
					"WSS URI.",
				nameof(endpoint));
		return endpoint;
	}
}
