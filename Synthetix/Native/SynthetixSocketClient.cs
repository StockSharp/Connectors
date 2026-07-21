namespace StockSharp.Synthetix.Native;

sealed class SynthetixSocketClient : BaseLogReceiver
{
	private sealed record Subscription(string Type, string Symbol,
		string TimeFrame, string Format, int? Depth,
		int? UpdateFrequencyMilliseconds);

	private readonly Uri _infoEndpoint;
	private readonly Uri _tradeEndpoint;
	private readonly SynthetixSigner _signer;
	private readonly string _subAccountId;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _subscriptions = [];
	private readonly Dictionary<string, TaskCompletionSource<bool>> _pending =
		new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _infoSendGate = new(1, 1);
	private readonly SemaphoreSlim _tradeSendGate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _infoClient;
	private WebSocketClient _tradeClient;
	private bool _isPrivateSubscribed;
	private long _requestId;

	public SynthetixSocketClient(string infoEndpoint, string tradeEndpoint,
		SynthetixSigner signer, string subAccountId, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_infoEndpoint = infoEndpoint.NormalizeSynthetixSocketEndpoint(
			nameof(infoEndpoint));
		_tradeEndpoint = tradeEndpoint.NormalizeSynthetixSocketEndpoint(
			nameof(tradeEndpoint));
		_signer = signer ?? throw new ArgumentNullException(nameof(signer));
		_subAccountId = subAccountId;
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Synthetix_WS";

	public event Func<SynthetixSocketNotification<SynthetixPriceUpdate>,
		CancellationToken, ValueTask> PriceReceived;
	public event Func<SynthetixSocketNotification<SynthetixTradeUpdate>,
		CancellationToken, ValueTask> TradeReceived;
	public event Func<SynthetixSocketNotification<SynthetixBookUpdate>,
		CancellationToken, ValueTask> BookReceived;
	public event Func<SynthetixSocketNotification<SynthetixCandleUpdate>,
		CancellationToken, ValueTask> CandleReceived;
	public event Func<SynthetixSocketNotification<SynthetixPrivateUpdate>,
		CancellationToken, ValueTask> PrivateReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_infoClient is not null || _tradeClient is not null)
			throw new InvalidOperationException(
				"Synthetix WebSocket is already initialized.");
		_infoClient = CreateClient(_infoEndpoint, false);
		try
		{
			await _infoClient.ConnectAsync(cancellationToken);
			if (_signer.IsAvailable && !_subAccountId.IsEmpty())
			{
				_tradeClient = CreateClient(_tradeEndpoint, true);
				await _tradeClient.ConnectAsync(cancellationToken);
				await AuthenticateAsync(_tradeClient, cancellationToken);
			}
		}
		catch
		{
			await DisconnectAsync(cancellationToken);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		var trade = _tradeClient;
		var info = _infoClient;
		_tradeClient = null;
		_infoClient = null;
		FailPending(new OperationCanceledException(
			"Synthetix WebSocket disconnected."));
		await DisposeClientAsync(trade, cancellationToken);
		await DisposeClientAsync(info, cancellationToken);
	}

