namespace StockSharp.Bluefin.Native;

sealed class BluefinSocketClient : BaseLogReceiver
{
	private static readonly string[] _accountStreams =
	[
		"AccountOrderUpdate",
		"AccountTradeUpdate",
		"AccountAggregatedTradeUpdate",
		"AccountPositionUpdate",
		"AccountUpdate",
		"AccountTransactionUpdate",
		"AccountCommandFailureUpdate",
	];

	private readonly Uri _marketEndpoint;
	private readonly Uri _accountEndpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, HashSet<string>> _marketSubscriptions =
		new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _marketSendGate = new(1, 1);
	private readonly SemaphoreSlim _accountSendGate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _marketClient;
	private WebSocketClient _accountClient;
	private string _accessToken;
	private bool _isAccountSubscribed;

	public BluefinSocketClient(string marketEndpoint, string accountEndpoint,
		string accessToken, WorkingTime workingTime, int reconnectAttempts)
	{
		_marketEndpoint = marketEndpoint.NormalizeSocketEndpoint(
			nameof(marketEndpoint));
		_accountEndpoint = accountEndpoint.NormalizeSocketEndpoint(
			nameof(accountEndpoint));
		_accessToken = accessToken;
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Bluefin_WS";

	public event Func<BluefinMarketStreamMessage, CancellationToken, ValueTask>
		MarketMessageReceived;
	public event Func<BluefinAccountStreamMessage, CancellationToken, ValueTask>
		AccountMessageReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_marketClient is not null || _accountClient is not null)
			throw new InvalidOperationException(
				"Bluefin WebSocket is already initialized.");
		_marketClient = CreateClient(_marketEndpoint, false);
		try
		{
			await _marketClient.ConnectAsync(cancellationToken);
			if (!_accessToken.IsEmpty())
			{
				_accountClient = CreateClient(_accountEndpoint, true);
				await _accountClient.ConnectAsync(cancellationToken);
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
		var account = _accountClient;
		var market = _marketClient;
		_accountClient = null;
		_marketClient = null;
		await DisposeClientAsync(account, cancellationToken);
		await DisposeClientAsync(market, cancellationToken);
	}

	public async ValueTask ReplaceAccessTokenAsync(string accessToken,
		CancellationToken cancellationToken)
	{
		accessToken = accessToken.ThrowIfEmpty(nameof(accessToken));
		var previous = _accountClient;
		_accountClient = null;
		_accessToken = accessToken;
		await DisposeClientAsync(previous, cancellationToken);
		if (_marketClient is null)
			return;
		var client = _accountClient = CreateClient(_accountEndpoint, true);
		try
		{
			await client.ConnectAsync(cancellationToken);
			if (_isAccountSubscribed)
				await SendAccountSubscriptionAsync(client, true,
					cancellationToken);
		}
		catch
		{
			_accountClient = null;
			await DisposeClientAsync(client, cancellationToken);
			throw;
		}
	}

	public async ValueTask SubscribeMarketAsync(string symbol, string stream,
		CancellationToken cancellationToken)
		=> await ChangeMarketSubscriptionAsync(symbol, stream, true,
			cancellationToken);

	public async ValueTask UnsubscribeMarketAsync(string symbol, string stream,
		CancellationToken cancellationToken)
		=> await ChangeMarketSubscriptionAsync(symbol, stream, false,
			cancellationToken);

	public async ValueTask SubscribeAccountAsync(
		CancellationToken cancellationToken)
	{
		if (_accessToken.IsEmpty() || _accountClient?.IsConnected != true)
			throw new InvalidOperationException(
				"Bluefin account WebSocket authentication is unavailable.");
		if (_isAccountSubscribed)
			return;
		_isAccountSubscribed = true;
		try
		{
			await SendAccountSubscriptionAsync(_accountClient, true,
				cancellationToken);
		}
		catch
		{
			_isAccountSubscribed = false;
			throw;
		}
	}

	public async ValueTask UnsubscribeAccountAsync(
		CancellationToken cancellationToken)
	{
		if (!_isAccountSubscribed)
			return;
		_isAccountSubscribed = false;
		if (_accountClient?.IsConnected == true)
			await SendAccountSubscriptionAsync(_accountClient, false,
				cancellationToken);
	}

	private WebSocketClient CreateClient(Uri endpoint, bool isAccount)
	{
		WebSocketClient client = null;
		client = new WebSocketClient(endpoint.ToString(),
			(state, token) => OnStateChangedAsync(client, isAccount, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(message, isAccount, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a), static (s, a) => { })
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		client.Init += socket => socket.Options.DangerousDeflateOptions = new();
		return client;
	}

	private async ValueTask ChangeMarketSubscriptionAsync(string symbol,
		string stream, bool isSubscribe, CancellationToken cancellationToken)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant();
		stream = stream.ThrowIfEmpty(nameof(stream)).Trim();
		var changed = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				if (!_marketSubscriptions.TryGetValue(symbol, out var streams))
					_marketSubscriptions[symbol] = streams =
						new(StringComparer.Ordinal);
				changed = streams.Add(stream);
			}
			else if (_marketSubscriptions.TryGetValue(symbol, out var streams))
			{
				changed = streams.Remove(stream);
				if (streams.Count == 0)
					_marketSubscriptions.Remove(symbol);
			}
		}
		if (!changed || _marketClient?.IsConnected != true)
			return;
		try
		{
			await SendMarketSubscriptionAsync(_marketClient, symbol, stream,
				isSubscribe, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
				{
					if (_marketSubscriptions.TryGetValue(symbol, out var streams))
					{
						streams.Remove(stream);
						if (streams.Count == 0)
							_marketSubscriptions.Remove(symbol);
					}
				}
				else
				{
					if (!_marketSubscriptions.TryGetValue(symbol, out var streams))
						_marketSubscriptions[symbol] = streams =
							new(StringComparer.Ordinal);
					streams.Add(stream);
				}
			}
			throw;
		}
	}

	private async ValueTask SendMarketSubscriptionAsync(WebSocketClient client,
		string symbol, string stream, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		await _marketSendGate.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(new BluefinMarketSubscriptionMessage
			{
				Method = isSubscribe ? "Subscribe" : "Unsubscribe",
				DataStreams =
				[
					new()
					{
						Symbol = symbol,
						Streams = [stream],
					},
				],
			}, cancellationToken);
		}
		finally
		{
			_marketSendGate.Release();
		}
	}

	private async ValueTask SendAccountSubscriptionAsync(WebSocketClient client,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		await _accountSendGate.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(new BluefinAccountSubscriptionMessage
			{
				AuthToken = _accessToken,
				Method = isSubscribe ? "Subscribe" : "Unsubscribe",
				DataStreams = _accountStreams,
			}, cancellationToken);
		}
		finally
		{
			_accountSendGate.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketMessage message,
		bool isAccount, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<BluefinSocketHeader>(payload);
			if (header.IsSuccess == false)
				throw new BluefinApiException(
					"Bluefin WebSocket rejected a command: " +
					(header.Message ?? "unknown error"));
			if (header.Event.IsEmpty())
				return;
			if (isAccount)
				await RaiseAsync(AccountMessageReceived,
					Deserialize<BluefinAccountStreamMessage>(payload),
					cancellationToken);
			else
				await RaiseAsync(MarketMessageReceived,
					Deserialize<BluefinMarketStreamMessage>(payload),
					cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await RaiseErrorAsync(new InvalidDataException(
				"Failed to process a Bluefin WebSocket message.", error),
				cancellationToken);
		}
	}

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _settings) ??
				throw new InvalidDataException(
					"Bluefin returned an empty WebSocket JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bluefin returned malformed WebSocket JSON.", error);
		}
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		bool isAccount, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			if (isAccount)
			{
				if (_isAccountSubscribed)
					await SendAccountSubscriptionAsync(client, true,
						cancellationToken);
			}
			else
			{
				KeyValuePair<string, string[]>[] subscriptions;
				using (_sync.EnterScope())
					subscriptions = [.. _marketSubscriptions.Select(pair =>
						new KeyValuePair<string, string[]>(pair.Key,
							[.. pair.Value]))];
				foreach (var (symbol, streams) in subscriptions)
					foreach (var stream in streams)
						await SendMarketSubscriptionAsync(client, symbol, stream,
							true, cancellationToken);
			}
		}
		if (!isAccount)
			await RaiseAsync(StateChanged, state, cancellationToken);
	}

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

	protected override void DisposeManaged()
	{
		_marketClient?.Dispose();
		_accountClient?.Dispose();
		_marketClient = null;
		_accountClient = null;
		_marketSendGate.Dispose();
		_accountSendGate.Dispose();
		base.DisposeManaged();
	}
}
