namespace StockSharp.CryptoCom.Native;

sealed class CryptoComWsClient : BaseLogReceiver
{
	private const string _authMethod = "public/auth";
	private const string _subscribeMethod = "subscribe";
	private const string _unsubscribeMethod = "unsubscribe";
	private readonly WebSocketClient _client;
	private readonly bool _isUser;
	private readonly string _apiKey;
	private readonly CryptoComSigner _signer;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _channels = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _restoreSync = new(1, 1);
	private TaskCompletionSource<CryptoComWsHeader> _authentication;
	private long _nextRequestId;
	private bool _isReady;

	public CryptoComWsClient(string endpoint, bool isUser, SecureString key, SecureString secret,
		WorkingTime workingTime)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		_isUser = isUser;
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_signer = isUser && !secret.IsEmpty() ? new CryptoComSigner(secret) : null;
		if (isUser && (_apiKey.IsEmpty() || _signer is null))
			throw new ArgumentException("Crypto.com Exchange user WebSocket requires an API key and secret.");

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
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
	}

	public override string Name => nameof(CryptoCom) + "_" + (_isUser ? "UserWs" : "MarketWs");

	public event Func<CryptoComWsEnvelope<CryptoComTicker>, CancellationToken, ValueTask> TickerReceived;
	public event Func<CryptoComWsEnvelope<CryptoComWsBookItem>, CancellationToken, ValueTask> BookReceived;
	public event Func<CryptoComWsEnvelope<CryptoComPublicTrade>, CancellationToken, ValueTask> TradeReceived;
	public event Func<CryptoComWsEnvelope<CryptoComCandle>, CancellationToken, ValueTask> CandleReceived;
	public event Func<CryptoComWsEnvelope<CryptoComOrder>, CancellationToken, ValueTask> UserOrderReceived;
	public event Func<CryptoComWsEnvelope<CryptoComUserTrade>, CancellationToken, ValueTask> UserTradeReceived;
	public event Func<CryptoComWsEnvelope<CryptoComBalance>, CancellationToken, ValueTask> BalanceReceived;
	public event Func<CryptoComWsEnvelope<CryptoComPosition>, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_signer?.Dispose();
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

	public ValueTask SubscribeAsync(string channel, CancellationToken cancellationToken)
	{
		var shouldSend = false;
		using (_sync.EnterScope())
			shouldSend = _channels.Add(channel.ThrowIfEmpty(nameof(channel)));

		return shouldSend ? SendSubscriptionAsync(channel, true, cancellationToken) : default;
	}

	public ValueTask UnsubscribeAsync(string channel, CancellationToken cancellationToken)
	{
		var shouldSend = false;
		using (_sync.EnterScope())
			shouldSend = _channels.Remove(channel);

		return shouldSend ? SendSubscriptionAsync(channel, false, cancellationToken) : default;
	}

	public async ValueTask ResubscribeAsync(string channel, CancellationToken cancellationToken)
	{
		await SendSubscriptionAsync(channel, false, cancellationToken);
		await SendSubscriptionAsync(channel, true, cancellationToken);
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state, CancellationToken cancellationToken)
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
			await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
			if (_isUser)
				await AuthenticateAsync(cancellationToken);

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

	private async ValueTask AuthenticateAsync(CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _nextRequestId);
		var nonce = DateTime.UtcNow.ToUnixMilliseconds();
		var completion = new TaskCompletionSource<CryptoComWsHeader>(TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_authentication = completion;

		try
		{
			await _client.SendAsync(new CryptoComWsAuthRequest
			{
				Id = id,
				Method = _authMethod,
				ApiKey = _apiKey,
				Signature = _signer.Sign(_authMethod, id, _apiKey, CryptoComEmptyParams.Instance, nonce),
				Nonce = nonce,
			}, cancellationToken, id);

			await completion.Task.WaitAsync(cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
			{
				if (ReferenceEquals(_authentication, completion))
					_authentication = null;
			}
		}
	}

	private ValueTask SendSubscriptionAsync(string channel, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _nextRequestId);
		var isBook = channel.StartsWith("book.", StringComparison.OrdinalIgnoreCase);
		return _client.SendAsync(new CryptoComWsSubscriptionRequest
		{
			Id = id,
			Method = isSubscribe ? _subscribeMethod : _unsubscribeMethod,
			Nonce = DateTime.UtcNow.ToUnixMilliseconds(),
			Parameters = new()
			{
				Channels = [channel],
				BookSubscriptionType = isSubscribe && isBook ? "SNAPSHOT_AND_UPDATE" : null,
				BookUpdateFrequency = isSubscribe && isBook ? 100 : null,
			},
		}, cancellationToken, id);
	}

	private async ValueTask OnProcessAsync(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			var header = Deserialize<CryptoComWsHeader>(payload);
			if (header.Method.EqualsIgnoreCase("public/heartbeat"))
			{
				await _client.SendAsync(new CryptoComWsHeartbeatResponse
				{
					Id = header.Id,
					Method = "public/respond-heartbeat",
				}, cancellationToken, header.Id);
				return;
			}

			if (header.Method.EqualsIgnoreCase(_authMethod))
			{
				TaskCompletionSource<CryptoComWsHeader> completion;
				using (_sync.EnterScope())
					completion = _authentication;

				if (header.Code == 0)
					completion?.TrySetResult(header);
				else
					completion?.TrySetException(CreateError(header));
				return;
			}

			if (header.Code != 0)
				throw CreateError(header);

			var routing = Deserialize<CryptoComWsRoutingEnvelope>(payload).Result;
			if (routing?.Channel.IsEmpty() != false)
				return;

			switch (routing.Channel.ToLowerInvariant())
			{
				case "ticker":
					if (TickerReceived is { } tickerHandler)
						await tickerHandler(Deserialize<CryptoComWsEnvelope<CryptoComTicker>>(payload), cancellationToken);
					break;

				case "book":
				case "book.update":
					if (BookReceived is { } bookHandler)
						await bookHandler(Deserialize<CryptoComWsEnvelope<CryptoComWsBookItem>>(payload), cancellationToken);
					break;

				case "trade":
					if (TradeReceived is { } tradeHandler)
						await tradeHandler(Deserialize<CryptoComWsEnvelope<CryptoComPublicTrade>>(payload), cancellationToken);
					break;

				case "candlestick":
					if (CandleReceived is { } candleHandler)
						await candleHandler(Deserialize<CryptoComWsEnvelope<CryptoComCandle>>(payload), cancellationToken);
					break;

				case "user.order":
				case "user.advance.order":
					if (UserOrderReceived is { } orderHandler)
						await orderHandler(Deserialize<CryptoComWsEnvelope<CryptoComOrder>>(payload), cancellationToken);
					break;

				case "user.trade":
					if (UserTradeReceived is { } userTradeHandler)
						await userTradeHandler(Deserialize<CryptoComWsEnvelope<CryptoComUserTrade>>(payload), cancellationToken);
					break;

				case "user.balance":
					if (BalanceReceived is { } balanceHandler)
						await balanceHandler(Deserialize<CryptoComWsEnvelope<CryptoComBalance>>(payload), cancellationToken);
					break;

				case "user.positions":
					if (PositionReceived is { } positionHandler)
						await positionHandler(Deserialize<CryptoComWsEnvelope<CryptoComPosition>>(payload), cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private static T Deserialize<T>(string payload)
		where T : class
		=> JsonConvert.DeserializeObject<T>(payload)
			?? throw new InvalidDataException("Crypto.com Exchange WebSocket returned an empty JSON value.");

	private static Exception CreateError(CryptoComWsHeader header)
		=> new InvalidOperationException($"Crypto.com Exchange WebSocket error {header.Code}: {header.Message.IsEmpty(header.Original)}");

	private async ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
	{
		this.AddErrorLog(error);
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}
}
