namespace StockSharp.Weex.Native;

sealed class WeexWsClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly WeexSections _section;
	private readonly bool _isPrivate;
	private readonly Func<WeexWebSocketAuthentication> _authenticationProvider;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _restoreSync = new(1, 1);
	private readonly HashSet<string> _channels = new(StringComparer.OrdinalIgnoreCase);
	private long _nextRequestId;
	private bool _isReady;

	public WeexWsClient(string endpoint, WeexSections section, bool isPrivate,
		Func<WeexWebSocketAuthentication> authenticationProvider, WorkingTime workingTime)
	{
		_section = section;
		_isPrivate = isPrivate;
		_authenticationProvider = authenticationProvider;
		if (isPrivate && authenticationProvider is null)
			throw new ArgumentNullException(nameof(authenticationProvider));

		_client = new WebSocketClient(
			NormalizeEndpoint(endpoint),
			OnStateChangedAsync,
			(error, token) => RaiseErrorAsync(error, token),
			OnProcessAsync,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = 5,
			WorkingTime = workingTime,
			DisableAutoResend = true,
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
		_client.Init += OnInit;

		if (isPrivate)
		{
			_channels.Add("account");
			_channels.Add("orders");
			_channels.Add("fill");
			if (section == WeexSections.Futures)
				_channels.Add("positions");
		}
	}

	public override string Name => nameof(Weex) + $"_{_section}_{(_isPrivate ? "UserWs" : "MarketWs")}";

	public event Func<WeexWsEnvelope<WeexWsSpotTicker>, CancellationToken, ValueTask> SpotTickerReceived;
	public event Func<WeexWsEnvelope<WeexWsFuturesTicker[]>, CancellationToken, ValueTask> FuturesTickerReceived;
	public event Func<WeexSections, WeexWsDepth, CancellationToken, ValueTask> DepthReceived;
	public event Func<WeexSections, WeexWsEnvelope<WeexWsTrade[]>, CancellationToken, ValueTask> TradesReceived;
	public event Func<WeexSections, WeexWsEnvelope<WeexWsCandle[]>, CancellationToken, ValueTask> CandleReceived;
	public event Func<WeexSections, WeexWsAccountEntry[], CancellationToken, ValueTask> AccountReceived;
	public event Func<WeexSections, WeexWsOrder[], CancellationToken, ValueTask> OrderReceived;
	public event Func<WeexSections, WeexWsFill[], CancellationToken, ValueTask> FillReceived;
	public event Func<WeexWsPosition[], CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.Init -= OnInit;
		_restoreSync.Dispose();
		_client.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await _client.ConnectAsync(cancellationToken);
		await RestoreSessionAsync(cancellationToken);
		_isReady = true;
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		_isReady = false;
		await _client.DisconnectAsync(cancellationToken);
	}

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client.SendAsync(new WeexWsPong
		{
			Id = Interlocked.Increment(ref _nextRequestId),
		}, cancellationToken);

	public ValueTask SubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> SubscribeAsync(CreateChannel(symbol, "ticker"), cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> UnsubscribeAsync(CreateChannel(symbol, "ticker"), cancellationToken);

	public ValueTask SubscribeDepthAsync(string symbol, int depth, CancellationToken cancellationToken)
		=> SubscribeAsync(CreateChannel(symbol, depth > 15 ? "depth200" : "depth15"), cancellationToken);

	public ValueTask UnsubscribeDepthAsync(string symbol, int depth, CancellationToken cancellationToken)
		=> UnsubscribeAsync(CreateChannel(symbol, depth > 15 ? "depth200" : "depth15"), cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> SubscribeAsync(CreateChannel(symbol, "trade"), cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> UnsubscribeAsync(CreateChannel(symbol, "trade"), cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> SubscribeAsync(CreateChannel(symbol, $"kline_{timeFrame.ToNative()}_LAST_PRICE"), cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(CreateChannel(symbol, $"kline_{timeFrame.ToNative()}_LAST_PRICE"), cancellationToken);

	public async ValueTask ResubscribeDepthAsync(string symbol, int depth,
		CancellationToken cancellationToken)
	{
		var channel = CreateChannel(symbol, depth > 15 ? "depth200" : "depth15");
		await SendSubscriptionAsync(channel, false, cancellationToken);
		await SendSubscriptionAsync(channel, true, cancellationToken);
	}

	private ValueTask SubscribeAsync(string channel, CancellationToken cancellationToken)
	{
		bool isAdded;
		using (_sync.EnterScope())
		{
			if (!_channels.Contains(channel) && _channels.Count >= 100)
				throw new InvalidOperationException("WEEX allows at most 100 channels per WebSocket connection.");
			isAdded = _channels.Add(channel);
		}
		return isAdded ? SendSubscriptionAsync(channel, true, cancellationToken) : default;
	}

	private ValueTask UnsubscribeAsync(string channel, CancellationToken cancellationToken)
	{
		bool isRemoved;
		using (_sync.EnterScope())
			isRemoved = _channels.Remove(channel);
		return isRemoved ? SendSubscriptionAsync(channel, false, cancellationToken) : default;
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Connected && _isReady)
		{
			try
			{
				await RestoreSessionAsync(cancellationToken);
			}
			catch (Exception error)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
		}

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask RestoreSessionAsync(CancellationToken cancellationToken)
	{
		await _restoreSync.WaitAsync(cancellationToken);
		try
		{
			await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
			string[] channels;
			using (_sync.EnterScope())
				channels = [.. _channels];

			foreach (var channel in channels)
				await SendSubscriptionAsync(channel, true, cancellationToken);
		}
		finally
		{
			_restoreSync.Release();
		}
	}

	private ValueTask SendSubscriptionAsync(string channel, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _nextRequestId);
		return _client.SendAsync(new WeexWsCommand
		{
			Method = isSubscribe ? "SUBSCRIBE" : "UNSUBSCRIBE",
			Parameters = [channel],
			Id = id,
		}, cancellationToken, isSubscribe ? id : -id);
	}

	private async ValueTask OnProcessAsync(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			var header = Deserialize<WeexWsHeader>(payload);
			if (header.PublicControlEvent.EqualsIgnoreCase("ping") ||
				header.PrivateControlEvent.EqualsIgnoreCase("ping"))
			{
				await PingAsync(cancellationToken);
				return;
			}

			if (header.IsSuccess == false)
				throw new InvalidOperationException($"WEEX WebSocket request {header.Id} failed: {header.Message}");

			switch (header.Event?.ToLowerInvariant())
			{
				case "24hrticker":
					if (SpotTickerReceived is { } spotTickerHandler)
						await spotTickerHandler(Deserialize<WeexWsEnvelope<WeexWsSpotTicker>>(payload), cancellationToken);
					break;

				case "ticker":
					if (FuturesTickerReceived is { } futuresTickerHandler)
						await futuresTickerHandler(Deserialize<WeexWsEnvelope<WeexWsFuturesTicker[]>>(payload), cancellationToken);
					break;

				case "depth":
					if (DepthReceived is { } depthHandler)
						await depthHandler(_section, Deserialize<WeexWsDepth>(payload), cancellationToken);
					break;

				case "trade":
					if (TradesReceived is { } tradeHandler)
						await tradeHandler(_section, Deserialize<WeexWsEnvelope<WeexWsTrade[]>>(payload), cancellationToken);
					break;

				case "kline":
					if (CandleReceived is { } candleHandler)
						await candleHandler(_section, Deserialize<WeexWsEnvelope<WeexWsCandle[]>>(payload), cancellationToken);
					break;

				case "account":
					if (AccountReceived is { } accountHandler)
						await accountHandler(_section,
							Deserialize<WeexWsEnvelope<WeexWsAccountEntry[]>>(payload).Data ?? [], cancellationToken);
					break;

				case "orders":
					if (OrderReceived is { } orderHandler)
						await orderHandler(_section,
							Deserialize<WeexWsEnvelope<WeexWsOrder[]>>(payload).Data ?? [], cancellationToken);
					break;

				case "fill":
					if (FillReceived is { } fillHandler)
						await fillHandler(_section,
							Deserialize<WeexWsEnvelope<WeexWsFill[]>>(payload).Data ?? [], cancellationToken);
					break;

				case "positions":
					if (PositionReceived is { } positionHandler)
						await positionHandler(
							Deserialize<WeexWsEnvelope<WeexWsPosition[]>>(payload).Data ?? [], cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private void OnInit(ClientWebSocket socket)
	{
		socket.Options.SetRequestHeader("User-Agent", "StockSharp-WEEX-Connector/1.0");
		if (!_isPrivate)
			return;

		var authentication = _authenticationProvider();
		socket.Options.SetRequestHeader("ACCESS-KEY", authentication.ApiKey);
		socket.Options.SetRequestHeader("ACCESS-PASSPHRASE", authentication.Passphrase);
		socket.Options.SetRequestHeader("ACCESS-TIMESTAMP", authentication.Timestamp);
		socket.Options.SetRequestHeader("ACCESS-SIGN", authentication.Signature);
	}

	private static T Deserialize<T>(string payload)
		where T : class
		=> JsonConvert.DeserializeObject<T>(payload)
			?? throw new InvalidDataException("WEEX WebSocket returned an empty JSON value.");

	private async ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
	{
		this.AddErrorLog(error);
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static string CreateChannel(string symbol, string channel)
		=> $"{symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant()}@{channel}";

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}
}
