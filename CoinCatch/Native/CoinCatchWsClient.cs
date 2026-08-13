namespace StockSharp.CoinCatch.Native;

sealed class CoinCatchWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(
		string Channel, string InstrumentId);

	private readonly string _endpoint;
	private readonly CoinCatchProductTypes _productType;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _desiredSubscriptions = [];
	private readonly HashSet<Subscription> _serverSubscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public CoinCatchWsClient(string endpoint,
		CoinCatchProductTypes productType, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_productType = productType;
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "CoinCatch_WS";

	public event Func<CoinCatchTicker, CancellationToken, ValueTask>
		TickerReceived;
	public event Func<CoinCatchOrderBook, CancellationToken, ValueTask>
		OrderBookReceived;
	public event Func<CoinCatchTrade, CancellationToken, ValueTask>
		TradeReceived;
	public event Func<string, string, CoinCatchCandle,
		CancellationToken, ValueTask> CandleReceived;
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
				"CoinCatch WebSocket is already initialized.");
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
			? SendAsync("ping", cancellationToken)
			: default;

	public ValueTask SubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("ticker", NormalizeSymbol(symbol)),
			true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("ticker", NormalizeSymbol(symbol)),
			false, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(depth <= 5 ? "books5" : "books15",
				NormalizeSymbol(symbol)),
			true, cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(depth <= 5 ? "books5" : "books15",
				NormalizeSymbol(symbol)),
			false, cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("trade", NormalizeSymbol(symbol)),
			true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("trade", NormalizeSymbol(symbol)),
			false, cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, string channel,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(channel.ThrowIfEmpty(nameof(channel)),
				NormalizeSymbol(symbol)),
			true, cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, string channel,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(channel.ThrowIfEmpty(nameof(channel)),
				NormalizeSymbol(symbol)),
			false, cancellationToken);

	internal static string CreateSubscriptionJson(bool isSubscribe,
		CoinCatchProductTypes productType, string channel,
		string symbol)
		=> JsonConvert.SerializeObject(new
		{
			op = isSubscribe ? "subscribe" : "unsubscribe",
			args = new[]
			{
				new CoinCatchWsArgument
				{
					InstrumentType =
						productType.ToWebSocketInstrumentType(),
					Channel = channel.ThrowIfEmpty(nameof(channel)),
					InstrumentId = NormalizeSymbol(symbol),
				},
			},
		}, Formatting.None);

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
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
			SendSettings = _jsonSettings,
		};
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent", "StockSharp-CoinCatch-Connector/1.0");
			return default;
		};
		return client;
	}

	private async ValueTask OnStateChangedAsync(
		ConnectionStates state, CancellationToken cancellationToken)
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
		Subscription subscription, bool isSubscribe,
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
		Subscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(new
		{
			op = isSubscribe ? "subscribe" : "unsubscribe",
			args = new[]
			{
				new CoinCatchWsArgument
				{
					InstrumentType =
						_productType.ToWebSocketInstrumentType(),
					Channel = subscription.Channel,
					InstrumentId = subscription.InstrumentId,
				},
			},
		}, cancellationToken);

	private async ValueTask SendAsync(object body,
		CancellationToken cancellationToken)
	{
		var client = _client ?? throw new InvalidOperationException(
			"CoinCatch WebSocket is disconnected.");
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

	private async ValueTask OnProcessAsync(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong"))
			return;
		if (payload.EqualsIgnoreCase("ping"))
		{
			await SendAsync("pong", cancellationToken);
			return;
		}
		try
		{
			var root = JObject.Parse(payload);
			if (root["event"] is not null)
			{
				var serviceEvent = root.ToObject<CoinCatchWsEvent>(
					JsonSerializer.Create(_jsonSettings));
				if (serviceEvent is null)
					throw new InvalidDataException(
						"CoinCatch WebSocket returned an invalid event.");
				if (serviceEvent.Event.EqualsIgnoreCase("error") ||
					(!serviceEvent.Code.IsEmpty() &&
						serviceEvent.Code != "0"))
					throw new InvalidDataException(
						$"CoinCatch WebSocket request failed " +
							$"({serviceEvent.Code}): " +
							serviceEvent.Message);
				return;
			}
			var argument = root["arg"]?.ToObject<CoinCatchWsArgument>(
				JsonSerializer.Create(_jsonSettings));
			if (argument?.Channel.IsEmpty() != false ||
				root["data"] is not JArray { Count: > 0 })
				return;
			switch (argument.Channel.ToLowerInvariant())
			{
				case "ticker":
					var ticker =
						DeserializePush<CoinCatchTicker>(payload);
					foreach (var item in ticker.Data ?? [])
					{
						item.Symbol = item.Symbol.IsEmpty()
							? ticker.Argument.InstrumentId
							: item.Symbol;
						if (TickerReceived is { } tickerHandler)
							await tickerHandler.InvokeAsync(
								item, cancellationToken);
					}
					break;

				case "books5":
				case "books15":
					var book =
						DeserializePush<CoinCatchOrderBook>(payload);
					foreach (var item in book.Data ?? [])
					{
						item.Symbol = book.Argument.InstrumentId;
						if (OrderBookReceived is { } bookHandler)
							await bookHandler.InvokeAsync(
								item, cancellationToken);
					}
					break;

				case "trade":
				case "tradenew":
					var trades =
						DeserializePush<CoinCatchTrade>(payload);
					foreach (var item in trades.Data ?? [])
					{
						item.Symbol = item.Symbol.IsEmpty()
							? trades.Argument.InstrumentId
							: item.Symbol;
						if (TradeReceived is { } tradeHandler)
							await tradeHandler.InvokeAsync(
								item, cancellationToken);
					}
					break;

				default:
					if (argument.Channel.StartsWith(
						"candle",
						StringComparison.OrdinalIgnoreCase) &&
						CandleReceived is { } candleHandler)
					{
						var candles =
							DeserializePush<CoinCatchCandle>(payload);
						foreach (var candle in candles.Data ?? [])
							await candleHandler.InvokeAsync(
								argument.InstrumentId,
								argument.Channel,
								candle,
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

	internal static CoinCatchWsPush<TData> DeserializePush<TData>(
		string payload)
	{
		try
		{
			var result = JsonConvert.DeserializeObject<
				CoinCatchWsPush<TData>>(
					payload.ThrowIfEmpty(nameof(payload)),
					new JsonSerializerSettings
					{
						DateParseHandling = DateParseHandling.None,
						NullValueHandling =
							NullValueHandling.Ignore,
						Culture = CultureInfo.InvariantCulture,
					});
			if (result?.Argument is null || result.Data is null)
				throw new InvalidDataException(
					"CoinCatch WebSocket returned an invalid " +
						"data envelope.");
			return result;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinCatch WebSocket returned malformed JSON.",
				error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler.InvokeAsync(error, cancellationToken)
			: default;

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol))
			.ToCoinCatchWebSocketSymbol();
}
