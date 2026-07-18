namespace StockSharp.Pionex.Native;

readonly record struct PionexWsChannel(string Topic, string Symbol, int? Limit);

sealed class PionexWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly PionexSections? _section;
	private readonly bool _isPrivate;
	private readonly string _apiKey;
	private readonly HMACSHA256 _hasher;
	private readonly Lock _signSync = new();
	private readonly Lock _sync = new();
	private readonly HashSet<PionexWsChannel> _channels = [];
	private readonly SemaphoreSlim _sessionSync = new(1, 1);
	private readonly SemaphoreSlim _connectionSync = new(1, 1);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private DateTime _nextSendTime;
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _authentication;
	private bool _isReady;
	private bool _isDisposing;

	public PionexWsClient(string endpoint, PionexSections? section, bool isPrivate,
		SecureString key, SecureString secret, WorkingTime workingTime)
	{
		_endpoint = NormalizeEndpoint(endpoint);
		_section = section;
		_isPrivate = isPrivate;
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));

		if (isPrivate && (section is null || _apiKey.IsEmpty() || _hasher is null))
			throw new ArgumentException("Pionex private WebSocket requires a market section, API key, and secret.");

		if (isPrivate)
		{
			_channels.Add(new("BALANCE", null, null));
			if (section == PionexSections.Futures)
			{
				_channels.Add(new("ORDER", "ALL", null));
				_channels.Add(new("FILL", "ALL", null));
				_channels.Add(new("POSITION", "ALL", null));
			}
		}
	}

	private WorkingTime WorkingTime { get; }

	private bool IsSpotPrivate => _isPrivate && _section == PionexSections.Spot;

	public override string Name => nameof(Pionex) + "_" +
		(_isPrivate ? $"{_section}_UserWs" : "MarketWs");

	public event Func<PionexWsTradeMessage, CancellationToken, ValueTask> TradesReceived;
	public event Func<PionexWsDepthMessage, CancellationToken, ValueTask> DepthReceived;
	public event Func<PionexWsIndexMessage, CancellationToken, ValueTask> IndexReceived;
	public event Func<PionexSections, PionexWsOrderMessage, CancellationToken, ValueTask> OrderReceived;
	public event Func<PionexSections, PionexWsFillMessage, CancellationToken, ValueTask> FillReceived;
	public event Func<PionexSections, PionexWsBalanceMessage, CancellationToken, ValueTask> BalanceReceived;
	public event Func<PionexWsPositionMessage, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_isDisposing = true;
		_isReady = false;
		_client?.Dispose();
		_hasher?.Dispose();
		_sessionSync.Dispose();
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
				throw new InvalidOperationException("Pionex WebSocket is already initialized.");
			var client = CreateClient();
			_client = client;
			await client.ConnectAsync(cancellationToken);
			await RestoreSessionAsync(client, cancellationToken);
			_isReady = true;
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
		_isReady = false;
		var client = _client;
		_client = null;
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

	public ValueTask SubscribeAsync(string topic, string symbol, int? limit,
		CancellationToken cancellationToken)
	{
		var channel = new PionexWsChannel(topic.ThrowIfEmpty(nameof(topic)).ToUpperInvariant(),
			symbol?.ToUpperInvariant(), limit);
		bool shouldSend;
		using (_sync.EnterScope())
			shouldSend = _channels.Add(channel);
		return shouldSend && _client is { IsConnected: true } client
			? SendSubscriptionAsync(client, channel, true, cancellationToken)
			: default;
	}

	public ValueTask UnsubscribeAsync(string topic, string symbol, int? limit,
		CancellationToken cancellationToken)
	{
		var channel = new PionexWsChannel(topic?.ToUpperInvariant(), symbol?.ToUpperInvariant(), limit);
		bool shouldSend;
		using (_sync.EnterScope())
			shouldSend = _channels.Remove(channel);
		return shouldSend && _client is { IsConnected: true } client
			? SendSubscriptionAsync(client, channel, false, cancellationToken)
			: default;
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			IsSpotPrivate ? CreateSpotPrivateEndpoint() : _endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = IsSpotPrivate ? 0 : 5,
			WorkingTime = WorkingTime,
			DisableAutoResend = true,
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
		client.Init += static socket =>
			socket.Options.SetRequestHeader("User-Agent", "StockSharp-Pionex-Connector/1.0");
		return client;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient source, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored && _isReady && ReferenceEquals(source, _client))
		{
			try
			{
				await RestoreSessionAsync(source, cancellationToken);
			}
			catch (Exception error)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
		}

		if (state == ConnectionStates.Failed && IsSpotPrivate && _isReady && !_isDisposing &&
			ReferenceEquals(source, _client))
		{
			await ReplaceSpotPrivateClientAsync(source, cancellationToken);
			return;
		}

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ReplaceSpotPrivateClientAsync(WebSocketClient failedClient,
		CancellationToken cancellationToken)
	{
		await _connectionSync.WaitAsync(cancellationToken);
		try
		{
			if (!_isReady || _isDisposing || !ReferenceEquals(failedClient, _client))
				return;

			var replacement = CreateClient();
			_client = replacement;
			try
			{
				await replacement.ConnectAsync(cancellationToken);
				await RestoreSessionAsync(replacement, cancellationToken);
				failedClient.Dispose();
				if (StateChanged is { } handler)
					await handler(ConnectionStates.Restored, cancellationToken);
			}
			catch
			{
				replacement.Dispose();
				_client = null;
				throw;
			}
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, cancellationToken);
			if (StateChanged is { } handler)
				await handler(ConnectionStates.Failed, cancellationToken);
		}
		finally
		{
			_connectionSync.Release();
		}
	}

	private async ValueTask RestoreSessionAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		await _sessionSync.WaitAsync(cancellationToken);
		try
		{
			if (_isPrivate && _section == PionexSections.Futures)
				await AuthenticateFuturesAsync(client, cancellationToken);

			PionexWsChannel[] channels;
			using (_sync.EnterScope())
				channels = [.. _channels];
			foreach (var channel in channels)
				await SendSubscriptionAsync(client, channel, true, cancellationToken);
		}
		finally
		{
			_sessionSync.Release();
		}
	}

	private async ValueTask AuthenticateFuturesAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_authentication = completion;
		try
		{
			await SendWireAsync(client, new PionexWsAuthCommand
			{
				Arguments = new()
				{
					ApiKey = _apiKey,
					Timestamp = timestamp,
					Signature = Sign(timestamp.ToString(CultureInfo.InvariantCulture) + "websocket_auth"),
				},
			}, cancellationToken);
			await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
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

	private ValueTask SendSubscriptionAsync(WebSocketClient client, PionexWsChannel channel,
		bool isSubscribe, CancellationToken cancellationToken)
		=> SendWireAsync(client, new PionexWsSubscriptionCommand
		{
			Operation = isSubscribe ? "SUBSCRIBE" : "UNSUBSCRIBE",
			Topic = channel.Topic,
			Symbol = channel.Symbol,
			Limit = isSubscribe ? channel.Limit : null,
		}, cancellationToken);

	private async ValueTask SendWireAsync<TCommand>(WebSocketClient client, TCommand command,
		CancellationToken cancellationToken)
		where TCommand : class
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextSendTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			await client.SendAsync(command, cancellationToken);
			_nextSendTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(210);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient source, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			var header = Deserialize<PionexWsHeader>(payload);
			if (header.Operation.EqualsIgnoreCase("PING"))
			{
				await SendWireAsync(source, new PionexWsHeartbeat
				{
					Operation = "PONG",
					Timestamp = header.Timestamp,
				}, cancellationToken);
				return;
			}

			if (header.Event.EqualsIgnoreCase("auth") || header.Operation.EqualsIgnoreCase("auth") ||
				header.Type.EqualsIgnoreCase("AUTHENTICATED"))
			{
				TaskCompletionSource<bool> completion;
				using (_sync.EnterScope())
					completion = _authentication;
				if (header.Code.IsEmpty() || header.Code == "0")
					completion?.TrySetResult(true);
				else
					completion?.TrySetException(CreateError(header));
				return;
			}

			if (!header.Code.IsEmpty() && header.Code != "0")
				throw CreateError(header);
			if (header.Type.EqualsIgnoreCase("SUBSCRIBED") ||
				header.Type.EqualsIgnoreCase("UNSUBSCRIBED"))
				return;

			switch (header.Topic?.ToUpperInvariant())
			{
				case "TRADE":
					if (TradesReceived is { } tradeHandler)
						await tradeHandler(Deserialize<PionexWsTradeMessage>(payload), cancellationToken);
					break;
				case "DEPTH":
				case "ORDERBOOK":
					if (DepthReceived is { } depthHandler)
						await depthHandler(Deserialize<PionexWsDepthMessage>(payload), cancellationToken);
					break;
				case "INDEX":
					if (IndexReceived is { } indexHandler)
						await indexHandler(Deserialize<PionexWsIndexMessage>(payload), cancellationToken);
					break;
				case "ORDER":
					if (OrderReceived is { } orderHandler && _section is PionexSections orderSection)
						await orderHandler(orderSection, Deserialize<PionexWsOrderMessage>(payload), cancellationToken);
					break;
				case "FILL":
					if (FillReceived is { } fillHandler && _section is PionexSections fillSection)
						await fillHandler(fillSection, Deserialize<PionexWsFillMessage>(payload), cancellationToken);
					break;
				case "BALANCE":
					if (BalanceReceived is { } balanceHandler && _section is PionexSections balanceSection)
						await balanceHandler(balanceSection, Deserialize<PionexWsBalanceMessage>(payload), cancellationToken);
					break;
				case "POSITION":
					if (PositionReceived is { } positionHandler)
						await positionHandler(Deserialize<PionexWsPositionMessage>(payload), cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or TimeoutException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private string CreateSpotPrivateEndpoint()
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
		var uri = new Uri(_endpoint, UriKind.Absolute);
		var canonical = "key=" + _apiKey + "&timestamp=" + timestamp;
		var signature = Sign(uri.AbsolutePath + "?" + canonical + "websocket_auth");
		return _endpoint + (_endpoint.Contains('?') ? "&" : "?") +
			"key=" + Uri.EscapeDataString(_apiKey) + "&timestamp=" + timestamp +
			"&signature=" + signature;
	}

	private string Sign(string payload)
	{
		byte[] hash;
		using (_signSync.EnterScope())
			hash = _hasher.ComputeHash(payload.UTF8());
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	private static T Deserialize<T>(string payload)
		where T : class
		=> JsonConvert.DeserializeObject<T>(payload)
			?? throw new InvalidDataException("Pionex WebSocket returned an empty JSON value.");

	private static Exception CreateError(PionexWsHeader header)
		=> new InvalidOperationException($"Pionex WebSocket error {header.Code}: {header.Message}.");

	private async ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
	{
		this.AddErrorLog(error);
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}
}
