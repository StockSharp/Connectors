namespace StockSharp.Bitvavo.Native;

sealed class BitvavoWsClient : BaseLogReceiver
{
	private const long _accessWindow = 10000;
	private readonly string _endpoint;
	private readonly BitvavoRestClient _restClient;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<SubscriptionKey> _subscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly SemaphoreSlim _authSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private string[] _accountMarkets;
	private bool _isAuthenticated;
	private TaskCompletionSource<bool> _authentication;

	private readonly record struct SubscriptionKey(BitvavoChannels Channel, string Market,
		string Interval);

	public BitvavoWsClient(string endpoint, BitvavoRestClient restClient,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/');
		_restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Bitvavo) + "_Ws";

	public event Func<BitvavoWsTicker, CancellationToken, ValueTask> TickerReceived;
	public event Func<BitvavoTicker, CancellationToken, ValueTask> Ticker24Received;
	public event Func<BitvavoPublicTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<BitvavoOrderBook, CancellationToken, ValueTask> BookReceived;
	public event Func<BitvavoWsCandles, CancellationToken, ValueTask> CandlesReceived;
	public event Func<BitvavoOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<BitvavoFill, CancellationToken, ValueTask> FillReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		_authSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException("Bitvavo WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeTickerAsync(string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(BitvavoChannels.Ticker, market, null, true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(BitvavoChannels.Ticker, market, null, false,
			cancellationToken);

