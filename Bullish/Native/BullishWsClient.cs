namespace StockSharp.Bullish.Native;

sealed class BullishWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly BullishWsKinds _kind;
	private readonly BullishRestClient _restClient;
	private readonly WorkingTime _workingTime;
	private readonly Lock _sync = new();
	private readonly HashSet<BullishWsChannel> _channels = [];
	private readonly SemaphoreSlim _connectionSync = new(1, 1);
	private readonly SemaphoreSlim _sessionSync = new(1, 1);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private WebSocketClient _client;
	private DateTime _nextSendTime;
	private DateTime _tokenTime;
	private long _requestId;
	private bool _isReady;

	public BullishWsClient(string endpoint, BullishWsKinds kind, BullishRestClient restClient,
		WorkingTime workingTime, IEnumerable<string> tradingAccountIds = null)
	{
		_kind = kind;
		_restClient = restClient;
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + (kind switch
		{
			BullishWsKinds.OrderBook => "/trading-api/v1/market-data/orderbook",
			BullishWsKinds.Trades => "/trading-api/v1/market-data/trades",
			BullishWsKinds.Tick => "/trading-api/v1/market-data/tick",
			BullishWsKinds.Private => "/trading-api/v1/private-data",
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
		});

		if (kind != BullishWsKinds.Private)
			return;
		if (restClient?.IsCredentialsAvailable != true)
			throw new ArgumentException("Bullish private WebSocket requires credentials.");
		foreach (var accountId in tradingAccountIds ?? [])
		{
			if (accountId.IsEmpty())
				continue;
			_channels.Add(new("orders", null, accountId));
			_channels.Add(new("trades", null, accountId));
			_channels.Add(new("assetAccounts", null, accountId));
			_channels.Add(new("tradingAccounts", null, accountId));
			_channels.Add(new("derivativesPositionsV2", null, accountId));
		}
	}

	public override string Name => nameof(Bullish) + "_" + _kind + "Ws";

	public event Func<string, BullishWsLevel2Data, CancellationToken, ValueTask> DepthReceived;
	public event Func<BullishWsTradesData, CancellationToken, ValueTask> TradesReceived;
	public event Func<BullishTick, CancellationToken, ValueTask> TickReceived;
	public event Func<BullishOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<BullishTrade, CancellationToken, ValueTask> FillReceived;
	public event Func<BullishAssetAccount, CancellationToken, ValueTask> AssetAccountReceived;
	public event Func<BullishTradingAccount, CancellationToken, ValueTask> TradingAccountReceived;
	public event Func<BullishDerivativePosition, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_isReady = false;
		_client?.Dispose();
		_connectionSync.Dispose();
		_sessionSync.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await _connectionSync.WaitAsync(cancellationToken);
		try
		{
			if (_client is not null)
				throw new InvalidOperationException("Bullish WebSocket is already initialized.");
			var client = await CreateClientAsync(cancellationToken);
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
		await _connectionSync.WaitAsync(cancellationToken);
		try
		{
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
		finally
		{
			_connectionSync.Release();
		}
	}

	public ValueTask SubscribeAsync(string topic, string symbol,
		CancellationToken cancellationToken)
	{
		if (_kind == BullishWsKinds.Private)
			throw new InvalidOperationException("Private Bullish subscriptions are account-scoped.");
		var channel = new BullishWsChannel(topic.ThrowIfEmpty(nameof(topic)),
			symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant(), null);
		bool shouldSend;
		using (_sync.EnterScope())
			shouldSend = _channels.Add(channel);
		return shouldSend && _client is { IsConnected: true } client
			? SendSubscriptionAsync(client, channel, true, cancellationToken)
			: default;
	}

	public ValueTask UnsubscribeAsync(string topic, string symbol,
		CancellationToken cancellationToken)
	{
		if (_kind == BullishWsKinds.Private)
			return default;
		var channel = new BullishWsChannel(topic, symbol?.ToUpperInvariant(), null);
		bool shouldSend;
		using (_sync.EnterScope())
			shouldSend = _channels.Remove(channel);
		return shouldSend && _client is { IsConnected: true } client
			? SendSubscriptionAsync(client, channel, false, cancellationToken)
			: default;
	}

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		if (_kind == BullishWsKinds.Private && _tokenTime != default &&
			DateTime.UtcNow - _tokenTime >= TimeSpan.FromHours(23))
		{
			await ReconnectForTokenAsync(cancellationToken);
			return;
		}

		var client = _client;
		if (client?.IsConnected != true)
			return;
		await SendWireAsync(client, new BullishWsCommand
		{
			Method = "keepalivePing",
			Parameters = new BullishWsEmptyParameters(),
			Id = NextRequestId(),
		}, cancellationToken);
	}

	private async ValueTask<WebSocketClient> CreateClientAsync(
		CancellationToken cancellationToken)
	{
		var jwt = _kind == BullishWsKinds.Private
			? await _restClient.GetJwtAsync(cancellationToken)
			: null;
		if (!jwt.IsEmpty())
			_tokenTime = DateTime.UtcNow;

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
			ReconnectAttempts = 5,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
		client.Init += socket =>
		{
			socket.Options.SetRequestHeader("User-Agent", "StockSharp-Bullish-Connector/1.0");
			if (!jwt.IsEmpty())
				socket.Options.SetRequestHeader("Cookie", $"JWT_COOKIE={jwt}");
		};
		return client;
	}

	private async ValueTask ReconnectForTokenAsync(CancellationToken cancellationToken)
	{
		await _connectionSync.WaitAsync(cancellationToken);
		try
		{
			var previous = _client;
			_client = null;
			if (previous is not null)
			{
				try
				{
					if (previous.IsConnected)
						await previous.DisconnectAsync(cancellationToken);
				}
				finally
				{
					previous.Dispose();
				}
			}

			var client = await CreateClientAsync(cancellationToken);
			_client = client;
			await client.ConnectAsync(cancellationToken);
			await RestoreSessionAsync(client, cancellationToken);
			_isReady = true;
		}
		finally
		{
			_connectionSync.Release();
		}
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

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask RestoreSessionAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		await _sessionSync.WaitAsync(cancellationToken);
		try
		{
			BullishWsChannel[] channels;
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

	private ValueTask SendSubscriptionAsync(WebSocketClient client, BullishWsChannel channel,
		bool isSubscribe, CancellationToken cancellationToken)
		=> SendWireAsync(client, new BullishWsCommand
		{
			Method = isSubscribe ? "subscribe" : "unsubscribe",
			Parameters = _kind == BullishWsKinds.Private
				? new BullishWsPrivateParameters
				{
					Topic = channel.Topic,
					TradingAccountId = channel.TradingAccountId,
				}
				: new BullishWsMarketParameters
				{
					Topic = channel.Topic,
					Symbol = channel.Symbol,
				},
			Id = NextRequestId(),
		}, cancellationToken);

	private string NextRequestId()
		=> Interlocked.Increment(ref _requestId).ToString(CultureInfo.InvariantCulture);

	private async ValueTask SendWireAsync(WebSocketClient client, BullishWsCommand command,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextSendTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			await client.SendAsync(command, cancellationToken);
			_nextSendTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(10);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient source, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		_ = source;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			var header = Deserialize<BullishWsHeader>(payload);
			if (header?.Error is not null)
				throw new InvalidOperationException(
					$"Bullish WebSocket {header.Error.Code}: {header.Error.ErrorCode} {header.Error.ErrorCodeName}".Trim());
			if (header?.DataType.IsEmpty() != false)
				return;

			switch (_kind)
			{
				case BullishWsKinds.OrderBook:
					if (DepthReceived is { } depthHandler)
					{
						var data = Deserialize<BullishWsDataMessage<BullishWsLevel2Data>>(payload);
						if (data?.Data is not null)
							await depthHandler(data.Type, data.Data, cancellationToken);
					}
					break;

				case BullishWsKinds.Trades:
					if (TradesReceived is { } tradeHandler)
					{
						var data = Deserialize<BullishWsDataMessage<BullishWsTradesData>>(payload);
						if (data?.Data is not null)
							await tradeHandler(data.Data, cancellationToken);
					}
					break;

				case BullishWsKinds.Tick:
					if (TickReceived is { } tickHandler)
					{
						var data = Deserialize<BullishWsDataMessage<BullishTick>>(payload);
						if (data?.Data is not null)
							await tickHandler(data.Data, cancellationToken);
					}
					break;

				case BullishWsKinds.Private:
					await ProcessPrivateAsync(header, payload, cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or TimeoutException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessPrivateAsync(BullishWsHeader header, string payload,
		CancellationToken cancellationToken)
	{
		var isSnapshot = header.Type.EqualsIgnoreCase("snapshot");
		switch (header.DataType)
		{
			case "V1TAOrder":
				if (OrderReceived is { } orderHandler)
				{
					foreach (var item in ReadPrivateItems<BullishOrder>(payload, isSnapshot))
						await orderHandler(item, cancellationToken);
				}
				break;

			case "V1TATrade":
				if (FillReceived is { } fillHandler)
				{
					foreach (var item in ReadPrivateItems<BullishTrade>(payload, isSnapshot))
						await fillHandler(item, cancellationToken);
				}
				break;

			case "V1TAAssetAccount":
				if (AssetAccountReceived is { } assetHandler)
				{
					foreach (var item in ReadPrivateItems<BullishAssetAccount>(payload, isSnapshot))
						await assetHandler(item, cancellationToken);
				}
				break;

			case "V1TATradingAccount":
				if (TradingAccountReceived is { } accountHandler)
				{
					foreach (var item in ReadPrivateItems<BullishTradingAccount>(payload, isSnapshot))
						await accountHandler(item, cancellationToken);
				}
				break;

			case "V1TADerivativesPosition":
				if (PositionReceived is { } positionHandler)
				{
					foreach (var item in ReadPrivateItems<BullishDerivativePosition>(payload, isSnapshot))
						await positionHandler(item, cancellationToken);
				}
				break;
		}
	}

	private TItem[] ReadPrivateItems<TItem>(string payload, bool isSnapshot)
		where TItem : class
	{
		if (isSnapshot)
			return Deserialize<BullishWsDataMessage<TItem[]>>(payload)?.Data ?? [];
		var item = Deserialize<BullishWsDataMessage<TItem>>(payload)?.Data;
		return item is null ? [] : [item];
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
