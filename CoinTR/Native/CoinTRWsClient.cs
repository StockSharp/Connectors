namespace StockSharp.CoinTR.Native;

sealed class CoinTRWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(
		bool IsPrivate,
		string Channel,
		string InstrumentId,
		string Coin);

	private const string _instrumentType = "SPOT";
	private const string _defaultInstrument = "default";

	private readonly string _publicEndpoint;
	private readonly string _privateEndpoint;
	private readonly CoinTRAuthenticator _authenticator;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _desiredSubscriptions = [];
	private readonly HashSet<Subscription> _serverSubscriptions = [];
	private readonly SemaphoreSlim _publicSendSync = new(1, 1);
	private readonly SemaphoreSlim _privateSendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _publicClient;
	private WebSocketClient _privateClient;

	public CoinTRWsClient(string publicEndpoint, string privateEndpoint,
		SecureString key, SecureString secret, SecureString passphrase,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_publicEndpoint = publicEndpoint.ThrowIfEmpty(
			nameof(publicEndpoint)).Trim();
		_privateEndpoint = privateEndpoint.ThrowIfEmpty(
			nameof(privateEndpoint)).Trim();
		_authenticator = new(key, secret, passphrase);
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "CoinTR_WS";

	public event Func<CoinTRTicker, CancellationToken, ValueTask>
		TickerReceived;
	public event Func<CoinTROrderBook, CancellationToken, ValueTask>
		OrderBookReceived;
	public event Func<CoinTRTrade, CancellationToken, ValueTask>
		TradeReceived;
	public event Func<string, string, CoinTRCandle,
		CancellationToken, ValueTask> CandleReceived;
	public event Func<CoinTRBalance[], CancellationToken, ValueTask>
		BalancesReceived;
	public event Func<CoinTROrder[], CancellationToken, ValueTask>
		OrdersReceived;
	public event Func<CoinTRFill[], CancellationToken, ValueTask>
		FillsReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	protected override void DisposeManaged()
	{
		_publicClient?.Dispose();
		_privateClient?.Dispose();
		_publicSendSync.Dispose();
		_privateSendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_publicClient is not null || _privateClient is not null)
			throw new InvalidOperationException(
				"CoinTR WebSocket is already initialized.");

		_publicClient = CreateClient(_publicEndpoint, false);
		try
		{
			await _publicClient.ConnectAsync(cancellationToken);
			if (_authenticator.IsAvailable)
			{
				_privateClient = CreateClient(_privateEndpoint, true);
				await _privateClient.ConnectAsync(cancellationToken);
				await AuthenticateAsync(_privateClient, cancellationToken);
			}
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
		var publicClient = _publicClient;
		var privateClient = _privateClient;
		_publicClient = null;
		_privateClient = null;
		try
		{
			if (privateClient?.IsConnected == true)
				await privateClient.DisconnectAsync(cancellationToken);
			if (publicClient?.IsConnected == true)
				await publicClient.DisconnectAsync(cancellationToken);
		}
		finally
		{
			privateClient?.Dispose();
			publicClient?.Dispose();
			using (_sync.EnterScope())
				_serverSubscriptions.Clear();
		}
	}

	public async ValueTask SendHeartbeatAsync(
		CancellationToken cancellationToken)
	{
		var publicClient = _publicClient;
		var privateClient = _privateClient;
		if (publicClient?.IsConnected == true)
			await SendAsync(publicClient, false, "ping", cancellationToken);
		if (privateClient?.IsConnected == true)
			await SendAsync(privateClient, true, "ping", cancellationToken);
	}

	public ValueTask SubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(false, "ticker",
			NormalizeSymbol(symbol), null), true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(false, "ticker",
			NormalizeSymbol(symbol), null), false, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(false, "books15",
			NormalizeSymbol(symbol), null), true, cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(false, "books15",
			NormalizeSymbol(symbol), null), false, cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(false, "trade",
			NormalizeSymbol(symbol), null), true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(false, "trade",
			NormalizeSymbol(symbol), null), false, cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(false,
			"candle" + interval.ThrowIfEmpty(nameof(interval)),
			NormalizeSymbol(symbol), null), true, cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(false,
			"candle" + interval.ThrowIfEmpty(nameof(interval)),
			NormalizeSymbol(symbol), null), false, cancellationToken);

	public ValueTask SubscribeBalancesAsync(
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(true, "account", null,
			_defaultInstrument), true, cancellationToken);

	public ValueTask UnsubscribeBalancesAsync(
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(true, "account", null,
			_defaultInstrument), false, cancellationToken);

	public ValueTask SubscribeOrdersAsync(
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(true, "orders",
			_defaultInstrument, null), true, cancellationToken);

	public ValueTask UnsubscribeOrdersAsync(
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(true, "orders",
			_defaultInstrument, null), false, cancellationToken);

	public ValueTask SubscribeFillsAsync(
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(true, "fill",
			_defaultInstrument, null), true, cancellationToken);

	public ValueTask UnsubscribeFillsAsync(
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(true, "fill",
			_defaultInstrument, null), false, cancellationToken);

	private WebSocketClient CreateClient(string endpoint, bool isPrivate)
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			endpoint,
			(state, token) => OnStateChangedAsync(
				client, isPrivate, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) =>
				OnProcessAsync(socket, isPrivate, message, token),
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
			socket.Options.SetRequestHeader("User-Agent",
				"StockSharp-CoinTR-Connector/1.0");
			return default;
		};
		return client;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		bool isPrivate, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			if (isPrivate)
				await AuthenticateAsync(client, cancellationToken);
			Subscription[] subscriptions;
			using (_sync.EnterScope())
			{
				_serverSubscriptions.RemoveWhere(subscription =>
					subscription.IsPrivate == isPrivate);
				subscriptions = [.. _desiredSubscriptions.Where(
					subscription =>
						subscription.IsPrivate == isPrivate)];
			}
			foreach (var subscription in subscriptions)
			{
				using (_sync.EnterScope())
					if (!_desiredSubscriptions.Contains(subscription) ||
						!_serverSubscriptions.Add(subscription))
						continue;
				try
				{
					await SendSubscriptionAsync(client, subscription, true,
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
		Subscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var client = subscription.IsPrivate
			? _privateClient
			: _publicClient;
		if (subscription.IsPrivate && client is null)
			throw new InvalidOperationException(
				"CoinTR credentials are required for private streams.");
		var send = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				_desiredSubscriptions.Add(subscription);
				send = client?.IsConnected == true &&
					_serverSubscriptions.Add(subscription);
			}
			else
			{
				_desiredSubscriptions.Remove(subscription);
				send = client?.IsConnected == true &&
					_serverSubscriptions.Remove(subscription);
			}
		}
		if (!send)
			return;
		try
		{
			await SendSubscriptionAsync(client, subscription, isSubscribe,
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

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		Subscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var argument = new CoinTRWsArgument
		{
			InstrumentType = _instrumentType,
			Channel = subscription.Channel,
			InstrumentId = subscription.InstrumentId,
			Coin = subscription.Coin,
		};
		return SendAsync(client, subscription.IsPrivate, new
		{
			op = isSubscribe ? "subscribe" : "unsubscribe",
			args = new[] { argument },
		}, cancellationToken);
	}

	private async ValueTask AuthenticateAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		var timestamp = (long)(DateTime.UtcNow - DateTime.UnixEpoch)
			.TotalSeconds;
		var prehash = timestamp.ToString(CultureInfo.InvariantCulture) +
			"GET/user/verify";
		await SendAsync(client, true, new
		{
			op = "login",
			args = new[]
			{
				new
				{
					apiKey = _authenticator.Key,
					passphrase = _authenticator.Passphrase,
					timestamp = timestamp.ToString(
						CultureInfo.InvariantCulture),
					sign = CoinTRAuthenticator.CreateSignature(
						GetSecret(), prehash),
				},
			},
		}, cancellationToken);
	}

	private string GetSecret()
	{
		if (!_authenticator.IsAvailable)
			throw new InvalidOperationException(
				"CoinTR credentials are unavailable.");
		return _authenticator.Secret;
	}

	private async ValueTask SendAsync(WebSocketClient client,
		bool isPrivate, object body,
		CancellationToken cancellationToken)
	{
		var semaphore = isPrivate
			? _privateSendSync
			: _publicSendSync;
		await semaphore.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(body, cancellationToken);
		}
		finally
		{
			semaphore.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		bool isPrivate, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong"))
			return;
		if (payload.EqualsIgnoreCase("ping"))
		{
			await SendAsync(client, isPrivate, "pong", cancellationToken);
			return;
		}
		try
		{
			var root = JObject.Parse(payload);
			if (root["event"] is not null)
			{
				var serviceEvent = root.ToObject<CoinTRWsEvent>(
					JsonSerializer.Create(_jsonSettings));
				if (serviceEvent is null)
					throw new InvalidDataException(
						"CoinTR WebSocket returned an invalid event.");
				if (serviceEvent.Event.EqualsIgnoreCase("error") ||
					(!serviceEvent.Code.IsEmpty() &&
						serviceEvent.Code != "0"))
					throw new InvalidDataException(
						$"CoinTR WebSocket request failed " +
						$"({serviceEvent.Code}): {serviceEvent.Message}");
				return;
			}
			var argument = root["arg"]?.ToObject<CoinTRWsArgument>(
				JsonSerializer.Create(_jsonSettings));
			if (argument?.Channel.IsEmpty() != false ||
				root["data"] is not JArray { Count: > 0 })
				return;
			switch (argument.Channel.ToLowerInvariant())
			{
				case "ticker":
					var ticker = DeserializePush<CoinTRTicker>(payload);
					foreach (var item in ticker.Data ?? [])
					{
						item.Symbol = item.Symbol.IsEmpty(
							ticker.Argument.InstrumentId);
						if (TickerReceived is { } tickerHandler)
							await tickerHandler(item, cancellationToken);
					}
					break;

				case "books15":
					var book = DeserializePush<CoinTROrderBook>(payload);
					foreach (var item in book.Data ?? [])
					{
						item.Symbol = book.Argument.InstrumentId;
						if (OrderBookReceived is { } bookHandler)
							await bookHandler(item, cancellationToken);
					}
					break;

				case "trade":
					var trades = DeserializePush<CoinTRTrade>(payload);
					foreach (var item in trades.Data ?? [])
					{
						item.Symbol = item.Symbol.IsEmpty(
							trades.Argument.InstrumentId);
						if (TradeReceived is { } tradeHandler)
							await tradeHandler(item, cancellationToken);
					}
					break;

				case "account":
					if (BalancesReceived is { } balanceHandler)
						await balanceHandler(DeserializePush<
							CoinTRBalance>(payload).Data ?? [],
							cancellationToken);
					break;

				case "orders":
					if (OrdersReceived is { } orderHandler)
						await orderHandler(DeserializePush<
							CoinTROrder>(payload).Data ?? [],
							cancellationToken);
					break;

				case "fill":
					if (FillsReceived is { } fillHandler)
						await fillHandler(DeserializePush<
							CoinTRFill>(payload).Data ?? [],
							cancellationToken);
					break;

				default:
					if (argument.Channel.StartsWith("candle",
						StringComparison.OrdinalIgnoreCase) &&
						CandleReceived is { } candleHandler)
					{
						var candles = DeserializePush<
							CoinTRCandle>(payload);
						var interval = argument.Channel["candle".Length..];
						foreach (var candle in candles.Data ?? [])
							await candleHandler(
								argument.InstrumentId, interval, candle,
								cancellationToken);
					}
					break;
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or
			FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	internal static CoinTRWsPush<TData> DeserializePush<TData>(
		string payload)
	{
		try
		{
			var result = JsonConvert.DeserializeObject<
				CoinTRWsPush<TData>>(
				payload.ThrowIfEmpty(nameof(payload)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				});
			if (result?.Argument is null || result.Data is null)
				throw new InvalidDataException(
					"CoinTR WebSocket returned an invalid data envelope.");
			return result;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinTR WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant();
}
