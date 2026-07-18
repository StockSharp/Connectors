namespace StockSharp.Ourbit.Native;

readonly record struct OurbitFuturesWsSubscription(string Method, string Symbol, string Interval);

sealed class OurbitFuturesWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly string _apiKey;
	private readonly HMACSHA256 _hasher;
	private readonly Lock _sync = new();
	private readonly Lock _signSync = new();
	private readonly HashSet<OurbitFuturesWsSubscription> _subscriptions = [];
	private readonly SemaphoreSlim _connectionSync = new(1, 1);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private WebSocketClient _client;
	private DateTime _nextSendTime;
	private bool _isLoggedIn;
	private bool _isLoginPending;
	private OurbitFuturesWsFilter[] _privateFilters = [];

	public OurbitFuturesWsClient(string endpoint, SecureString key, SecureString secret,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/');
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Ourbit) + "_FuturesWs";

	public event Func<OurbitFuturesTicker, CancellationToken, ValueTask> TickerReceived;
	public event Func<string, OurbitFuturesDepth, CancellationToken, ValueTask> DepthReceived;
	public event Func<string, OurbitFuturesTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<string, OurbitFuturesWsCandle, CancellationToken, ValueTask> CandleReceived;
	public event Func<OurbitFuturesOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<OurbitFuturesFill, CancellationToken, ValueTask> FillReceived;
	public event Func<OurbitFuturesBalance, long, CancellationToken, ValueTask> BalanceReceived;
	public event Func<OurbitFuturesPosition, long, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_hasher?.Dispose();
		_connectionSync.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await _connectionSync.WaitAsync(cancellationToken);
		try
		{
			if (_client is not null)
				throw new InvalidOperationException("Ourbit futures WebSocket is already initialized.");
			var client = CreateClient();
			_client = client;
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			_client?.Dispose();
			_client = null;
			throw;
		}
		finally
		{
			_connectionSync.Release();
		}
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		await _connectionSync.WaitAsync(cancellationToken);
		try
		{
			var client = _client;
			_client = null;
			_isLoggedIn = false;
			_isLoginPending = false;
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
		finally
		{
			_connectionSync.Release();
		}
	}

	public ValueTask SubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("sub.ticker", symbol, null), true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("sub.ticker", symbol, null), false, cancellationToken);

	public ValueTask SubscribeDepthAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("sub.depth", symbol, null), true, cancellationToken);

	public ValueTask UnsubscribeDepthAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("sub.depth", symbol, null), false, cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("sub.deal", symbol, null), true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("sub.deal", symbol, null), false, cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("sub.kline", symbol, interval), true, cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new("sub.kline", symbol, interval), false, cancellationToken);

	public async ValueTask SetPrivateSubscriptionsAsync(bool orders, bool fills, bool positions,
		bool balances, CancellationToken cancellationToken)
	{
		var filters = new List<OurbitFuturesWsFilter>();
		if (orders)
			filters.Add(new() { Name = "order" });
		if (fills)
			filters.Add(new() { Name = "order.deal" });
		if (positions)
			filters.Add(new() { Name = "position" });
		if (balances)
			filters.Add(new() { Name = "asset" });
		using (_sync.EnterScope())
			_privateFilters = [.. filters];
		if (filters.Count == 0)
		{
			if (_isLoggedIn)
				await SendPrivateFilterAsync(cancellationToken);
			return;
		}
		EnsureCredentials();
		if (_isLoggedIn)
			await SendPrivateFilterAsync(cancellationToken);
		else
			await SendLoginAsync(cancellationToken);
	}

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendAsync(client, new OurbitFuturesWsCommand { Method = "ping" }, cancellationToken)
			: default;

	private ValueTask ChangeSubscriptionAsync(OurbitFuturesWsSubscription subscription,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		bool shouldSend;
		using (_sync.EnterScope())
			shouldSend = isSubscribe ? _subscriptions.Add(subscription) : _subscriptions.Remove(subscription);
		if (!shouldSend || _client?.IsConnected != true)
			return default;
		return SendSubscriptionAsync(subscription, isSubscribe, cancellationToken);
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Ourbit-Connector/1.0");
		return client;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Disconnected)
		{
			_isLoggedIn = false;
			_isLoginPending = false;
		}
		if (state == ConnectionStates.Restored)
		{
			try
			{
				await RestoreAsync(client, cancellationToken);
			}
			catch (Exception error)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask RestoreAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		OurbitFuturesWsSubscription[] subscriptions;
		OurbitFuturesWsFilter[] filters;
		using (_sync.EnterScope())
		{
			subscriptions = [.. _subscriptions];
			filters = _privateFilters;
		}
		foreach (var subscription in subscriptions)
			await SendSubscriptionAsync(client, subscription, true, cancellationToken);
		_isLoggedIn = false;
		_isLoginPending = false;
		if (filters.Length > 0)
			await SendLoginAsync(cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(OurbitFuturesWsSubscription subscription,
		bool isSubscribe, CancellationToken cancellationToken)
		=> SendSubscriptionAsync(_client, subscription, isSubscribe, cancellationToken);

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		OurbitFuturesWsSubscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var method = isSubscribe
			? subscription.Method
			: subscription.Method.Replace("sub.", "unsub.", StringComparison.Ordinal);
		return SendAsync(client, new OurbitFuturesWsCommand
		{
			Method = method,
			Parameters = new()
			{
				Symbol = subscription.Symbol,
				Interval = subscription.Interval,
				IsCompressed = subscription.Method == "sub.depth" ? false : null,
			},
		}, cancellationToken);
	}

	private async ValueTask SendLoginAsync(CancellationToken cancellationToken)
	{
		if (_isLoginPending || _isLoggedIn)
			return;
		EnsureCredentials();
		var client = _client;
		if (client?.IsConnected != true)
			return;
		_isLoginPending = true;
		var requestTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			.ToString(CultureInfo.InvariantCulture);
		await SendAsync(client, new OurbitFuturesWsCommand
		{
			IsSubscribe = false,
			Method = "login",
			Parameters = new()
			{
				ApiKey = _apiKey,
				RequestTime = requestTime,
				Signature = Sign(_apiKey + requestTime),
			},
		}, cancellationToken);
	}

	private ValueTask SendPrivateFilterAsync(CancellationToken cancellationToken)
	{
		OurbitFuturesWsFilter[] filters;
		using (_sync.EnterScope())
			filters = _privateFilters;
		if (_client?.IsConnected != true)
			return default;
		return SendAsync(_client, new OurbitFuturesWsCommand
		{
			Method = "personal.filter",
			Parameters = new() { Filters = filters },
		}, cancellationToken);
	}

	private async ValueTask SendAsync(WebSocketClient client, OurbitFuturesWsCommand command,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextSendTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			await client.SendAsync(command, cancellationToken);
			_nextSendTime = DateTime.UtcNow.AddMilliseconds(10);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong"))
			return;
		try
		{
			var header = Deserialize<OurbitFuturesWsHeader>(payload);
			if (header?.Channel.IsEmpty() != false)
				return;
			var channel = header.Channel;
			if (channel.EqualsIgnoreCase("rs.error"))
			{
				var reply = Deserialize<OurbitFuturesWsReply>(payload);
				_isLoginPending = false;
				throw new InvalidOperationException($"Ourbit futures WebSocket error: {reply?.Data}");
			}
			if (channel.EqualsIgnoreCase("rs.login"))
			{
				var reply = Deserialize<OurbitFuturesWsReply>(payload);
				_isLoginPending = false;
				_isLoggedIn = reply?.Data.EqualsIgnoreCase("success") == true;
				if (!_isLoggedIn)
					throw new InvalidOperationException($"Ourbit futures WebSocket login failed: {reply?.Data}");
				await SendPrivateFilterAsync(cancellationToken);
				return;
			}
			if (channel.StartsWith("rs.", StringComparison.OrdinalIgnoreCase))
				return;

			switch (channel)
			{
				case "push.ticker":
				{
					var data = Deserialize<OurbitFuturesWsEnvelope<OurbitFuturesTicker>>(payload);
					if (data?.Data is not null && TickerReceived is { } handler)
						await handler(data.Data, cancellationToken);
					break;
				}
				case "push.depth":
				{
					var data = Deserialize<OurbitFuturesWsEnvelope<OurbitFuturesDepth>>(payload);
					if (data?.Data is not null && DepthReceived is { } handler)
						await handler(data.Symbol, data.Data, cancellationToken);
					break;
				}
				case "push.deal":
				{
					var data = Deserialize<OurbitFuturesWsEnvelope<OurbitFuturesTrade>>(payload);
					if (data?.Data is not null && TradeReceived is { } handler)
						await handler(data.Symbol, data.Data, cancellationToken);
					break;
				}
				case "push.kline":
				{
					var data = Deserialize<OurbitFuturesWsEnvelope<OurbitFuturesWsCandle>>(payload);
					if (data?.Data is not null && CandleReceived is { } handler)
						await handler(data.Symbol, data.Data, cancellationToken);
					break;
				}
				case "push.personal.order":
				{
					var data = Deserialize<OurbitFuturesWsEnvelope<OurbitFuturesOrder>>(payload);
					if (data?.Data is not null && OrderReceived is { } handler)
						await handler(data.Data, cancellationToken);
					break;
				}
				case "push.personal.order.deal":
				{
					var data = Deserialize<OurbitFuturesWsEnvelope<OurbitFuturesFill>>(payload);
					if (data?.Data is not null && FillReceived is { } handler)
						await handler(data.Data, cancellationToken);
					break;
				}
				case "push.personal.asset":
				{
					var data = Deserialize<OurbitFuturesWsEnvelope<OurbitFuturesBalance>>(payload);
					if (data?.Data is not null && BalanceReceived is { } handler)
						await handler(data.Data, data.Time, cancellationToken);
					break;
				}
				case "push.personal.position":
				{
					var data = Deserialize<OurbitFuturesWsEnvelope<OurbitFuturesPosition>>(payload);
					if (data?.Data is not null && PositionReceived is { } handler)
						await handler(data.Data, data.Time, cancellationToken);
					break;
				}
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private void EnsureCredentials()
	{
		if (_apiKey.IsEmpty() || _hasher is null)
			throw new InvalidOperationException("Ourbit API key and secret are required for private futures streaming.");
	}

	private string Sign(string value)
	{
		using (_signSync.EnterScope())
			return Convert.ToHexString(_hasher.ComputeHash(value.UTF8())).ToLowerInvariant();
	}

	private static TMessage Deserialize<TMessage>(string payload)
		=> JsonConvert.DeserializeObject<TMessage>(payload, new JsonSerializerSettings
		{
			DateParseHandling = DateParseHandling.None,
			NullValueHandling = NullValueHandling.Ignore,
			Culture = CultureInfo.InvariantCulture,
		});

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
