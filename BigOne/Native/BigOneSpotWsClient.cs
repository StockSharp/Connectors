namespace StockSharp.BigOne.Native;

sealed class BigOneSpotWsClient : BaseLogReceiver
{
	private readonly record struct StreamKey(
		string Channel,
		string Market,
		string Period);

	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<StreamKey> _desiredStreams = [];
	private readonly Dictionary<string, SortedDictionary<decimal, decimal>>
		_bids = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, SortedDictionary<decimal, decimal>>
		_asks = new(StringComparer.OrdinalIgnoreCase);
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

	public BigOneSpotWsClient(
		string endpoint,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "BigONE_Spot_WS";

	public event Func<BigOneTicker,
		CancellationToken, ValueTask> TickerReceived;
	public event Func<BigOneOrderBook,
		CancellationToken, ValueTask> OrderBookReceived;
	public event Func<BigOneTradePush,
		CancellationToken, ValueTask> TradesReceived;
	public event Func<BigOneKlineEvent,
		CancellationToken, ValueTask> CandleReceived;
	public event Func<BigOneBalance,
		CancellationToken, ValueTask> BalanceReceived;
	public event Func<BigOneOrder,
		CancellationToken, ValueTask> OrderReceived;
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
				"BigONE spot WebSocket is already initialized.");
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
				_bids.Clear();
				_asks.Clear();
			}
		}
	}

	public ValueTask AuthenticateAsync(
		string token,
		CancellationToken cancellationToken)
		=> SendRequestAsync(
			"authenticateCustomerRequest",
			new { token = "Bearer " +
				token.ThrowIfEmpty(nameof(token)) },
			null,
			cancellationToken);

	public ValueTask SubscribeTickerAsync(
		string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("ticker", NormalizeMarket(market), null),
			true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(
		string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("ticker", NormalizeMarket(market), null),
			false, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(
		string market,
		CancellationToken cancellationToken)
		=> SubscribeOrderBookAsync(market, 0, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(
		string market,
		int depth,
		CancellationToken cancellationToken)
	{
		_ = depth;
		return ChangeSubscriptionAsync(
			new("depth", NormalizeMarket(market), null),
			true, cancellationToken);
	}

	public ValueTask UnsubscribeOrderBookAsync(
		string market,
		int depth,
		CancellationToken cancellationToken)
	{
		_ = depth;
		return ChangeSubscriptionAsync(
			new("depth", NormalizeMarket(market), null),
			false, cancellationToken);
	}

	public ValueTask SubscribeTradesAsync(
		string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("trades", NormalizeMarket(market), null),
			true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(
		string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("trades", NormalizeMarket(market), null),
			false, cancellationToken);

	public ValueTask SubscribeCandlesAsync(
		string market,
		string period,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("candles", NormalizeMarket(market),
				NormalizePeriod(period)),
			true, cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(
		string market,
		string period,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new("candles", NormalizeMarket(market),
				NormalizePeriod(period)),
			false, cancellationToken);

	public ValueTask SubscribeKlineAsync(
		string market,
		string period,
		CancellationToken cancellationToken)
		=> SubscribeCandlesAsync(
			market, period, cancellationToken);

	public ValueTask UnsubscribeKlineAsync(
		string market,
		string period,
		CancellationToken cancellationToken)
		=> UnsubscribeCandlesAsync(
			market, period, cancellationToken);

	public ValueTask SubscribeAccountsAsync(
		CancellationToken cancellationToken)
		=> SendRequestAsync(
			"subscribeViewerAccountsRequest",
			new { }, null, cancellationToken);

	public ValueTask SubscribeOrdersAsync(
		CancellationToken cancellationToken)
		=> SendRequestAsync(
			"subscribeAllViewerOrdersRequest",
			new { }, null, cancellationToken);

	internal static BigOneSpotWsMessage DeserializeMessage(
		string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<BigOneSpotWsMessage>(
				payload.ThrowIfEmpty(nameof(payload)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.DateTime,
					DateTimeZoneHandling = DateTimeZoneHandling.Utc,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				}) ?? throw new InvalidDataException(
					"BigONE spot WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"BigONE spot WebSocket returned malformed JSON.",
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
			socket.Options.AddSubProtocol("json");
			socket.Options.SetRequestHeader(
				"User-Agent",
				"StockSharp-BigONE-Connector/1.0");
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
			StreamKey[] streams;
			using (_sync.EnterScope())
				streams = [.. _desiredStreams];
			foreach (var stream in streams)
				await SendSubscriptionAsync(
					stream, true, cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(
		StreamKey stream,
		bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var client = _client ??
			throw new InvalidOperationException(
				"BigONE spot WebSocket is disconnected.");
		var changed = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
				changed = _desiredStreams.Add(stream);
			else
			{
				changed = _desiredStreams.Remove(stream);
				if (stream.Channel == "depth")
				{
					_bids.Remove(stream.Market);
					_asks.Remove(stream.Market);
				}
			}
		}
		if (changed && client.IsConnected)
			await SendSubscriptionAsync(
				stream, isSubscribe, cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(
		StreamKey stream,
		bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var prefix = isSubscribe ? "subscribe" : "unsubscribe";
		return stream.Channel switch
		{
			"ticker" => SendRequestAsync(
				$"{prefix}MarketsTickerRequest",
				new { markets = new[] { stream.Market } },
				stream, cancellationToken),
			"depth" => SendRequestAsync(
				$"{prefix}MarketDepthRequest",
				new { market = stream.Market },
				stream, cancellationToken),
			"trades" => SendRequestAsync(
				$"{prefix}MarketTradesRequest",
				isSubscribe
					? new { market = stream.Market, limit = 20 }
					: new { market = stream.Market, limit = 0 },
				stream, cancellationToken),
			"candles" => SendRequestAsync(
				$"{prefix}MarketCandlesRequest",
				isSubscribe
					? new
					{
						market = stream.Market,
						period = stream.Period,
						limit = 20,
					}
					: new
					{
						market = stream.Market,
						period = stream.Period,
						limit = 0,
					},
				stream, cancellationToken),
			_ => throw new ArgumentOutOfRangeException(
				nameof(stream), stream, LocalizedStrings.InvalidValue),
		};
	}

	private async ValueTask SendRequestAsync(
		string requestName,
		object parameters,
		StreamKey? stream,
		CancellationToken cancellationToken)
	{
		var requestId = stream is StreamKey key
			? $"{key.Channel}:{key.Market}:{key.Period}"
			: Interlocked.Increment(ref _requestId)
				.ToString(CultureInfo.InvariantCulture);
		var body = new JObject
		{
			["requestId"] = requestId,
			[requestName] = JObject.FromObject(parameters),
		};
		await SendAsync(body, cancellationToken);
	}

	private async ValueTask SendAsync(
		object body,
		CancellationToken cancellationToken)
	{
		var client = _client ??
			throw new InvalidOperationException(
				"BigONE spot WebSocket is disconnected.");
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
			var response = DeserializeMessage(payload);
			if (response.Error is not null)
				throw new InvalidDataException(
					$"BigONE spot WebSocket error " +
					$"{response.Error.Code}: {response.Error.Message}");

			foreach (var ticker in response.TickersSnapshot?.Tickers ?? [])
				await RaiseTickerAsync(ticker, cancellationToken);
			if (response.TickerUpdate?.Ticker is { } tickerUpdate)
				await RaiseTickerAsync(tickerUpdate, cancellationToken);

			if (response.DepthSnapshot?.Depth is { } depthSnapshot)
				await RaiseDepthAsync(
					depthSnapshot, true, cancellationToken);
			if (response.DepthUpdate?.Depth is { } depthUpdate)
				await RaiseDepthAsync(
					depthUpdate, false, cancellationToken);

			if (response.TradesSnapshot?.Trades is { Length: > 0 }
				trades)
				await RaiseTradesAsync(trades, cancellationToken);
			if (response.TradeUpdate?.Trade is { } trade)
				await RaiseTradesAsync(
					[trade], cancellationToken);

			foreach (var candle in
				response.CandlesSnapshot?.Candles ?? [])
				await RaiseCandleAsync(
					candle, true, cancellationToken);
			if (response.CandleUpdate?.Candle is { } candleUpdate)
				await RaiseCandleAsync(
					candleUpdate, false, cancellationToken);

			foreach (var account in
				response.AccountsSnapshot?.Accounts ?? [])
				if (BalanceReceived is { } balanceHandler)
					await balanceHandler(
						account.ToBalance(), cancellationToken);
			if (response.AccountUpdate?.Account is { } accountUpdate &&
				BalanceReceived is { } accountUpdateHandler)
				await accountUpdateHandler(
					accountUpdate.ToBalance(), cancellationToken);

			foreach (var order in
				response.OrdersSnapshot?.Orders ?? [])
				if (OrderReceived is { } orderHandler)
					await orderHandler(
						order.ToOrder(), cancellationToken);
			if (response.OrderUpdate?.Order is { } orderUpdate &&
				OrderReceived is { } orderUpdateHandler)
				await orderUpdateHandler(
					orderUpdate.ToOrder(), cancellationToken);
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or
			FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask RaiseTickerAsync(
		BigOneSpotTicker ticker,
		CancellationToken cancellationToken)
		=> TickerReceived is { } handler
			? handler(ticker.ToTicker(), cancellationToken)
			: default;

	private async ValueTask RaiseDepthAsync(
		BigOneSpotDepth depth,
		bool isSnapshot,
		CancellationToken cancellationToken)
	{
		if (depth?.Market.IsEmpty() != false)
			return;
		decimal[][] bids;
		decimal[][] asks;
		using (_sync.EnterScope())
		{
			if (isSnapshot ||
				!_bids.TryGetValue(depth.Market, out var bidBook))
			{
				bidBook = [];
				_bids[depth.Market] = bidBook;
			}
			if (isSnapshot ||
				!_asks.TryGetValue(depth.Market, out var askBook))
			{
				askBook = [];
				_asks[depth.Market] = askBook;
			}
			if (isSnapshot)
			{
				bidBook.Clear();
				askBook.Clear();
			}
			ApplyLevels(bidBook, depth.Bids);
			ApplyLevels(askBook, depth.Asks);
			bids = [.. bidBook.Select(static pair =>
				new[] { pair.Key, pair.Value })];
			asks = [.. askBook.Select(static pair =>
				new[] { pair.Key, pair.Value })];
		}
		if (OrderBookReceived is { } handler)
			await handler(new()
			{
				Pair = depth.Market,
				Timestamp = DateTime.UtcNow
					.ToBigOneMilliseconds(),
				Bids = bids,
				Asks = asks,
			}, cancellationToken);
	}

	private ValueTask RaiseTradesAsync(
		BigOneSpotTrade[] trades,
		CancellationToken cancellationToken)
	{
		var market = trades?.FirstOrDefault(
			static trade => !trade.Market.IsEmpty())?.Market;
		if (market.IsEmpty())
			return default;
		var converted = trades.Select(
			trade => trade.ToTrade(market)).ToArray();
		return TradesReceived is { } handler
			? handler(new()
			{
				Pair = market,
				EventId = DateTime.UtcNow
					.ToBigOneMilliseconds()
					.ToString(CultureInfo.InvariantCulture),
				Data = converted,
			}, cancellationToken)
			: default;
	}

	private ValueTask RaiseCandleAsync(
		BigOneSpotCandle candle,
		bool isSnapshot,
		CancellationToken cancellationToken)
	{
		if (CandleReceived is not { } handler)
			return default;
		var converted = candle.ToCandle(isSnapshot);
		return handler(new()
		{
			Market = candle.Market,
			Kline = new()
			{
				StartTime = converted.Timestamp,
				EndTime = converted.Timestamp,
				Resolution = candle.Period,
				Open = converted.Open,
				High = converted.High,
				Low = converted.Low,
				Close = converted.Close,
				Volume = converted.Volume,
				IsFinished = converted.IsFinished,
			},
		}, cancellationToken);
	}

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	private static void ApplyLevels(
		IDictionary<decimal, decimal> book,
		IEnumerable<BigOnePriceLevel> levels)
	{
		foreach (var level in levels ?? [])
		{
			if (level.Price <= 0)
				continue;
			if (level.Amount <= 0)
				book.Remove(level.Price);
			else
				book[level.Price] = level.Amount;
		}
	}

	private static string NormalizeMarket(string market)
	{
		market = market.ThrowIfEmpty(nameof(market)).Trim()
			.ToUpperInvariant();
		return market.Contains('-')
			? market
			: market.ToBigOneSpotSymbol();
	}

	private static string NormalizePeriod(string period)
	{
		period = period.ThrowIfEmpty(nameof(period)).Trim()
			.ToUpperInvariant();
		if (!BigOneExtensions.TimeFrames.Any(
			timeFrame => timeFrame.ToBigOneSpotStreamPeriod()
				.EqualsIgnoreCase(period)))
			throw new ArgumentOutOfRangeException(
				nameof(period), period,
				"Unsupported BigONE spot candle period.");
		return period;
	}
}