	public ValueTask SubscribeTicker24Async(string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(BitvavoChannels.Ticker24, market, null, true,
			cancellationToken);

	public ValueTask UnsubscribeTicker24Async(string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(BitvavoChannels.Ticker24, market, null, false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(BitvavoChannels.Trades, market, null, true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(BitvavoChannels.Trades, market, null, false,
			cancellationToken);

	public ValueTask SubscribeBookAsync(string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(BitvavoChannels.Book, market, null, true,
			cancellationToken);

	public ValueTask UnsubscribeBookAsync(string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(BitvavoChannels.Book, market, null, false,
			cancellationToken);

	public async ValueTask ResubscribeBookAsync(string market,
		CancellationToken cancellationToken)
	{
		if (_client?.IsConnected != true)
			return;
		var key = new SubscriptionKey(BitvavoChannels.Book, market, null);
		using (_sync.EnterScope())
			if (!_subscriptions.Contains(key))
				return;
		await SendSubscriptionAsync(_client, key, false, cancellationToken);
		await SendSubscriptionAsync(_client, key, true, cancellationToken);
	}

	public ValueTask SubscribeCandlesAsync(string market, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(BitvavoChannels.Candles, market, interval, true,
			cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string market, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(BitvavoChannels.Candles, market, interval, false,
			cancellationToken);

	public async ValueTask SubscribeAccountAsync(string[] markets,
		CancellationToken cancellationToken)
	{
		markets = [.. (markets ?? []).Where(static market => !market.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(static market => market, StringComparer.OrdinalIgnoreCase)];
		if (markets.Length == 0)
			throw new InvalidOperationException(
				"Bitvavo account subscription requires at least one market.");

		using (_sync.EnterScope())
		{
			if (_accountMarkets is not null)
				return;
			_accountMarkets = markets;
		}

		try
		{
			if (_client?.IsConnected == true)
			{
				await EnsureAuthenticatedAsync(_client, cancellationToken);
				await SendAccountSubscriptionAsync(_client, markets, true,
					cancellationToken);
			}
		}
		catch
		{
			using (_sync.EnterScope())
				_accountMarkets = null;
			throw;
		}
	}

	public async ValueTask UnsubscribeAccountAsync(CancellationToken cancellationToken)
	{
		string[] markets;
		using (_sync.EnterScope())
		{
			markets = _accountMarkets;
			_accountMarkets = null;
		}
		if (markets is not null && _client?.IsConnected == true)
			await SendAccountSubscriptionAsync(_client, markets, false,
				cancellationToken);
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
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Bitvavo-Connector/1.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		ResetAuthentication();
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

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			ResetAuthentication();
			SubscriptionKey[] subscriptions;
			string[] accountMarkets;
			using (_sync.EnterScope())
			{
				subscriptions = [.. _subscriptions];
				accountMarkets = _accountMarkets;
			}
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription, true,
					cancellationToken);
			if (accountMarkets is not null)
			{
				await EnsureAuthenticatedAsync(client, cancellationToken);
				await SendAccountSubscriptionAsync(client, accountMarkets, true,
					cancellationToken);
			}
		}

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(BitvavoChannels channel, string market,
		string interval, bool isSubscribe, CancellationToken cancellationToken)
	{
		var key = new SubscriptionKey(channel,
			market.ThrowIfEmpty(nameof(market)), interval);
		using (_sync.EnterScope())
			if (isSubscribe ? !_subscriptions.Add(key) : !_subscriptions.Remove(key))
				return;

		if (_client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAsync(_client, key, isSubscribe, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_subscriptions.Remove(key);
				else
					_subscriptions.Add(key);
			}
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		SubscriptionKey subscription, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new BitvavoWsSubscriptionCommand
		{
			Action = isSubscribe
				? BitvavoActions.Subscribe
				: BitvavoActions.Unsubscribe,
			Channels =
			[
				new()
				{
					Name = subscription.Channel,
					Markets = [subscription.Market],
					Intervals = subscription.Interval.IsEmpty()
						? null
						: [subscription.Interval],
				},
			],
		}, cancellationToken);

	private ValueTask SendAccountSubscriptionAsync(WebSocketClient client,
		string[] markets, bool isSubscribe, CancellationToken cancellationToken)
		=> SendAsync(client, new BitvavoWsSubscriptionCommand
		{
			Action = isSubscribe
				? BitvavoActions.Subscribe
				: BitvavoActions.Unsubscribe,
			Channels =
			[
				new()
				{
					Name = BitvavoChannels.Account,
					Markets = markets,
				},
			],
		}, cancellationToken);

	private async ValueTask EnsureAuthenticatedAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		if (_isAuthenticated)
			return;
		if (!_restClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Bitvavo API key and secret are required for private WebSocket channels.");

		await _authSync.WaitAsync(cancellationToken);
		try
		{
			if (_isAuthenticated)
				return;
			TaskCompletionSource<bool> completion;
			using (_sync.EnterScope())
				completion = _authentication ??= new(
					TaskCreationOptions.RunContinuationsAsynchronously);

			if (!completion.Task.IsCompleted)
			{
				var timestamp = _restClient.CreateWebSocketTimestamp();
				await SendAsync(client, new BitvavoWsAuthenticateCommand
				{
					Key = _restClient.ApiKey,
					Signature = _restClient.CreateWebSocketSignature(timestamp),
					Timestamp = timestamp,
					Window = _accessWindow,
				}, cancellationToken);
			}

			if (!await completion.Task.WaitAsync(TimeSpan.FromSeconds(15),
				cancellationToken))
				throw new InvalidOperationException(
					"Bitvavo rejected WebSocket authentication.");
			_isAuthenticated = true;
		}
		finally
		{
			_authSync.Release();
		}
	}

	private void ResetAuthentication()
	{
		TaskCompletionSource<bool> completion;
		using (_sync.EnterScope())
		{
			_isAuthenticated = false;
			completion = _authentication;
			_authentication = null;
		}
		completion?.TrySetCanceled();
	}

	private async ValueTask SendAsync<TPayload>(WebSocketClient client, TPayload payload,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(payload, cancellationToken);
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
		if (payload.IsEmpty())
			return;

		try
		{
			var header = Deserialize<BitvavoWsHeader>(payload);
			if (header.ErrorCode is not null || !header.Error.IsEmpty())
			{
				var error = new InvalidOperationException(
					$"Bitvavo WebSocket error {header.ErrorCode}: {header.Error}".Trim());
				TaskCompletionSource<bool> completion;
				using (_sync.EnterScope())
					completion = _authentication;
				completion?.TrySetException(error);
				throw error;
			}
			if (header.IsAuthenticated is bool isAuthenticated)
			{
				TaskCompletionSource<bool> completion;
				using (_sync.EnterScope())
				{
					_isAuthenticated = isAuthenticated;
					completion = _authentication;
				}
				completion?.TrySetResult(isAuthenticated);
				return;
			}

			switch (header.Event)
			{
				case BitvavoEvents.Ticker:
				{
					var ticker = Deserialize<BitvavoWsTicker>(payload);
					if (!ticker.Market.IsEmpty() && TickerReceived is { } handler)
						await handler(ticker, cancellationToken);
					break;
				}
				case BitvavoEvents.Ticker24:
				{
					var envelope = Deserialize<BitvavoWsTicker24Envelope>(payload);
					if (envelope.Data is not null && Ticker24Received is { } handler)
						await handler(envelope.Data, cancellationToken);
					break;
				}
				case BitvavoEvents.Trade:
				{
					var trade = Deserialize<BitvavoPublicTrade>(payload);
					if (!trade.Market.IsEmpty() && TradeReceived is { } handler)
						await handler(trade, cancellationToken);
					break;
				}
				case BitvavoEvents.Book:
				{
					var book = Deserialize<BitvavoOrderBook>(payload);
					if (!book.Market.IsEmpty() && BookReceived is { } handler)
						await handler(book, cancellationToken);
					break;
				}
				case BitvavoEvents.Candles:
				{
					var candles = Deserialize<BitvavoWsCandles>(payload);
					if (!candles.Market.IsEmpty() && CandlesReceived is { } handler)
						await handler(candles, cancellationToken);
					break;
				}
				case BitvavoEvents.Order:
				{
					var order = Deserialize<BitvavoOrder>(payload);
					if (!order.Market.IsEmpty() && OrderReceived is { } handler)
						await handler(order, cancellationToken);
					break;
				}
				case BitvavoEvents.Fill:
				{
					var fill = Deserialize<BitvavoFill>(payload);
					if (!fill.Market.IsEmpty() && FillReceived is { } handler)
						await handler(fill, cancellationToken);
					break;
				}
				case BitvavoEvents.Subscribed:
				case BitvavoEvents.Unsubscribed:
				case null:
					break;
				default:
					throw new InvalidDataException(
						$"Unknown Bitvavo WebSocket event '{header.Event}'.");
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings)
				?? throw new InvalidDataException(
					"Bitvavo WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bitvavo WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
