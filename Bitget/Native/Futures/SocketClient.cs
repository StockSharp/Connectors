namespace StockSharp.Bitget.Native.Futures;

using StockSharp.Bitget.Native.Futures.Model;

class SocketClient : BaseLogReceiver
{
	private static class Channels
	{
		public const string Ticker = "ticker";
		public const string OrderBook = "books";
		public const string Trades = "trade";
		public const string Candles = "candle";
		public const string BookTicker = "books5";
		public const string Positions = "positions";
		public const string Orders = "orders";
		public const string UserTrades = "fill";
		public const string Account = "account";
	}

	private static class Actions
	{
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
	}

	private const string _publicWsFallback = "wss://ws.bitget.com/v2/ws/public";
	private const string _privateWsFallback = "wss://ws.bitget.com/v2/ws/private";
	private const string _demoPublicWsFallback = "wss://wspap.bitget.com/v2/ws/public";
	private const string _demoPrivateWsFallback = "wss://wspap.bitget.com/v2/ws/private";

	public override string Name => nameof(Bitget) + "_" + nameof(Futures) + nameof(SocketClient);

	public event Func<IEnumerable<Ticker>, CancellationToken, ValueTask> TickersReceived;
	public event Func<OrderBook, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<IEnumerable<Trade>, CancellationToken, ValueTask> TradesReceived;
	public event Func<JToken, CancellationToken, ValueTask> BookTickerReceived;
	public event Func<IEnumerable<Candle>, CancellationToken, ValueTask> CandlesReceived;
	public event Func<IEnumerable<Position>, CancellationToken, ValueTask> PositionsReceived;
	public event Func<IEnumerable<Order>, CancellationToken, ValueTask> OrdersReceived;
	public event Func<IEnumerable<UserTrade>, CancellationToken, ValueTask> UserTradesReceived;
	public event Func<IEnumerable<Balance>, CancellationToken, ValueTask> BalancesReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly string _productType;
	private readonly Authenticator _authenticator;
	private readonly WebSocketClient _publicClient;
	private readonly WebSocketClient _privateClient;

	public SocketClient(BitgetMessageAdapter adapter, string productType, Authenticator authenticator, WorkingTime workingTime)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_productType = productType.ThrowIfEmpty(nameof(productType));
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

		var publicEndpoint = ResolveEndpoint(adapter.FuturesPublicWsEndpoint, adapter.IsDemo, _publicWsFallback, _demoPublicWsFallback);
		var privateEndpoint = ResolveEndpoint(adapter.FuturesPrivateWsEndpoint, adapter.IsDemo, _privateWsFallback, _demoPrivateWsFallback);

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

		IEnumerable<T> getList<T>() => data.DeserializeObject<IEnumerable<T>>();

		if (channel.Equals(Channels.Ticker, StringComparison.OrdinalIgnoreCase))
		{
			if (TickersReceived is { } tickersHandler)
			{
				var tickers = getList<Ticker>().ToArray();

				foreach (var ticker in tickers)
					ticker.Symbol ??= instId;

				await tickersHandler(tickers, cancellationToken);
			}

			return;
		}

		if (channel.Equals(Channels.OrderBook, StringComparison.OrdinalIgnoreCase))
		{
			if (OrderBookReceived is { } bookHandler)
			{
				var book = data.First().DeserializeObject<OrderBook>();
				book.InstId ??= instId;
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
			if (TradesReceived is { } tradesHandler)
			{
				var trades = getList<Trade>().ToArray();

				foreach (var trade in trades)
					trade.Symbol ??= instId;

				await tradesHandler(trades, cancellationToken);
			}

			return;
		}

		if (channel.StartsWith(Channels.Candles, StringComparison.OrdinalIgnoreCase))
		{
			if (CandlesReceived is { } candlesHandler)
			{
				var candles = data
					.Select(item => ParseCandle(item, instId, channel))
					.Where(c => c is not null)
					.ToArray();

				if (candles.Length > 0)
					await candlesHandler(candles, cancellationToken);
			}

			return;
		}

		switch (channel.ToLowerInvariant())
		{
			case Channels.Positions:
				if (PositionsReceived is { } positionsHandler)
					await positionsHandler(getList<Position>(), cancellationToken);
				break;
			case Channels.Orders:
				if (OrdersReceived is { } ordersHandler)
					await ordersHandler(getList<Order>(), cancellationToken);
				break;
			case Channels.UserTrades:
				if (UserTradesReceived is { } userTradesHandler)
					await userTradesHandler(getList<UserTrade>(), cancellationToken);
				break;
			case Channels.Account:
				if (BalancesReceived is { } balancesHandler)
					await balancesHandler(getList<Balance>(), cancellationToken);
				break;
			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, msg.ToString());
				break;
		}
	}

	private async ValueTask ProcessEvent(string @event, JObject obj, bool isPrivate, CancellationToken cancellationToken)
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
	}

	public ValueTask SubscribeTicker(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { channel = Channels.Ticker, instType = _productType, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeTicker(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { channel = Channels.Ticker, instType = _productType, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask SubscribeOrderBook(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { channel = Channels.OrderBook, instType = _productType, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeOrderBook(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { channel = Channels.OrderBook, instType = _productType, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask SubscribeTrades(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { channel = Channels.Trades, instType = _productType, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeTrades(string symbol, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { channel = Channels.Trades, instType = _productType, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask SubscribeCandles(string symbol, string granularity, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { channel = $"{Channels.Candles}{granularity}", instType = _productType, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeCandles(string symbol, string granularity, CancellationToken cancellationToken)
		=> SendPublic(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { channel = $"{Channels.Candles}{granularity}", instType = _productType, instId = symbol }
			}
		}, cancellationToken);

	public ValueTask SubscribeOrders(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { channel = Channels.Orders, instType = _productType }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeOrders(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { channel = Channels.Orders, instType = _productType }
			}
		}, cancellationToken);

	public ValueTask SubscribeUserTrades(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { channel = Channels.UserTrades, instType = _productType }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeUserTrades(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { channel = Channels.UserTrades, instType = _productType }
			}
		}, cancellationToken);

	public ValueTask SubscribePositions(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { channel = Channels.Positions, instType = _productType }
			}
		}, cancellationToken);

	public ValueTask UnsubscribePositions(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { channel = Channels.Positions, instType = _productType }
			}
		}, cancellationToken);

	public ValueTask SubscribeBalances(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Subscribe,
			args = new[]
			{
				new { channel = Channels.Account, instType = _productType }
			}
		}, cancellationToken);

	public ValueTask UnsubscribeBalances(CancellationToken cancellationToken)
		=> SendPrivate(new
		{
			op = Actions.Unsubscribe,
			args = new[]
			{
				new { channel = Channels.Account, instType = _productType }
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
