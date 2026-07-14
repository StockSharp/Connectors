namespace StockSharp.Bitget.Native.Spot;

using StockSharp.Bitget.Native.Spot.Model;

class SocketClient : BaseLogReceiver
{
	private static class Channels
	{
		public const string Ticker = "ticker";
		public const string OrderBook = "books";
		public const string Trades = "trade";
		public const string Candles = "candle";
		public const string BookTicker = "books5";
		public const string Account = "account";
		public const string Orders = "orders";
		public const string UserTrades = "fill";
	}

	private static class Actions
	{
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
	}

	private const string _spotInstType = "SPOT";
	private const string _publicWsFallback = "wss://ws.bitget.com/v2/ws/public";
	private const string _privateWsFallback = "wss://ws.bitget.com/v2/ws/private";
	private const string _demoPublicWsFallback = "wss://wspap.bitget.com/v2/ws/public";
	private const string _demoPrivateWsFallback = "wss://wspap.bitget.com/v2/ws/private";

	public override string Name => nameof(Bitget) + "_" + nameof(Spot) + nameof(SocketClient);

	public event Func<Ticker, CancellationToken, ValueTask> TickerReceived;
	public event Func<OrderBook, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<Trade, CancellationToken, ValueTask> TradeReceived;
	public event Func<Candle, CancellationToken, ValueTask> CandleReceived;
	public event Func<IEnumerable<Balance>, CancellationToken, ValueTask> BalancesReceived;
	public event Func<IEnumerable<Order>, CancellationToken, ValueTask> OrdersReceived;
	public event Func<IEnumerable<UserTrade>, CancellationToken, ValueTask> UserTradesReceived;
	public event Func<JToken, CancellationToken, ValueTask> BookTickerReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _publicClient;
	private readonly WebSocketClient _privateClient;
	private readonly Authenticator _authenticator;

	public SocketClient(BitgetMessageAdapter adapter, Authenticator authenticator, WorkingTime workingTime)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

		var publicEndpoint = ResolveEndpoint(adapter.SpotPublicWsEndpoint, adapter.IsDemo, _publicWsFallback, _demoPublicWsFallback);
		var privateEndpoint = ResolveEndpoint(adapter.SpotPrivateWsEndpoint, adapter.IsDemo, _privateWsFallback, _demoPrivateWsFallback);

		_publicClient = CreateClient(
			publicEndpoint,
			isPrivate: false,
			adapter,
			workingTime,
			OnPublicProcess);

