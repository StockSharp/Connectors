namespace StockSharp.TastyTrade.Native;

sealed class TastyTradeMarketStreamer : BaseLogReceiver
{
	private const int _feedChannel = 1;
	private readonly WebSocketClient _client;
	private readonly string _token;
	private readonly Lock _sync = new();
	private readonly Dictionary<(DxEventTypes type, string symbol), SubscriptionState> _subscriptions = [];
	private CancellationTokenSource _keepAliveCts;
	private Task _keepAliveTask;
	private volatile bool _isReady;

	public TastyTradeMarketStreamer(string address, string token, WorkingTime workingTime, int reconnectAttempts)
	{
		_token = token.ThrowIfEmpty(nameof(token));
		_client = new WebSocketClient(
			address,
			OnStateChanged,
			(error, ct) => Error is { } errorHandler ? errorHandler(error, ct) : default,
			Process,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			SendSettings = new() { NullValueHandling = NullValueHandling.Ignore },
		};
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(TastyTrade) + "_" + nameof(TastyTradeMarketStreamer);

	public event Func<DxEvent, CancellationToken, ValueTask> DataReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await _client.ConnectAsync(cancellationToken);
		_keepAliveCts = new CancellationTokenSource();
		_keepAliveTask = KeepAlive(_keepAliveCts.Token);
	}

	public void Disconnect()
	{
		_isReady = false;
		_keepAliveCts?.Cancel();
		_keepAliveCts?.Dispose();
		_keepAliveCts = null;
		_keepAliveTask = null;
		_client.Disconnect();
	}

	public async ValueTask Subscribe(DxEventTypes type, string symbol, DateTime? from, CancellationToken cancellationToken)
	{
		var subscription = new DxSubscription
		{
			Type = type,
			Symbol = symbol,
			FromTime = type == DxEventTypes.Candle && from is DateTime fromTime ? (long)fromTime.ToUniversalTime().ToUnix(false) : null,
		};
		var isNew = false;
		using (_sync.EnterScope())
		{
			if (_subscriptions.TryGetValue((type, symbol), out var state))
				state.Count++;
			else
			{
				_subscriptions.Add((type, symbol), new(subscription));
				isNew = true;
			}
		}
		if (isNew && _isReady)
			await SendSubscription([subscription], null, cancellationToken);
	}

	public async ValueTask Unsubscribe(DxEventTypes type, string symbol, CancellationToken cancellationToken)
	{
		DxSubscription subscription = null;
		using (_sync.EnterScope())
		{
			if (_subscriptions.TryGetValue((type, symbol), out var state) && --state.Count == 0)
			{
				subscription = state.Subscription;
				_subscriptions.Remove((type, symbol));
			}
		}
		if (subscription is not null && _isReady)
			await SendSubscription(null, [subscription], cancellationToken);
	}

	private ValueTask OnStateChanged(ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state != ConnectionStates.Connected)
			_isReady = false;
		return default;
	}

	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		_isReady = false;
		return _client.SendAsync(new DxSetupRequest
		{
			Type = DxMessageTypes.SETUP,
			Channel = 0,
			Version = "0.1-StockSharp/1.0",
			KeepAliveTimeout = 60,
			AcceptKeepAliveTimeout = 60,
		}, cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var raw = message.AsString();
		if (raw.IsEmpty())
			return;
		var header = JsonConvert.DeserializeObject<DxMessageHeader>(raw);
		switch (header.Type)
		{
			case DxMessageTypes.SETUP:
				await _client.SendAsync(new DxAuthRequest { Type = DxMessageTypes.AUTH, Channel = 0, Token = _token }, cancellationToken);
				break;
			case DxMessageTypes.AUTH_STATE:
				if (!header.State.EqualsIgnoreCase("AUTHORIZED"))
					await RaiseError(header.Message.IsEmpty("DXLink authorization failed."), cancellationToken);
				else
					await _client.SendAsync(new DxChannelRequest
					{
						Type = DxMessageTypes.CHANNEL_REQUEST,
						Channel = _feedChannel,
						Service = "FEED",
						Parameters = new() { Contract = "AUTO" },
					}, cancellationToken);
				break;
			case DxMessageTypes.CHANNEL_OPENED:
				await OpenFeed(cancellationToken);
				break;
			case DxMessageTypes.FEED_DATA:
				foreach (var data in JsonConvert.DeserializeObject<DxFeedData>(raw)?.Data ?? [])
					if (DataReceived is { } handler)
						await handler(data, cancellationToken);
				break;
			case DxMessageTypes.ERROR:
				await RaiseError(header.Message.IsEmpty(header.Error), cancellationToken);
				break;
		}
	}

	private async ValueTask OpenFeed(CancellationToken cancellationToken)
	{
		await _client.SendAsync(new DxFeedSetup
		{
			Type = DxMessageTypes.FEED_SETUP,
			Channel = _feedChannel,
			AcceptAggregationPeriod = 0,
			AcceptDataFormat = "FULL",
			AcceptEventFields = new()
			{
				Quote = ["eventType", "eventSymbol", "bidTime", "askTime", "bidPrice", "askPrice", "bidSize", "askSize"],
				Trade = ["eventType", "eventSymbol", "time", "price", "size", "dayVolume"],
				Summary = ["eventType", "eventSymbol", "dayId", "openPrice", "highPrice", "lowPrice", "prevDayClosePrice"],
				Candle = ["eventType", "eventSymbol", "eventFlags", "time", "open", "high", "low", "close", "volume", "openInterest"],
			},
		}, cancellationToken);
		_isReady = true;
		DxSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = _subscriptions.Values.Select(s => s.Subscription).ToArray();
		if (subscriptions.Length > 0)
			await SendSubscription(subscriptions, null, cancellationToken);
	}

	private ValueTask SendSubscription(DxSubscription[] add, DxSubscription[] remove, CancellationToken cancellationToken)
		=> _client.SendAsync(new DxFeedSubscription
		{
			Type = DxMessageTypes.FEED_SUBSCRIPTION,
			Channel = _feedChannel,
			Add = add,
			Remove = remove,
		}, cancellationToken);

	private async Task KeepAlive(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
				await _client.SendAsync(new DxMessageHeader { Type = DxMessageTypes.KEEPALIVE, Channel = 0 }, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				if (Error is { } handler)
					await handler(ex, CancellationToken.None);
			}
		}
	}

	private ValueTask RaiseError(string message, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(new InvalidOperationException(message), cancellationToken) : default;

	protected override void DisposeManaged()
	{
		Disconnect();
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	private sealed class SubscriptionState
	{
		public SubscriptionState(DxSubscription subscription)
		{
			Subscription = subscription;
			Count = 1;
		}

		public DxSubscription Subscription { get; }

		public int Count { get; set; }
	}
}
