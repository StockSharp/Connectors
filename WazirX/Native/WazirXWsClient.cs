namespace StockSharp.WazirX.Native;

sealed class WazirXWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _publicStreams =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _privateStreams =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private WebSocketClient _client;
	private string _authKey;

	public WazirXWsClient(
		string endpoint,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = ValidateEndpoint(endpoint);
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "WazirX_WS";

	public event Func<
		WazirXWsMessage,
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
		_client = null;
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"WazirX WebSocket is already initialized.");
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
				_publicStreams.Clear();
				_privateStreams.Clear();
				_authKey = null;
			}
		}
	}

	public void SetAuthKey(string authKey)
	{
		using (_sync.EnterScope())
			_authKey = authKey.ThrowIfEmpty(nameof(authKey));
	}

	public async ValueTask SubscribeAsync(
		string stream,
		bool isPrivate,
		CancellationToken cancellationToken)
	{
		stream = NormalizeStream(stream);
		string authKey;
		var send = false;
		using (_sync.EnterScope())
		{
			var streams = isPrivate
				? _privateStreams
				: _publicStreams;
			if (!streams.Add(stream))
				return;
			authKey = isPrivate
				? _authKey.ThrowIfEmpty(nameof(_authKey))
				: null;
			send = _client?.IsConnected == true;
		}
		if (!send)
			return;
		try
		{
			await SendAsync(
				CreateSubscription(
					[stream], true, authKey),
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				(isPrivate
					? _privateStreams
					: _publicStreams).Remove(stream);
			throw;
		}
	}

	public async ValueTask UnsubscribeAsync(
		string stream,
		bool isPrivate,
		CancellationToken cancellationToken)
	{
		stream = NormalizeStream(stream);
		string authKey;
		var send = false;
		using (_sync.EnterScope())
		{
			var streams = isPrivate
				? _privateStreams
				: _publicStreams;
			if (!streams.Remove(stream))
				return;
			authKey = isPrivate ? _authKey : null;
			send = _client?.IsConnected == true;
		}
		if (send)
			await SendAsync(
				CreateSubscription(
					[stream], false, authKey),
				cancellationToken);
	}

	public ValueTask PingAsync(
		CancellationToken cancellationToken)
		=> _client?.IsConnected == true
			? SendAsync(
				new JObject
				{
					["event"] = "ping",
				}.ToString(Formatting.None),
				cancellationToken)
			: default;

	internal static string CreateSubscription(
		IEnumerable<string> streams,
		bool isSubscribe,
		string authKey)
	{
		var values = (streams ?? [])
			.Select(NormalizeStream)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (values.Length == 0)
			throw new ArgumentException(
				"At least one WazirX stream is required.",
				nameof(streams));
		var body = new JObject
		{
			["event"] = isSubscribe
				? "subscribe"
				: "unsubscribe",
			["streams"] = new JArray(values),
		};
		if (!authKey.IsEmpty())
			body["auth_key"] = authKey;
		return body.ToString(Formatting.None);
	}

	internal static WazirXWsMessage DeserializeMessage(
		string payload)
	{
		try
		{
			var root = JObject.Parse(
				payload.ThrowIfEmpty(nameof(payload)));
			var eventName = root.Value<string>("event");
			if (eventName.EqualsIgnoreCase("error"))
				throw new InvalidDataException(
					"WazirX WebSocket error: " +
						(root["data"]?["message"] ??
							root["data"]?["code"]));
			var stream = root.Value<string>("stream");
			if (stream.IsEmpty())
				return new();
			var data = root["data"];
			if (stream.EqualsIgnoreCase("!ticker@arr"))
				return new()
				{
					Stream = stream,
					Tickers = [..
						(data as JArray ?? [])
							.OfType<JObject>()
							.Select(
								WazirXRestClient.ParseTicker)
							.Where(static value =>
								value is not null)],
				};
			if (stream.EndsWithIgnoreCase("@trades"))
				return new()
				{
					Stream = stream,
					Trades = [..
						(data?["trades"] as JArray ?? [])
							.OfType<JObject>()
							.Select(value =>
								WazirXRestClient.ParseTrade(
									value,
									GetSymbol(stream)))
							.Where(static value =>
								value is not null)],
				};
			if (stream.ContainsIgnoreCase("@depth"))
				return new()
				{
					Stream = stream,
					Book = WazirXRestClient.ParseBook(
						data as JObject,
						GetSymbol(stream),
						false),
				};
			if (stream.ContainsIgnoreCase("@kline_"))
				return new()
				{
					Stream = stream,
					Candle = ParseCandle(
						data as JObject,
						GetSymbol(stream)),
				};
			if (stream.EqualsIgnoreCase(
				"outboundAccountPosition"))
				return new()
				{
					Stream = stream,
					Balances =
						WazirXRestClient.ParseBalances(data),
				};
			if (stream.EqualsIgnoreCase("orderUpdate"))
				return new()
				{
					Stream = stream,
					Order = WazirXRestClient.ParseOrder(
						data as JObject),
				};
			if (stream.EqualsIgnoreCase("ownTrade"))
				return new()
				{
					Stream = stream,
					UserTrade =
						WazirXRestClient.ParseUserTrade(
							data as JObject),
				};
			return new()
			{
				Stream = stream,
			};
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"WazirX WebSocket returned malformed JSON.",
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
				"StockSharp-WazirX-Connector/1.0");
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
			string[] publicStreams;
			string[] privateStreams;
			string authKey;
			using (_sync.EnterScope())
			{
				publicStreams = [.. _publicStreams];
				privateStreams = [.. _privateStreams];
				authKey = _authKey;
			}
			if (publicStreams.Length > 0)
				await SendAsync(
					CreateSubscription(
						publicStreams, true, null),
					cancellationToken);
			if (privateStreams.Length > 0)
				await SendAsync(
					CreateSubscription(
						privateStreams,
						true,
						authKey.ThrowIfEmpty(nameof(authKey))),
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
			var parsed = DeserializeMessage(payload);
			if (parsed.Stream.IsEmpty())
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

	private async ValueTask SendAsync(
		string payload,
		CancellationToken cancellationToken)
	{
		var client = _client ??
			throw new InvalidOperationException(
				"WazirX WebSocket is disconnected.");
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

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler.InvokeAsync(error, cancellationToken)
			: default;

	private static WazirXCandle ParseCandle(
		JObject value,
		string symbol)
	{
		if (value is null)
			return null;
		var timeFrame = WazirXRestClient
			.ReadString(value["i"])
			.FromWazirXInterval();
		if (timeFrame <= TimeSpan.Zero)
			return null;
		return new()
		{
			Symbol = (
				WazirXRestClient.ReadString(value["s"]) ??
					symbol)?.ToLowerInvariant(),
			TimeFrame = timeFrame,
			OpenTime = WazirXRestClient.ReadLong(value["t"])
				.FromWazirXTimestamp(),
			CloseTime = WazirXRestClient.ReadLong(value["T"])
				.FromWazirXTimestamp(),
			Open = WazirXRestClient.ReadDecimal(value["o"]),
			High = WazirXRestClient.ReadDecimal(value["h"]),
			Low = WazirXRestClient.ReadDecimal(value["l"]),
			Close = WazirXRestClient.ReadDecimal(value["c"]),
			Volume = WazirXRestClient.ReadDecimal(value["v"]),
		};
	}

	private static string GetSymbol(string stream)
		=> stream.ThrowIfEmpty(nameof(stream))
			.Split('@')[0]
			.ToLowerInvariant();

	private static string NormalizeStream(string stream)
	{
		stream = stream.ThrowIfEmpty(nameof(stream)).Trim();
		if (stream.Length > 128 ||
			stream.Any(char.IsWhiteSpace))
			throw new ArgumentException(
				"Invalid WazirX stream name.",
				nameof(stream));
		return stream;
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
				"WazirX WebSocket endpoint must be an " +
					"absolute WSS URI.",
				nameof(endpoint));
		return endpoint;
	}
}