		if (_authenticator.CanSign)
		{
			_privateClient = CreateClient(
				privateEndpoint,
				isPrivate: true,
				adapter,
				workingTime,
				OnPrivateProcess);
		}
	}

	protected override void DisposeManaged()
	{
		_publicClient.Dispose();
		_privateClient?.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask Connect(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		await _publicClient.ConnectAsync(cancellationToken);

		if (_privateClient is not null)
		{
			await _privateClient.ConnectAsync(cancellationToken);
			await Authenticate(cancellationToken);
		}
	}

	private async ValueTask Authenticate(CancellationToken cancellationToken)
	{
		var timestamp = (long)DateTime.UtcNow.ToUnix(false);
		var message = $"{timestamp}GET/user/verify";
		var signature = _authenticator.Sign(message);

		await SendPrivate(new
		{
			op = "login",
			args = new[]
			{
				new
				{
					apiKey = _authenticator.Key.UnSecure(),
					passphrase = _authenticator.Passphrase.UnSecure(),
					timestamp = timestamp.ToString(),
					sign = signature
				}
			}
		}, cancellationToken);
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);

		_publicClient.Disconnect();
		_privateClient?.Disconnect();
	}

	private async ValueTask OnPublicProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		await OnProcess(msg, isPrivate: false, cancellationToken);
	}

	private async ValueTask OnPrivateProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		await OnProcess(msg, isPrivate: true, cancellationToken);
	}

	private async ValueTask OnProcess(WebSocketMessage msg, bool isPrivate, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();
		var @event = (string)obj["event"];

		if (!@event.IsEmpty())
		{
			await ProcessEvent(@event, obj, isPrivate, cancellationToken);
			return;
		}

		var arg = obj["arg"];
		var data = (JArray)obj["data"];

		if (data == null || data.Count == 0)
			return;

		var channel = (string)arg?["channel"];
		var instId = (string)arg?["instId"];

		if (channel.IsEmpty())
			return;

		T get<T>() => data.First().DeserializeObject<T>();
		IEnumerable<T> getList<T>() => data.DeserializeObject<IEnumerable<T>>();

		if (channel.Equals(Channels.Ticker, StringComparison.OrdinalIgnoreCase))
		{
			if (TickerReceived is { } tickerHandler)
			{
				var ticker = get<Ticker>();
				ticker.Symbol ??= instId;
				await tickerHandler(ticker, cancellationToken);
			}

			return;
		}

		if (channel.Equals(Channels.OrderBook, StringComparison.OrdinalIgnoreCase))
		{
			if (OrderBookReceived is { } bookHandler)
			{
				var book = get<OrderBook>();
				book.Symbol ??= instId;
				await bookHandler(book, cancellationToken);
			}

			return;
		}

		if (channel.Equals(Channels.BookTicker, StringComparison.OrdinalIgnoreCase))
		{
			if (BookTickerReceived is { } bookTickerHandler)
			{
				var token = data.First();

				if (token is JObject tokenObj && tokenObj["instId"] == null && !instId.IsEmpty())
					tokenObj["instId"] = instId;

				await bookTickerHandler(token, cancellationToken);
			}

			return;
		}

		if (channel.Equals(Channels.Trades, StringComparison.OrdinalIgnoreCase))
		{
			if (TradeReceived is { } tradeHandler)
			{
				foreach (var item in getList<Trade>())
				{
					item.Symbol ??= instId;
					await tradeHandler(item, cancellationToken);
				}
			}

			return;
		}

		if (channel.StartsWith(Channels.Candles, StringComparison.OrdinalIgnoreCase))
		{
			if (CandleReceived is { } candleHandler)
			{
				foreach (var item in data)
				{
					var candle = ParseCandle(item, instId, channel);

					if (candle is not null)
						await candleHandler(candle, cancellationToken);
				}
			}

			return;
		}

		switch (channel.ToLowerInvariant())
		{
			case Channels.Account:
				if (BalancesReceived is { } balancesHandler)
					await balancesHandler(getList<Balance>(), cancellationToken);
				break;
			case Channels.Orders:
				if (OrdersReceived is { } ordersHandler)
					await ordersHandler(getList<Order>(), cancellationToken);
				break;
			case Channels.UserTrades:
				if (UserTradesReceived is { } userTradesHandler)
					await userTradesHandler(getList<UserTrade>(), cancellationToken);
				break;
			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, msg.ToString());
				break;
		}
	}

	private ValueTask ProcessEvent(string @event, JObject obj, bool isPrivate, CancellationToken cancellationToken)
	{
		var code = ((string)obj["code"]) ?? "0";
		var success = code == "0";

		switch (@event.ToLowerInvariant())
		{
			case "error":
				LogError($"{(isPrivate ? "private" : "public")} ws error: {obj}");
				break;
			case "login":
				if (!success)
					LogError("Login failed: " + obj);
				break;
			case "subscribe":
			case "unsubscribe":
				if (!success)
					LogError($"{@event} failed: {obj}");
				break;
			default:
				this.AddVerboseLog("Service event {0}: {1}", @event, obj.ToString());
				break;
		}

		return default;
	}

	public ValueTask SubscribeTicker(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.Ticker, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeTicker(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.Ticker, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask SubscribeOrderBook(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.OrderBook, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeOrderBook(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.OrderBook, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask SubscribeTrades(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.Trades, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeTrades(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.Trades, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask SubscribeBookTicker(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.BookTicker, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeBookTicker(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.BookTicker, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask SubscribeCandles(string symbol, string granularity, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = $"{Channels.Candles}{granularity}", instId = symbol }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeCandles(string symbol, string granularity, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = $"{Channels.Candles}{granularity}", instId = symbol }
			}
		}, cancellationToken);

	public ValueTask SubscribeOrders(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.Orders }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeOrders(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.Orders }
			}
		}, cancellationToken);

	public ValueTask SubscribeUserTrades(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.UserTrades }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeUserTrades(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.UserTrades }
			}
		}, cancellationToken);

	public ValueTask SubscribeBalance(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.Account }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeBalance(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { instType = _spotInstType, channel = Channels.Account }
			}
		}, cancellationToken);

	private static Candle ParseCandle(JToken token, string symbol, string channel)
	{
		if (token is JArray values)
		{
			if (values.Count < 6)
				return null;

			var ts = values[0].Value<long?>();
			var granularity = channel.Length > Channels.Candles.Length ? channel[Channels.Candles.Length..] : string.Empty;

			return new Candle
			{
				Symbol = symbol,
				Granularity = granularity,
				Open = values[1].Value<double?>(),
				High = values[2].Value<double?>(),
				Low = values[3].Value<double?>(),
				Close = values[4].Value<double?>(),
				BaseVolume = values[5].Value<double?>(),
				UsdtVolume = values.Count > 6 ? values[6].Value<double?>() : null,
				Timestamp = ts?.FromUnix(false) ?? default,
			};
		}

		var candle = token.DeserializeObject<Candle>();
		candle.Symbol ??= symbol;
		candle.Granularity ??= channel.Length > Channels.Candles.Length ? channel[Channels.Candles.Length..] : string.Empty;
		return candle;
	}

	private WebSocketClient CreateClient(string url, bool isPrivate, BitgetMessageAdapter adapter, WorkingTime workingTime, Func<WebSocketMessage, CancellationToken, ValueTask> onProcess)
	{
		return new WebSocketClient(
			url,
			(state, token) =>
			{
				if (StateChanged is { } handler)
					return handler(state, token);
				return default;
			},
			(error, token) =>
			{
				this.AddErrorLog("{0} ws error: {1}", isPrivate ? "private" : "public", error);

				if (Error is { } handler)
					return handler(error, token);

				return default;
			},
			onProcess,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = adapter.ReConnectionSettings.ReAttemptCount,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
	}

	private static string NormalizeEndpoint(string endpoint, string fallback)
	{
		if (endpoint.IsEmpty())
			return fallback;

		endpoint = endpoint.Trim();

		if (!endpoint.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
			!endpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
			endpoint = $"wss://{endpoint}";

		return endpoint;
	}

	private static string ResolveEndpoint(string endpoint, bool isDemo, string prodFallback, string demoFallback)
	{
		var fallback = isDemo ? demoFallback : prodFallback;
		var normalized = NormalizeEndpoint(endpoint, fallback);

		if (isDemo && normalized.Equals(prodFallback, StringComparison.OrdinalIgnoreCase))
			return demoFallback;

		return normalized;
	}

	private ValueTask SendPublic(object body, CancellationToken cancellationToken)
		=> _publicClient.SendAsync(body, cancellationToken);

	private ValueTask SendPrivate(object body, CancellationToken cancellationToken)
	{
		if (_privateClient is null)
			throw new InvalidOperationException("Private socket is unavailable for unauthenticated connection.");

		return _privateClient.SendAsync(body, cancellationToken);
	}
}
