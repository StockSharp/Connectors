namespace StockSharp.Coincall.Native;

sealed class CoincallWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(
		string Channel,
		string Symbol,
		string Period);

	private readonly string _endpoint;
	private readonly string _key;
	private readonly string _secret;
	private readonly CoincallProductTypes _productType;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription>
		_desiredSubscriptions = [];
	private readonly HashSet<Subscription>
		_serverSubscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private WebSocketClient _client;

	public CoincallWsClient(
		string endpoint,
		SecureString key,
		SecureString secret,
		CoincallProductTypes productType,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_key = key.UnSecure().ThrowIfEmpty(nameof(key));
		_secret = secret.UnSecure().ThrowIfEmpty(nameof(secret));
		_productType = productType;
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Coincall_WS";

	public event Func<CoincallWsMessage, CancellationToken, ValueTask>
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
				"Coincall WebSocket is already initialized.");
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

	public ValueTask SendHeartbeatAsync(
		CancellationToken cancellationToken)
		=> _client?.IsConnected == true
			? SendAsync(new { action = "heartbeat" }, cancellationToken)
			: default;

	public ValueTask SubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(
				_productType == CoincallProductTypes.Options
					? "bsInfo"
					: "spotPrice",
				NormalizeSymbol(symbol),
				null),
			true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(
				_productType == CoincallProductTypes.Options
					? "bsInfo"
					: "spotPrice",
				NormalizeSymbol(symbol),
				null),
			false,
			cancellationToken);

	public ValueTask SubscribeOrderBookAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("orderBook", NormalizeSymbol(symbol), null),
			true,
			cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("orderBook", NormalizeSymbol(symbol), null),
			false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("lastTradeV2", NormalizeSymbol(symbol), null),
			true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("lastTradeV2", NormalizeSymbol(symbol), null),
			false,
			cancellationToken);

	public ValueTask SubscribeCandlesAsync(
		string symbol,
		string period,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(
				"kline",
				NormalizeSymbol(symbol),
				period.ThrowIfEmpty(nameof(period))),
			true,
			cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(
		string symbol,
		string period,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(
				"kline",
				NormalizeSymbol(symbol),
				period.ThrowIfEmpty(nameof(period))),
			false,
			cancellationToken);

	public async ValueTask SubscribePrivateAsync(
		CancellationToken cancellationToken)
	{
		foreach (var channel in new[]
		{
			"order",
			"trade",
			"positionEvent",
		})
			await ChangeSubscriptionAsync(
				new(channel, null, null),
				true,
				cancellationToken);
	}

	public async ValueTask UnsubscribePrivateAsync(
		CancellationToken cancellationToken)
	{
		foreach (var channel in new[]
		{
			"order",
			"trade",
			"positionEvent",
		})
			await ChangeSubscriptionAsync(
				new(channel, null, null),
				false,
				cancellationToken);
	}

	internal static string GenerateWebSocketSignature(
		string apiKey,
		long timestamp,
		string secret)
	{
		var value =
			"GET/users/self/verify?apiKey=" +
			apiKey.ThrowIfEmpty(nameof(apiKey)) +
			"&ts=" +
			timestamp.ToString(CultureInfo.InvariantCulture);
		using var hmac = new HMACSHA256(
			Encoding.UTF8.GetBytes(
				secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToHexString(
			hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
	}

	internal static string CreateConnectionUri(
		string endpoint,
		string apiKey,
		string secret,
		long timestamp)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint))
			.TrimEnd('/');
		var signature = GenerateWebSocketSignature(
			apiKey, timestamp, secret);
		return endpoint +
			"?code=10" +
			"&uuid=" + Uri.EscapeDataString(apiKey) +
			"&ts=" + timestamp.ToString(
				CultureInfo.InvariantCulture) +
			"&sign=" + Uri.EscapeDataString(signature) +
			"&apiKey=" + Uri.EscapeDataString(apiKey);
	}

	internal static string CreateSubscriptionJson(
		bool isSubscribe,
		string channel,
		string symbol,
		string period = null)
	{
		var payload = new JObject();
		if (!symbol.IsEmpty())
			payload["symbol"] = NormalizeSymbol(symbol);
		if (!period.IsEmpty())
			payload["period"] = period;
		var root = new JObject
		{
			["action"] = isSubscribe
				? "subscribe"
				: "unSubscribe",
			["dataType"] =
				channel.ThrowIfEmpty(nameof(channel)),
		};
		if (payload.HasValues)
			root["payload"] = payload;
		return root.ToString(Formatting.None);
	}

	internal static CoincallWsMessage DeserializeMessage(string json)
	{
		JObject root;
		try
		{
			root = JObject.Parse(json.ThrowIfEmpty(nameof(json)));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Coincall WebSocket returned invalid JSON.", error);
		}
		var code = root.Value<int?>("c");
		if (code is not (null or 11 or 20))
			throw new InvalidDataException(
				$"Coincall WebSocket request failed ({code}): " +
					root.Value<string>("msg"));
		var data = root["d"];
		if (data is null)
			return new();
		var first = data is JArray array
			? array.FirstOrDefault() as JObject
			: data as JObject;
		if (first is null)
			return new();
		var wrapped = Wrap(data);
		var dt = root.Value<int?>("dt");

		if (dt is 15 or 36)
			return new()
			{
				Fills = CoincallRestClient.DeserializeFills(
					wrapped),
			};

		if (first["asks"] is not null ||
			first["bids"] is not null)
			return new()
			{
				Book = CoincallRestClient.DeserializeBook(
					wrapped,
					first.Value<string>("s") ??
						first.Value<string>("symbol")),
			};

		if (first["oid"] is not null ||
			first["orderId"] is not null)
		{
			if (dt is 15 or 36 ||
				first["tid"] is not null ||
				first["tradeId"] is not null)
				return new()
				{
					Fills = CoincallRestClient.DeserializeFills(
						wrapped),
				};
			return new()
			{
				Orders = CoincallRestClient.DeserializeOrders(
					wrapped),
			};
		}

		if (first["positionId"] is not null ||
			first["upnl"] is not null &&
				first["q"] is not null &&
				first["pr"] is null &&
				first["matchPrice"] is null)
			return new()
			{
				Positions =
					CoincallRestClient.DeserializePositions(
						wrapped),
			};

		if (first["open"] is not null &&
			first["high"] is not null &&
			first["low"] is not null &&
			first["close"] is not null)
		{
			var period =
				first.Value<string>("pe") ??
				first.Value<string>("period") ??
				"m1";
			return new()
			{
				Candle = CoincallRestClient.DeserializeCandles(
					wrapped,
					first.Value<string>("s") ??
						first.Value<string>("symbol"),
					period.ToTimeFrame())
					.FirstOrDefault(),
			};
		}

		if (first["matchPrice"] is not null ||
			first["pr"] is not null &&
				(first["q"] is not null || first["sz"] is not null))
			return new()
			{
				Trades = CoincallRestClient.DeserializeTrades(
					wrapped,
					first.Value<string>("s") ??
						first.Value<string>("symbol")),
			};

		var values = data is JArray valuesArray
			? valuesArray.OfType<JObject>()
			: [first];
		var normalized = new JArray(
			values.Select(value => new JObject
			{
				["symbol"] = value["s"] ?? value["symbol"],
				["lastPrice"] = value["lp"] ?? value["lastPrice"],
				["markPrice"] = value["mp"] ?? value["markPrice"],
				["indexPrice"] = value["ip"] ?? value["indexPrice"],
				["bid"] = value["bp"] ?? value["bid"],
				["ask"] = value["ap"] ?? value["ask"],
				["price24hHigh"] = value["h"] ?? value["high"],
				["price24hLow"] = value["l"] ?? value["low"],
				["volume24h"] =
					value["v24"] ?? value["v"] ?? value["volume"],
				["openInterest"] =
					value["oi"] ?? value["openInterest"],
			}));
		return new()
		{
			Tickers =
				CoincallRestClient.DeserializeInstruments(
					Wrap(normalized),
					dt is < 20
						? CoincallProductTypes.Options
						: CoincallProductTypes.Futures),
		};
	}

	private WebSocketClient CreateClient()
	{
		var timestamp =
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		WebSocketClient client = null;
		client = new WebSocketClient(
			CreateConnectionUri(
				_endpoint, _key, _secret, timestamp),
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
				"StockSharp-Coincall-Connector/1.0");
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
						subscription,
						true,
						cancellationToken);
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
			await handler(state, cancellationToken);
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
				subscription,
				isSubscribe,
				cancellationToken);
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
		=> SendRawAsync(
			CreateSubscriptionJson(
				isSubscribe,
				subscription.Channel,
				subscription.Symbol,
				subscription.Period),
			cancellationToken);

	private async ValueTask SendRawAsync(
		string json,
		CancellationToken cancellationToken)
	{
		var client = _client ?? throw new InvalidOperationException(
			"Coincall WebSocket is disconnected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(
				JObject.Parse(json), cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask SendAsync(
		object value,
		CancellationToken cancellationToken)
	{
		var client = _client ?? throw new InvalidOperationException(
			"Coincall WebSocket is disconnected.");
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

	private async ValueTask OnProcessAsync(
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var parsed = DeserializeMessage(payload);
			if (MessageReceived is { } handler)
				await handler(parsed, cancellationToken);
		}
		catch (Exception error)
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
		=> symbol.ThrowIfEmpty(nameof(symbol))
			.Trim()
			.ToUpperInvariant();

	private static string Wrap(JToken data)
		=> new JObject
		{
			["code"] = 0,
			["data"] = data.DeepClone(),
		}.ToString(Formatting.None);
}