	public ValueTask SubscribePricesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("marketPrices", NormalizeSymbol(symbol),
			null, null, null, null), true, cancellationToken);

	public ValueTask UnsubscribePricesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("marketPrices", NormalizeSymbol(symbol),
			null, null, null, null), false, cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("trades", NormalizeSymbol(symbol), null,
			null, null, null), true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("trades", NormalizeSymbol(symbol), null,
			null, null, null), false, cancellationToken);

	public ValueTask SubscribeBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("orderbook", NormalizeSymbol(symbol),
			null, "snapshot", NormalizeDepth(depth), 250), true,
			cancellationToken);

	public ValueTask UnsubscribeBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("orderbook", NormalizeSymbol(symbol),
			null, "snapshot", NormalizeDepth(depth), 250), false,
			cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, string timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("candles", NormalizeSymbol(symbol),
			timeFrame.ThrowIfEmpty(nameof(timeFrame)), null, null, null), true,
			cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, string timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("candles", NormalizeSymbol(symbol),
			timeFrame.ThrowIfEmpty(nameof(timeFrame)), null, null, null), false,
			cancellationToken);

	public async ValueTask SubscribePrivateAsync(
		CancellationToken cancellationToken)
	{
		if (_tradeClient?.IsConnected != true)
			throw new InvalidOperationException(
				"Synthetix authenticated WebSocket is unavailable.");
		if (_isPrivateSubscribed)
			return;
		_isPrivateSubscribed = true;
		try
		{
			await SendPrivateSubscriptionAsync(_tradeClient, true,
				cancellationToken);
		}
		catch
		{
			_isPrivateSubscribed = false;
			throw;
		}
	}

	public async ValueTask UnsubscribePrivateAsync(
		CancellationToken cancellationToken)
	{
		if (!_isPrivateSubscribed)
			return;
		_isPrivateSubscribed = false;
		if (_tradeClient?.IsConnected == true)
			await SendPrivateSubscriptionAsync(_tradeClient, false,
				cancellationToken);
	}

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		if (_infoClient?.IsConnected == true)
			await SendCommandAsync(_infoClient, false, "ping",
				new SynthetixSocketPingParameters(), cancellationToken);
		if (_tradeClient?.IsConnected == true)
			await SendCommandAsync(_tradeClient, true, "ping",
				new SynthetixSocketPingParameters(), cancellationToken);
	}

	private WebSocketClient CreateClient(Uri endpoint, bool isTrade)
	{
		WebSocketClient client = null;
		client = new WebSocketClient(endpoint.ToString(),
			(state, token) => OnStateChangedAsync(client, isTrade, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a), static (s, a) => { })
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		client.Init += static socket => socket.Options.SetRequestHeader(
			"User-Agent", "StockSharp-Synthetix-Connector/1.0");
		return client;
	}

	private async ValueTask ChangeSubscriptionAsync(Subscription subscription,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		var changed = false;
		using (_sync.EnterScope())
			changed = isSubscribe
				? _subscriptions.Add(subscription)
				: _subscriptions.Remove(subscription);
		if (!changed || _infoClient?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAsync(_infoClient, subscription, isSubscribe,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_subscriptions.Remove(subscription);
				else
					_subscriptions.Add(subscription);
			}
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		Subscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendCommandAsync(client, false,
			isSubscribe ? "subscribe" : "unsubscribe",
			new SynthetixSubscriptionParameters
			{
				Type = subscription.Type,
				Symbol = subscription.Symbol,
				TimeFrame = subscription.TimeFrame,
				Format = subscription.Format,
				Depth = subscription.Depth,
				UpdateFrequencyMilliseconds =
					subscription.UpdateFrequencyMilliseconds,
			}, cancellationToken);

	private ValueTask SendPrivateSubscriptionAsync(WebSocketClient client,
		bool isSubscribe, CancellationToken cancellationToken)
		=> SendCommandAsync(client, true,
			isSubscribe ? "subscribe" : "unsubscribe",
			new SynthetixSubscriptionParameters
			{
				Type = "subAccountUpdates",
				SubAccountId = _subAccountId,
			}, cancellationToken);

	private async ValueTask AuthenticateAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		var seconds = checked((long)(DateTime.UtcNow - DateTime.UnixEpoch)
			.TotalSeconds);
		var auth = _signer.CreateAuthentication(_subAccountId, seconds);
		await SendCommandAsync(client, true, "auth",
			new SynthetixSocketAuthParameters
			{
				Message = auth.Message,
				Signature = auth.Signature,
			}, cancellationToken);
	}

	private async ValueTask SendCommandAsync<T>(WebSocketClient client,
		bool isTrade, string method, T parameters,
		CancellationToken cancellationToken)
	{
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"Synthetix WebSocket is not connected.");
		var id = method + "-" + Interlocked.Increment(ref _requestId)
			.ToString(CultureInfo.InvariantCulture);
		var completion = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_pending.Add(id, completion);
		var gate = isTrade ? _tradeSendGate : _infoSendGate;
		try
		{
			await gate.WaitAsync(cancellationToken);
			try
			{
				await client.SendAsync(new SynthetixSocketRequest<T>
				{
					Id = id,
					Method = method,
					Parameters = parameters,
				}, cancellationToken);
			}
			finally
			{
				gate.Release();
			}
			await completion.Task.WaitAsync(TimeSpan.FromSeconds(10),
				cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				_pending.Remove(id);
		}
	}

	private async ValueTask OnProcessAsync(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<SynthetixSocketHeader>(payload);
			if (!header.Channel.IsEmpty())
			{
				switch (header.Channel)
				{
					case "marketPriceUpdate":
						await RaiseAsync(PriceReceived, Deserialize<
							SynthetixSocketNotification<SynthetixPriceUpdate>>(
							payload), cancellationToken);
						break;
					case "trade":
						await RaiseAsync(TradeReceived, Deserialize<
							SynthetixSocketNotification<SynthetixTradeUpdate>>(
							payload), cancellationToken);
						break;
					case "orderbookUpdate":
						await RaiseAsync(BookReceived, Deserialize<
							SynthetixSocketNotification<SynthetixBookUpdate>>(
							payload), cancellationToken);
						break;
					case "candleUpdate":
						await RaiseAsync(CandleReceived, Deserialize<
							SynthetixSocketNotification<SynthetixCandleUpdate>>(
							payload), cancellationToken);
						break;
					case "subAccountUpdate":
						await RaiseAsync(PrivateReceived, Deserialize<
							SynthetixSocketNotification<SynthetixPrivateUpdate>>(
							payload), cancellationToken);
						break;
				}
				return;
			}
			var id = header.Id.IsEmpty() ? header.RequestId : header.Id;
			if (id.IsEmpty())
				return;
			TaskCompletionSource<bool> completion;
			using (_sync.EnterScope())
				_pending.TryGetValue(id, out completion);
			if (completion is null)
				return;
			if (header.Status >= 400 || header.Error is not null)
				completion.TrySetException(new InvalidOperationException(
					"Synthetix WebSocket " + (header.Error?.Code ??
						header.Status.ToString(CultureInfo.InvariantCulture)) +
					": " + (header.Error?.Message ?? "command rejected")));
			else
				completion.TrySetResult(true);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await RaiseErrorAsync(new InvalidDataException(
				"Failed to process a Synthetix WebSocket message.", error),
				cancellationToken);
		}
	}

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _settings) ??
				throw new InvalidDataException(
					"Synthetix returned an empty WebSocket JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Synthetix returned malformed WebSocket JSON.", error);
		}
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		bool isTrade, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			if (isTrade)
			{
				await AuthenticateAsync(client, cancellationToken);
				if (_isPrivateSubscribed)
					await SendPrivateSubscriptionAsync(client, true,
						cancellationToken);
			}
			else
			{
				Subscription[] subscriptions;
				using (_sync.EnterScope())
					subscriptions = [.. _subscriptions];
				foreach (var subscription in subscriptions)
					await SendSubscriptionAsync(client, subscription, true,
						cancellationToken);
			}
		}
		if (state == ConnectionStates.Failed)
			FailPending(new IOException(
				"Synthetix WebSocket connection failed."));
		if (!isTrade)
			await RaiseAsync(StateChanged, state, cancellationToken);
	}

	private void FailPending(Exception error)
	{
		TaskCompletionSource<bool>[] pending;
		using (_sync.EnterScope())
		{
			pending = [.. _pending.Values];
			_pending.Clear();
		}
		foreach (var completion in pending)
			completion.TrySetException(error);
	}

	private async ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
	{
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static ValueTask RaiseAsync<T>(
		Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> handler is null || value is null
			? default
			: handler(value, cancellationToken);

	private static async ValueTask DisposeClientAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		if (client is null)
			return;
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
		}
	}

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant();

	private static int NormalizeDepth(int depth)
		=> depth <= 10 ? 10 : depth <= 50 ? 50 : 100;

	protected override void DisposeManaged()
	{
		_infoClient?.Dispose();
		_tradeClient?.Dispose();
		_infoClient = null;
		_tradeClient = null;
		_infoSendGate.Dispose();
		_tradeSendGate.Dispose();
		base.DisposeManaged();
	}
}
