namespace StockSharp.AscendEx.Native;

sealed class AscendExWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(
		bool IsFutures,
		string Channel);

	private sealed class DepthState
	{
		public SortedDictionary<decimal, decimal> Asks { get; } = [];
		public SortedDictionary<decimal, decimal> Bids { get; } = [];
	}

	private readonly string _spotEndpoint;
	private readonly string _futuresEndpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _desiredSubscriptions = [];
	private readonly HashSet<Subscription> _serverSubscriptions = [];
	private readonly Dictionary<(bool IsFutures, string Symbol),
		DepthState> _depthStates = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private WebSocketClient _spotClient;
	private WebSocketClient _futuresClient;

	public AscendExWsClient(
		string spotEndpoint,
		string futuresEndpoint,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_spotEndpoint = spotEndpoint
			.ThrowIfEmpty(nameof(spotEndpoint)).Trim();
		_futuresEndpoint = futuresEndpoint
			.ThrowIfEmpty(nameof(futuresEndpoint)).Trim();
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "ASCENDEX_WS";

	public event Func<AscendExTicker,
		CancellationToken, ValueTask> TickerReceived;
	public event Func<AscendExOrderBook,
		CancellationToken, ValueTask> OrderBookReceived;
	public event Func<AscendExTradePush,
		CancellationToken, ValueTask> TradesReceived;
	public event Func<AscendExKlineEvent,
		CancellationToken, ValueTask> KlineReceived;
	public event Func<Exception,
		CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates,
		CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_spotClient?.Dispose();
		_spotClient = null;
		_futuresClient?.Dispose();
		_futuresClient = null;
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_spotClient is not null || _futuresClient is not null)
			throw new InvalidOperationException(
				"AscendEX WebSockets are already initialized.");

		_spotClient = CreateClient(_spotEndpoint, false);
		_futuresClient = CreateClient(_futuresEndpoint, true);

		try
		{
			await _spotClient.ConnectAsync(cancellationToken);
			await _futuresClient.ConnectAsync(cancellationToken);
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
		var spotClient = _spotClient;
		var futuresClient = _futuresClient;
		_spotClient = null;
		_futuresClient = null;

		try
		{
			if (spotClient?.IsConnected == true)
				await spotClient.DisconnectAsync(cancellationToken);
			if (futuresClient?.IsConnected == true)
				await futuresClient.DisconnectAsync(cancellationToken);
		}
		finally
		{
			spotClient?.Dispose();
			futuresClient?.Dispose();
			using (_sync.EnterScope())
			{
				_serverSubscriptions.Clear();
				_depthStates.Clear();
			}
		}
	}

	public ValueTask SubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			CreateSubscription(symbol, $"bbo:{NormalizeSymbol(symbol)}"),
			true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			CreateSubscription(symbol, $"bbo:{NormalizeSymbol(symbol)}"),
			false, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
	{
		_ = AscendExRestClient.NormalizeDepth(depth);
		return ChangeSubscriptionAsync(
			CreateSubscription(
				symbol, $"depth:{NormalizeSymbol(symbol)}"),
			true, cancellationToken);
	}

	public ValueTask UnsubscribeOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
	{
		_ = AscendExRestClient.NormalizeDepth(depth);
		return ChangeSubscriptionAsync(
			CreateSubscription(
				symbol, $"depth:{NormalizeSymbol(symbol)}"),
			false, cancellationToken);
	}

	public ValueTask SubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			CreateSubscription(
				symbol, $"trades:{NormalizeSymbol(symbol)}"),
			true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			CreateSubscription(
				symbol, $"trades:{NormalizeSymbol(symbol)}"),
			false, cancellationToken);

	public ValueTask SubscribeKlineAsync(
		string symbol,
		string resolution,
		CancellationToken cancellationToken)
	{
		_ = resolution.ToAscendExTimeFrame();
		return ChangeSubscriptionAsync(
			CreateSubscription(
				symbol,
				$"bar:{resolution}:{NormalizeSymbol(symbol)}"),
			true, cancellationToken);
	}

	public ValueTask UnsubscribeKlineAsync(
		string symbol,
		string resolution,
		CancellationToken cancellationToken)
	{
		_ = resolution.ToAscendExTimeFrame();
		return ChangeSubscriptionAsync(
			CreateSubscription(
				symbol,
				$"bar:{resolution}:{NormalizeSymbol(symbol)}"),
			false, cancellationToken);
	}

	private WebSocketClient CreateClient(
		string endpoint,
		bool isFutures)
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			endpoint,
			(state, token) => OnStateChangedAsync(
				isFutures, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(_, message, token) =>
				OnProcessAsync(isFutures, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = new JsonSerializerSettings
			{
				DateParseHandling = DateParseHandling.None,
				NullValueHandling = NullValueHandling.Ignore,
				Formatting = Formatting.None,
				Culture = CultureInfo.InvariantCulture,
			},
		};
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent",
				"StockSharp-AscendEX-Connector/1.0");
			return default;
		};
		return client;
	}

	private async ValueTask OnStateChangedAsync(
		bool isFutures,
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			Subscription[] subscriptions;
			using (_sync.EnterScope())
			{
				foreach (var key in _depthStates.Keys
					.Where(key => key.IsFutures == isFutures)
					.ToArray())
					_depthStates.Remove(key);
				subscriptions =
				[
					.. _desiredSubscriptions.Where(
						subscription =>
							subscription.IsFutures == isFutures),
				];
				foreach (var subscription in subscriptions)
					_serverSubscriptions.Add(subscription);
			}

			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(
					subscription, true, cancellationToken);
		}

		if (StateChanged is { } handler)
			await handler.InvokeAsync(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(
		Subscription subscription,
		bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var client = GetClient(subscription.IsFutures);
		var send = false;

		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				_desiredSubscriptions.Add(subscription);
				send = client.IsConnected &&
					_serverSubscriptions.Add(subscription);
			}
			else
			{
				_desiredSubscriptions.Remove(subscription);
				send = client.IsConnected &&
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
		=> SendAsync(
			subscription.IsFutures,
			new
			{
				op = isSubscribe ? "sub" : "unsub",
				ch = subscription.Channel,
			},
			cancellationToken);

	private async ValueTask SendAsync(
		bool isFutures,
		object body,
		CancellationToken cancellationToken)
	{
		var client = GetClient(isFutures);
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
		bool isFutures,
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			var envelope = DeserializeMessage(payload);
			var topic = envelope.Topic?.ToLowerInvariant();

			if (topic == "ping")
			{
				await SendAsync(
					isFutures, new { op = "pong" },
					cancellationToken);
				return;
			}

			if (envelope.Code != 0)
				throw new InvalidOperationException(
					$"AscendEX WebSocket error {envelope.Code}: " +
						envelope.Reason);

			switch (topic)
			{
				case "bbo":
					await ProcessBboAsync(
						envelope, cancellationToken);
					break;

				case "depth":
				case "depth-snapshot":
					await ProcessDepthAsync(
						isFutures, envelope, cancellationToken);
					break;

				case "trades":
					await ProcessTradesAsync(
						envelope, cancellationToken);
					break;

				case "bar":
					await ProcessBarAsync(
						envelope, cancellationToken);
					break;
			}
		}
		catch (Exception error) when (
			!cancellationToken.IsCancellationRequested)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessBboAsync(
		AscendExWsMessage envelope,
		CancellationToken cancellationToken)
	{
		var bbo = envelope.Data?.ToObject<AscendExBbo>();
		if (bbo is null || envelope.Symbol.IsEmpty())
			return;

		if (TickerReceived is { } handler)
			await handler.InvokeAsync(new AscendExTicker
			{
				Pair = envelope.Symbol,
				Bid = bbo.Bid,
				Ask = bbo.Ask,
				At = bbo.Timestamp,
			}, cancellationToken);
	}

	private async ValueTask ProcessDepthAsync(
		bool isFutures,
		AscendExWsMessage envelope,
		CancellationToken cancellationToken)
	{
		var update = envelope.Data?.ToObject<AscendExOrderBook>();
		if (update is null || envelope.Symbol.IsEmpty())
			return;

		AscendExOrderBook book;
		using (_sync.EnterScope())
		{
			var key = (isFutures, envelope.Symbol);
			if (!_depthStates.TryGetValue(key, out var state))
			{
				state = new();
				_depthStates.Add(key, state);
			}
			else if (envelope.Topic.EqualsIgnoreCase(
				"depth-snapshot"))
			{
				state.Asks.Clear();
				state.Bids.Clear();
			}

			ApplyLevels(state.Asks, update.Asks);
			ApplyLevels(state.Bids, update.Bids);

			book = new AscendExOrderBook
			{
				Pair = envelope.Symbol,
				Sequence = update.Sequence,
				Timestamp = update.Timestamp,
				Limit = 500,
				Asks =
				[
					.. state.Asks
						.Take(500)
						.Select(static level =>
							new[] { level.Key, level.Value }),
				],
				Bids =
				[
					.. state.Bids
						.Reverse()
						.Take(500)
						.Select(static level =>
							new[] { level.Key, level.Value }),
				],
			};
		}

		if (OrderBookReceived is { } handler)
			await handler.InvokeAsync(book, cancellationToken);
	}

	private async ValueTask ProcessTradesAsync(
		AscendExWsMessage envelope,
		CancellationToken cancellationToken)
	{
		var trades = envelope.Data?.ToObject<AscendExTrade[]>() ?? [];
		if (trades.Length == 0 || envelope.Symbol.IsEmpty())
			return;

		foreach (var trade in trades)
			trade.Pair = envelope.Symbol;

		if (TradesReceived is { } handler)
			await handler.InvokeAsync(new AscendExTradePush
			{
				Pair = envelope.Symbol,
				EventId = trades[0].Sequence > 0
					? trades[0].Sequence.ToString(
						CultureInfo.InvariantCulture)
					: null,
				Data = trades,
			}, cancellationToken);
	}

	private async ValueTask ProcessBarAsync(
		AscendExWsMessage envelope,
		CancellationToken cancellationToken)
	{
		var bar = envelope.Data?.ToObject<AscendExBar>();
		if (bar is null || envelope.Symbol.IsEmpty())
			return;

		bar.IsFinished =
			bar.Timestamp.FromAscendExMilliseconds() +
				bar.Interval.ToAscendExTimeFrame() <= DateTime.UtcNow;

		if (KlineReceived is { } handler)
			await handler.InvokeAsync(new AscendExKlineEvent
			{
				Market = envelope.Symbol,
				Kline = bar,
			}, cancellationToken);
	}

	internal static AscendExWsMessage DeserializeMessage(
		string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<AscendExWsMessage>(
				payload.ThrowIfEmpty(nameof(payload)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				}) ?? throw new InvalidDataException(
					"AscendEX WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"AscendEX WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler.InvokeAsync(error, cancellationToken)
			: default;

	private static void ApplyLevels(
		SortedDictionary<decimal, decimal> target,
		IEnumerable<decimal[]> updates)
	{
		foreach (var level in updates ?? [])
		{
			if (level is not { Length: >= 2 } || level[0] <= 0)
				continue;
			if (level[1] <= 0)
				target.Remove(level[0]);
			else
				target[level[0]] = level[1];
		}
	}

	private WebSocketClient GetClient(bool isFutures)
		=> (isFutures ? _futuresClient : _spotClient) ??
			throw new InvalidOperationException(
				"AscendEX WebSocket is disconnected.");

	private static Subscription CreateSubscription(
		string symbol,
		string channel)
		=> new(IsFutures(symbol), channel);

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol))
			.ToAscendExSecurityCode();

	private static bool IsFutures(string symbol)
		=> NormalizeSymbol(symbol).EndsWith(
			"-PERP", StringComparison.OrdinalIgnoreCase);
}
