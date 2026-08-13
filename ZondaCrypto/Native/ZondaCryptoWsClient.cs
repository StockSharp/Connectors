namespace StockSharp.ZondaCrypto.Native;

sealed class ZondaCryptoWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(
		string Module,
		string Path,
		bool IsPrivate);

	private readonly string _endpoint;
	private readonly ZondaCryptoAuthenticator _authenticator;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _desiredSubscriptions = [];
	private readonly HashSet<Subscription> _serverSubscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private WebSocketClient _client;
	private long _lastTimestamp;

	public ZondaCryptoWsClient(
		string endpoint,
		SecureString key,
		SecureString secret,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = ValidateEndpoint(endpoint);
		_authenticator = new(key, secret);
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "ZondaCrypto_WS";

	public event Func<
		ZondaCryptoWsMessage,
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
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"zondacrypto WebSocket is already initialized.");
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
				_serverSubscriptions.Clear();
		}
	}

	public ValueTask SubscribePublicAsync(
		string module,
		string path,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(NormalizeModule(module), NormalizePath(path), false),
			true,
			cancellationToken);

	public ValueTask UnsubscribePublicAsync(
		string module,
		string path,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(NormalizeModule(module), NormalizePath(path), false),
			false,
			cancellationToken);

	public ValueTask SubscribePrivateAsync(
		string module,
		string path,
		CancellationToken cancellationToken)
	{
		if (!_authenticator.IsAvailable)
			throw new InvalidOperationException(
				"zondacrypto API key and secret are required for " +
					"private WebSocket subscriptions.");
		return ChangeSubscriptionAsync(
			new(NormalizeModule(module), NormalizePath(path), true),
			true,
			cancellationToken);
	}

	public ValueTask UnsubscribePrivateAsync(
		string module,
		string path,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(NormalizeModule(module), NormalizePath(path), true),
			false,
			cancellationToken);

	public ValueTask SendHeartbeatAsync(
		CancellationToken cancellationToken)
		=> _client?.IsConnected == true
			? SendAsync(new JObject
			{
				["action"] = "ping",
			}, cancellationToken)
			: default;

	internal static string CreateSubscriptionJson(
		bool isSubscribe,
		bool isPrivate,
		string module,
		string path,
		string key,
		string secret,
		string timestamp)
	{
		module = NormalizeModule(module);
		path = NormalizePath(path);
		var body = new JObject
		{
			["action"] = isSubscribe
				? isPrivate
					? "subscribe-private"
					: "subscribe-public"
				: "unsubscribe",
			["module"] = module,
			["path"] = path,
		};
		if (isSubscribe && isPrivate)
		{
			body["hashSignature"] =
				ZondaCryptoAuthenticator.Sign(
					key, secret, timestamp);
			body["publicKey"] = key;
			body["requestTimestamp"] = long.TryParse(
				timestamp,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var numericTimestamp)
					? numericTimestamp
					: timestamp;
		}
		return body.ToString(Formatting.None);
	}

	internal static ZondaCryptoWsMessage DeserializeMessage(
		string payload)
	{
		try
		{
			var root = JObject.Parse(
				payload.ThrowIfEmpty(nameof(payload)));
			var action = root.Value<string>("action");
			if (action?.EndsWith(
				"-error",
				StringComparison.OrdinalIgnoreCase) == true ||
				root["error"] is not null)
				throw new InvalidDataException(
					"zondacrypto WebSocket request failed: " +
					(root["error"]?.ToString() ??
						root.Value<string>("message") ??
						action));
			var topic = root.Value<string>("topic");
			var message = root["message"] as JObject;
			var marketCode = GetMarketCode(topic);
			var ticker = topic?.Contains(
				"/ticker",
				StringComparison.OrdinalIgnoreCase) == true
					? ZondaCryptoRestClient.ParseTicker(
						message?["ticker"] as JObject ?? message,
						marketCode)
					: null;
			var bookChanges = topic?.Contains(
				"/orderbook/",
				StringComparison.OrdinalIgnoreCase) == true
					? ParseBookChanges(
						message?["changes"] as JArray,
						marketCode)
					: [];
			var trades = topic?.Contains(
				"/transactions/",
				StringComparison.OrdinalIgnoreCase) == true
					? ParseTrades(message, marketCode)
					: [];
			var wallet = topic?.Contains(
				"balances/balance/",
				StringComparison.OrdinalIgnoreCase) == true
					? ZondaCryptoRestClient.ParseWallet(message)
					: null;
			var offer = topic?.Contains(
				"trading/offers",
				StringComparison.OrdinalIgnoreCase) == true
					? ZondaCryptoRestClient.ParseOffer(
						message?["state"] as JObject ?? message)
					: null;
			return new()
			{
				Action = action,
				Topic = topic,
				Module = root.Value<string>("module"),
				Path = root.Value<string>("path"),
				Time = ReadTimestamp(
					root["timestamp"] ?? message?["timestamp"]),
				Sequence = ReadLong(root["seqNo"]),
				Ticker = ticker,
				BookChanges = bookChanges,
				Trades = trades,
				Wallet = wallet,
				Offer = offer,
			};
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"zondacrypto WebSocket returned malformed JSON.",
				error);
		}
	}

	private WebSocketClient CreateClient()
	{
		var client = new WebSocketClient(
			_endpoint,
			(state, token) =>
				OnStateChangedAsync(state, token),
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
				"StockSharp-ZondaCrypto-Connector/1.0");
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
			Subscription[] subscriptions;
			using (_sync.EnterScope())
			{
				_serverSubscriptions.Clear();
				subscriptions = [.. _desiredSubscriptions];
			}
			foreach (var subscription in subscriptions)
			{
				using (_sync.EnterScope())
				{
					if (!_desiredSubscriptions.Contains(subscription) ||
						!_serverSubscriptions.Add(subscription))
						continue;
				}
				try
				{
					await SendSubscriptionAsync(
						subscription, true, cancellationToken);
				}
				catch
				{
					using (_sync.EnterScope())
						_serverSubscriptions.Remove(subscription);
					throw;
				}
			}
		}
		if (StateChanged is { } handler)
			await handler.InvokeAsync(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(
		Subscription subscription,
		bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var send = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				_desiredSubscriptions.Add(subscription);
				send = _client?.IsConnected == true &&
					_serverSubscriptions.Add(subscription);
			}
			else
			{
				_desiredSubscriptions.Remove(subscription);
				send = _client?.IsConnected == true &&
					_serverSubscriptions.Remove(subscription);
			}
		}
		if (!send)
			return;
		try
		{
			await SendSubscriptionAsync(
				subscription, isSubscribe, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_serverSubscriptions.Remove(subscription);
				else
					_serverSubscriptions.Add(subscription);
			}
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(
		Subscription subscription,
		bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var timestamp = NextTimestamp().ToString(
			CultureInfo.InvariantCulture);
		return SendAsync(JObject.Parse(CreateSubscriptionJson(
			isSubscribe,
			subscription.IsPrivate,
			subscription.Module,
			subscription.Path,
			_authenticator.Key,
			subscription.IsPrivate
				? _authenticator.Secret
				: null,
			timestamp)), cancellationToken);
	}

	private async ValueTask SendAsync(
		object body,
		CancellationToken cancellationToken)
	{
		var client = _client ?? throw new InvalidOperationException(
			"zondacrypto WebSocket is disconnected.");
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
			var value = DeserializeMessage(payload);
			if (value.Action.EqualsIgnoreCase("pong"))
				return;
			if (MessageReceived is { } handler)
				await handler.InvokeAsync(value, cancellationToken);
		}
		catch (Exception error) when (
			error is JsonException or InvalidDataException or
				InvalidOperationException or FormatException or
				OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private static ZondaCryptoBookChange[] ParseBookChanges(
		JArray values,
		string marketCode)
		=> [.. (values ?? [])
			.OfType<JObject>()
			.Select(value =>
			{
				var state = value["state"] as JObject;
				var action = value.Value<string>("action");
				return new ZondaCryptoBookChange
				{
					MarketCode =
						value.Value<string>("marketCode") ??
						marketCode,
					Side = value.Value<string>("entryType").ToSide(),
					Price = ReadDecimal(
						value["rate"] ?? state?["ra"]),
					Volume = action.EqualsIgnoreCase("remove")
						? 0
						: ReadDecimal(state?["ca"]),
					IsRemove =
						action.EqualsIgnoreCase("remove"),
				};
			})
			.Where(static change => change.Price > 0)];

	private static ZondaCryptoTrade[] ParseTrades(
		JObject message,
		string marketCode)
	{
		var values = message?["transactions"] as JArray ??
			message?["items"] as JArray;
		if (values is null && message is not null &&
			message["id"] is not null)
			values = new(message);
		return [.. (values ?? [])
			.OfType<JObject>()
			.Select(value => ZondaCryptoRestClient.ParseTrade(
				value, marketCode))
			.Where(static trade => trade is not null)];
	}

	private long NextTimestamp()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastTimestamp);
			var next = Math.Max(
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				current + 1);
			if (Interlocked.CompareExchange(
				ref _lastTimestamp, next, current) == current)
				return next;
		}
	}

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler.InvokeAsync(error, cancellationToken)
			: default;

	private static string GetMarketCode(string topic)
	{
		if (topic.IsEmpty())
			return null;
		var value = topic.Split(
			'/',
			StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries).LastOrDefault();
		return value?.Contains('-') == true
			? value.ToUpperInvariant()
			: null;
	}

	private static decimal ReadDecimal(JToken value)
		=> decimal.TryParse(
			value?.ToString(),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	private static long ReadLong(JToken value)
		=> long.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	private static DateTime ReadTimestamp(JToken value)
	{
		var timestamp = ReadLong(value);
		return timestamp > 0
			? timestamp.FromZondaTimestamp()
			: DateTime.UtcNow;
	}

	private static string NormalizeModule(string module)
		=> module.ThrowIfEmpty(nameof(module))
			.Trim().ToLowerInvariant();

	private static string NormalizePath(string path)
		=> path.ThrowIfEmpty(nameof(path))
			.Trim().Trim('/').ToLowerInvariant();

	private static string ValidateEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(
			nameof(endpoint)).Trim();
		if (!Uri.TryCreate(
			endpoint,
			UriKind.Absolute,
			out var uri) ||
			!uri.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"zondacrypto WebSocket endpoint must be an " +
					"absolute WSS URI.",
				nameof(endpoint));
		return endpoint;
	}
}
