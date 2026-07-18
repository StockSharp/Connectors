namespace StockSharp.Xt.Native;

readonly record struct XtWsChannel(string Topic, string Symbol, int? Limit);

sealed class XtWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly XtSections _section;
	private readonly bool _isPrivate;
	private readonly XtRestClient _restClient;
	private readonly Lock _sync = new();
	private readonly HashSet<XtWsChannel> _channels = [];
	private readonly SemaphoreSlim _sessionSync = new(1, 1);
	private readonly SemaphoreSlim _connectionSync = new(1, 1);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private DateTime _nextSendTime;
	private DateTime _tokenTime;
	private string _listenKey;
	private WebSocketClient _client;
	private bool _isReady;

	public XtWsClient(string endpoint, XtSections section, bool isPrivate,
		XtRestClient restClient, WorkingTime workingTime)
	{
		_endpoint = NormalizeEndpoint(endpoint);
		_section = section;
		_isPrivate = isPrivate;
		_restClient = restClient;
		WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));

		if (isPrivate && (restClient is null || !restClient.IsCredentialsAvailable))
			throw new ArgumentException(
				"XT.COM private WebSocket requires an authenticated REST client.");

		if (isPrivate)
		{
			_channels.Add(new("BALANCE", null, null));
			_channels.Add(new("ORDER", null, null));
			_channels.Add(new("TRADE", null, null));
			if (section == XtSections.Futures)
				_channels.Add(new("POSITION", null, null));
		}
	}

	private WorkingTime WorkingTime { get; }

	public override string Name => nameof(Xt) + "_" + _section +
		(_isPrivate ? "_UserWs" : "_MarketWs");

	public event Func<XtSections, XtWsTradeMessage, CancellationToken, ValueTask> TradesReceived;
	public event Func<XtSections, XtWsDepthMessage, CancellationToken, ValueTask> DepthReceived;
	public event Func<XtSections, XtWsIndexMessage, CancellationToken, ValueTask> IndexReceived;
	public event Func<XtSections, XtWsOrderMessage, CancellationToken, ValueTask> OrderReceived;
	public event Func<XtSections, XtWsFillMessage, CancellationToken, ValueTask> FillReceived;
	public event Func<XtSections, XtWsBalanceMessage, CancellationToken, ValueTask> BalanceReceived;
	public event Func<XtWsPositionMessage, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_isReady = false;
		_client?.Dispose();
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
				throw new InvalidOperationException("XT.COM WebSocket is already initialized.");
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
			_listenKey = null;
			_tokenTime = default;
		}
	}

	public ValueTask SubscribeAsync(string topic, string symbol, int? limit,
		CancellationToken cancellationToken)
	{
		var channel = new XtWsChannel(topic.ThrowIfEmpty(nameof(topic)).ToUpperInvariant(),
			symbol?.ToUpperInvariant(), limit);
		bool shouldSend;
		using (_sync.EnterScope())
			shouldSend = _channels.Add(channel);
		return shouldSend && _client is { IsConnected: true } client
			? SendSubscriptionAsync(client, channel, true, null, cancellationToken)
			: default;
	}

	public ValueTask UnsubscribeAsync(string topic, string symbol, int? limit,
		CancellationToken cancellationToken)
	{
		var channel = new XtWsChannel(topic?.ToUpperInvariant(), symbol?.ToUpperInvariant(), limit);
		bool shouldSend;
		using (_sync.EnterScope())
			shouldSend = _channels.Remove(channel);
		return shouldSend && _client is { IsConnected: true } client
			? SendSubscriptionAsync(client, channel, false, null, cancellationToken)
			: default;
	}

	public async ValueTask SendHeartbeatAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			return;
		await SendTextAsync(client, "ping", cancellationToken);
		if (!_isPrivate)
			return;

		var refreshAfter = _section == XtSections.Futures
			? TimeSpan.FromHours(6)
			: TimeSpan.FromHours(24);
		if (_tokenTime != default && DateTime.UtcNow - _tokenTime < refreshAfter)
			return;
		await RefreshTokenAsync(client, cancellationToken);
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
			ReconnectAttempts = 5,
			WorkingTime = WorkingTime,
			DisableAutoResend = true,
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
		client.Init += static socket =>
			socket.Options.SetRequestHeader("User-Agent", "StockSharp-XT-Connector/1.0");
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

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask RestoreSessionAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		await _sessionSync.WaitAsync(cancellationToken);
		try
		{
			if (_isPrivate)
			{
				_listenKey = await GetListenKeyAsync(cancellationToken);
				_tokenTime = DateTime.UtcNow;
			}

			XtWsChannel[] channels;
			using (_sync.EnterScope())
				channels = [.. _channels];
			foreach (var channel in channels)
				await SendSubscriptionAsync(client, channel, true, null, cancellationToken);
		}
		finally
		{
			_sessionSync.Release();
		}
	}

	private async ValueTask RefreshTokenAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		await _sessionSync.WaitAsync(cancellationToken);
		try
		{
			var previous = _listenKey;
			var current = await GetListenKeyAsync(cancellationToken);
			_tokenTime = DateTime.UtcNow;
			if (current.EqualsIgnoreCase(previous))
				return;

			XtWsChannel[] channels;
			using (_sync.EnterScope())
				channels = [.. _channels];
			foreach (var channel in channels)
				await SendSubscriptionAsync(client, channel, false, previous, cancellationToken);
			_listenKey = current;
			foreach (var channel in channels)
				await SendSubscriptionAsync(client, channel, true, current, cancellationToken);
		}
		finally
		{
			_sessionSync.Release();
		}
	}

	private async ValueTask<string> GetListenKeyAsync(CancellationToken cancellationToken)
	{
		var token = _section == XtSections.Spot
			? await _restClient.GetSpotWsTokenAsync(cancellationToken)
			: await _restClient.GetFuturesListenKeyAsync(cancellationToken);
		return token.ThrowIfEmpty("listenKey");
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client, XtWsChannel channel,
		bool isSubscribe, string listenKey, CancellationToken cancellationToken)
	{
		listenKey ??= _listenKey;
		var parameter = ToWireChannel(channel, listenKey);
		return SendWireAsync(client, new XtWsSubscriptionCommand
		{
			Method = _section == XtSections.Spot
				? isSubscribe ? "subscribe" : "unsubscribe"
				: isSubscribe ? "SUBSCRIBE" : "UNSUBSCRIBE",
			Parameters = [parameter],
			ListenKey = _isPrivate && _section == XtSections.Spot ? listenKey : null,
			Id = Guid.NewGuid().ToString("N")[..16],
		}, cancellationToken);
	}

	private string ToWireChannel(XtWsChannel channel, string listenKey)
	{
		if (_isPrivate)
			return _section == XtSections.Spot
				? channel.Topic.ToLowerInvariant()
				: channel.Topic.ToLowerInvariant() + "@" + listenKey;

		var symbol = channel.Symbol.ThrowIfEmpty(nameof(channel.Symbol)).ToLowerInvariant();
		return channel.Topic switch
		{
			"TRADE" => "trade@" + symbol,
			"DEPTH" => _section == XtSections.Spot
				? $"depth@{symbol},{NormalizeDepth(channel.Limit)}"
				: $"depth@{symbol},{NormalizeDepth(channel.Limit)},1000ms",
			"INDEX" when _section == XtSections.Futures => "agg_ticker@" + symbol,
			_ => throw new NotSupportedException(
				$"XT.COM {_section} WebSocket topic {channel.Topic} is not supported."),
		};
	}

	private async ValueTask SendWireAsync<TCommand>(WebSocketClient client, TCommand command,
		CancellationToken cancellationToken)
		where TCommand : class
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await WaitForSendAsync(cancellationToken);
			await client.SendAsync(command, cancellationToken);
			_nextSendTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(60);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask SendTextAsync(WebSocketClient client, string value,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await WaitForSendAsync(cancellationToken);
			await client.SendAsync(value, cancellationToken);
			_nextSendTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(60);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask WaitForSendAsync(CancellationToken cancellationToken)
	{
		var delay = _nextSendTime - DateTime.UtcNow;
		if (delay > TimeSpan.Zero)
			await Task.Delay(delay, cancellationToken);
	}

	private async ValueTask OnProcessAsync(WebSocketClient source, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong"))
			return;
		if (payload.EqualsIgnoreCase("ping"))
		{
			await SendTextAsync(source, "pong", cancellationToken);
			return;
		}

		try
		{
			var header = Deserialize<XtWsResponse>(payload);
			if (header.Code is int code && code != 0)
				throw new InvalidOperationException(
					$"XT.COM WebSocket error {code}: {header.Message}.");
			if (header.Topic.IsEmpty())
				return;

			switch (header.Topic.ToUpperInvariant())
			{
				case "TRADE":
					if (_isPrivate)
						await ProcessPrivateFillAsync(payload, cancellationToken);
					else if (TradesReceived is { } tradeHandler)
					{
						var envelope = Deserialize<XtWsEnvelope<XtMarketTrade>>(payload);
						await tradeHandler(_section, new()
						{
							Symbol = envelope.Data?.Symbol,
							Data = envelope.Data is null ? [] : [envelope.Data],
						}, cancellationToken);
					}
					break;

				case "DEPTH":
					if (!_isPrivate && DepthReceived is { } depthHandler)
					{
						var envelope = Deserialize<XtWsEnvelope<XtDepthData>>(payload);
						await depthHandler(_section, new()
						{
							Symbol = envelope.Data?.Symbol,
							Timestamp = envelope.Data?.UpdateTime ?? 0,
							Data = envelope.Data,
						}, cancellationToken);
					}
					break;

				case "AGG_TICKER":
					if (!_isPrivate && IndexReceived is { } indexHandler)
					{
						var envelope = Deserialize<XtWsEnvelope<XtTicker>>(payload);
						await indexHandler(_section, new()
						{
							Symbol = envelope.Data?.Symbol,
							Data = envelope.Data is null ? [] : [new XtWsIndex
							{
								Symbol = envelope.Data.Symbol,
								IndexPrice = envelope.Data.IndexPrice,
								MarkPrice = envelope.Data.MarkPrice,
								UpdateTime = envelope.Data.Time,
							}],
						}, cancellationToken);
					}
					break;

				case "ORDER":
					if (_isPrivate)
						await ProcessPrivateOrderAsync(payload, cancellationToken);
					break;

				case "BALANCE":
					if (_isPrivate)
						await ProcessPrivateBalanceAsync(payload, cancellationToken);
					break;

				case "POSITION":
					if (_isPrivate && _section == XtSections.Futures &&
						PositionReceived is { } positionHandler)
					{
						var envelope = Deserialize<XtWsEnvelope<XtPosition>>(payload);
						await positionHandler(new()
						{
							Symbol = envelope.Data?.Symbol,
							Data = envelope.Data,
						}, cancellationToken);
					}
					break;
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessPrivateOrderAsync(string payload,
		CancellationToken cancellationToken)
	{
		if (OrderReceived is not { } handler)
			return;
		XtWsOrderMessage message;
		if (_section == XtSections.Spot)
		{
			var envelope = Deserialize<XtWsEnvelope<XtSpotWsOrder>>(payload);
			var data = envelope.Data;
			message = new()
			{
				Symbol = data?.Symbol,
				Timestamp = data?.UpdateTime ?? 0,
				Data = data is null ? null : new XtOrder
				{
					Symbol = data.Symbol,
					OrderId = data.OrderId,
					ClientOrderId = data.ClientOrderId,
					SpotSide = data.Side,
					SpotType = data.Type,
					OriginalSize = data.OriginalQuantity,
					OriginalAmount = data.OriginalQuoteQuantity,
					FilledSize = data.ExecutedQuantity,
					Price = data.Price,
					AveragePrice = data.AveragePrice,
					Fee = data.Fee,
					Status = data.State,
					SpotCreateTime = data.CreateTime,
					UpdateTime = data.UpdateTime,
				},
			};
		}
		else
		{
			var envelope = Deserialize<XtWsEnvelope<XtFuturesWsOrder>>(payload);
			var data = envelope.Data;
			message = new()
			{
				Symbol = data?.Symbol,
				Timestamp = data?.CreateTime ?? 0,
				Data = data is null ? null : new XtOrder
				{
					Symbol = data.Symbol,
					OrderId = data.OrderId,
					ClientOrderId = data.ClientOrderId,
					FuturesSide = data.Side,
					FuturesType = data.Type,
					TimeInForce = data.TimeInForce,
					PositionSide = data.PositionSide,
					OriginalSize = data.OriginalQuantity,
					FilledSize = data.ExecutedQuantity,
					Price = data.Price,
					AveragePrice = data.AveragePrice,
					Status = data.State,
					FuturesCreateTime = data.CreateTime,
				},
			};
		}
		await handler(_section, message, cancellationToken);
	}

	private async ValueTask ProcessPrivateFillAsync(string payload,
		CancellationToken cancellationToken)
	{
		if (FillReceived is not { } handler)
			return;
		XtWsFillMessage message;
		if (_section == XtSections.Spot)
		{
			var envelope = Deserialize<XtWsEnvelope<XtSpotWsFill>>(payload);
			var data = envelope.Data;
			message = new()
			{
				Symbol = data?.Symbol,
				Timestamp = data?.Timestamp ?? 0,
				Data = data is null ? null : new XtFill
				{
					SpotId = data.TradeId,
					OrderId = data.OrderId,
					Symbol = data.Symbol,
					SideAlias = data.IsBuyerMaker ? "SELL" : "BUY",
					Price = data.Price,
					Size = data.Quantity,
					SpotTimestamp = data.Timestamp,
				},
			};
		}
		else
		{
			var envelope = Deserialize<XtWsEnvelope<XtFuturesWsFill>>(payload);
			var data = envelope.Data;
			message = new()
			{
				Symbol = data?.Symbol,
				Timestamp = data?.Timestamp ?? 0,
				Data = data is null ? null : new XtFill
				{
					OrderId = data.OrderId,
					Symbol = data.Symbol,
					SideAlias = data.Side,
					Price = data.Price,
					Size = data.Quantity,
					Fee = data.Fee,
					FuturesTimestamp = data.Timestamp,
				},
			};
		}
		await handler(_section, message, cancellationToken);
	}

	private async ValueTask ProcessPrivateBalanceAsync(string payload,
		CancellationToken cancellationToken)
	{
		if (BalanceReceived is not { } handler)
			return;
		XtWsBalanceMessage message;
		if (_section == XtSections.Spot)
		{
			var envelope = Deserialize<XtWsEnvelope<XtSpotWsBalance>>(payload);
			var data = envelope.Data;
			var available = (data?.Total.ToDecimal() ?? 0m) - (data?.Frozen.ToDecimal() ?? 0m);
			message = new()
			{
				Timestamp = data?.Timestamp ?? 0,
				Data = data is null ? null : new XtWsBalanceData
				{
					Type = data.BusinessType,
					Symbol = data.Symbol,
					Timestamp = data.Timestamp,
					Balances = [new XtBalance
					{
						SpotCoin = data.Coin,
						SpotAvailable = available.ToWire(),
						SpotFrozen = data.Frozen,
						Total = data.Total,
					}],
				},
			};
		}
		else
		{
			var envelope = Deserialize<XtWsEnvelope<XtFuturesWsBalance>>(payload);
			var data = envelope.Data;
			message = new()
			{
				Data = data is null ? null : new XtWsBalanceData
				{
					Type = "CROSS",
					Balances = [new XtBalance
					{
						FuturesCoin = data.Coin,
						FuturesWallet = data.WalletBalance,
						FuturesFrozen = data.Frozen,
					}],
				},
			};
		}
		await handler(_section, message, cancellationToken);
	}

	private static T Deserialize<T>(string payload)
		where T : class
		=> JsonConvert.DeserializeObject<T>(payload)
			?? throw new InvalidDataException("XT.COM WebSocket returned an empty JSON value.");

	private async ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
	{
		this.AddErrorLog(error);
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static int NormalizeDepth(int? depth)
		=> depth switch
		{
			null or <= 5 => 5,
			<= 10 => 10,
			<= 20 => 20,
			_ => 50,
		};

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}
}
