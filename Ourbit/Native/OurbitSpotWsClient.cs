namespace StockSharp.Ourbit.Native;

sealed class OurbitSpotWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _publicChannels = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _privateChannels = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _connectionSync = new(1, 1);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private WebSocketClient _publicClient;
	private WebSocketClient _privateClient;
	private string _listenKey;
	private long _requestId;
	private DateTime _nextSendTime;

	public OurbitSpotWsClient(string endpoint, WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/');
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Ourbit) + "_SpotWs";

	public event Func<string, OurbitSpotWsBookTicker, long, CancellationToken, ValueTask> TickerReceived;
	public event Func<string, OurbitSpotWsDepth, long, CancellationToken, ValueTask> DepthReceived;
	public event Func<string, OurbitSpotWsTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<string, OurbitSpotWsKline, long, CancellationToken, ValueTask> CandleReceived;
	public event Func<OurbitSpotWsPrivateAccount, CancellationToken, ValueTask> AccountReceived;
	public event Func<string, OurbitSpotWsPrivateOrder, long, CancellationToken, ValueTask> OrderReceived;
	public event Func<string, OurbitSpotWsPrivateFill, CancellationToken, ValueTask> FillReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_publicClient?.Dispose();
		_privateClient?.Dispose();
		_connectionSync.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await _connectionSync.WaitAsync(cancellationToken);
		try
		{
			if (_publicClient is not null)
				throw new InvalidOperationException("Ourbit spot WebSocket is already initialized.");
			var client = CreateClient(_endpoint, false);
			_publicClient = client;
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			_publicClient?.Dispose();
			_publicClient = null;
			throw;
		}
		finally
		{
			_connectionSync.Release();
		}
	}

	public async ValueTask ConnectPrivateAsync(string listenKey,
		CancellationToken cancellationToken)
	{
		listenKey.ThrowIfEmpty(nameof(listenKey));
		await _connectionSync.WaitAsync(cancellationToken);
		try
		{
			if (_privateClient is not null && _listenKey == listenKey)
				return;
			await DisconnectPrivateCoreAsync(cancellationToken);
			_listenKey = listenKey;
			var separator = _endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
			var client = CreateClient(_endpoint + separator + "listenKey=" +
				Uri.EscapeDataString(listenKey), true);
			_privateClient = client;
			await client.ConnectAsync(cancellationToken);
			await RestoreAsync(client, true, cancellationToken);
		}
		catch
		{
			_privateClient?.Dispose();
			_privateClient = null;
			_listenKey = null;
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
			await DisconnectPrivateCoreAsync(cancellationToken);
			var client = _publicClient;
			_publicClient = null;
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

	private async ValueTask DisconnectPrivateCoreAsync(CancellationToken cancellationToken)
	{
		var client = _privateClient;
		_privateClient = null;
		_listenKey = null;
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

	public ValueTask SubscribePublicAsync(string channel, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channel, true, false, cancellationToken);

	public ValueTask UnsubscribePublicAsync(string channel, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channel, false, false, cancellationToken);

	public ValueTask SubscribePrivateAsync(string channel, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channel, true, true, cancellationToken);

	public ValueTask UnsubscribePrivateAsync(string channel, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channel, false, true, cancellationToken);

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		var command = new OurbitSpotWsCommand { Method = "PING" };
		if (_publicClient is { IsConnected: true } publicClient)
			await SendAsync(publicClient, command, cancellationToken);
		if (_privateClient is { IsConnected: true } privateClient)
			await SendAsync(privateClient, command, cancellationToken);
	}

	private ValueTask ChangeSubscriptionAsync(string channel, bool isSubscribe, bool isPrivate,
		CancellationToken cancellationToken)
	{
		channel.ThrowIfEmpty(nameof(channel));
		bool shouldSend;
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			var channels = isPrivate ? _privateChannels : _publicChannels;
			if (isSubscribe && !isPrivate && channels.Count >= 30 && !channels.Contains(channel))
				throw new InvalidOperationException("Ourbit spot WebSocket supports at most 30 public channels per connection.");
			shouldSend = isSubscribe ? channels.Add(channel) : channels.Remove(channel);
			client = isPrivate ? _privateClient : _publicClient;
		}
		if (!shouldSend || client?.IsConnected != true)
			return default;
		return SendSubscriptionAsync(client, channel, isSubscribe, cancellationToken);
	}

	private WebSocketClient CreateClient(string url, bool isPrivate)
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			url,
			(state, token) => OnStateChangedAsync(client, state, isPrivate, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, isPrivate, token),
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
		bool isPrivate, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			try
			{
				await RestoreAsync(client, isPrivate, cancellationToken);
			}
			catch (Exception error)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
		}
		if (!isPrivate && StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask RestoreAsync(WebSocketClient client, bool isPrivate,
		CancellationToken cancellationToken)
	{
		string[] channels;
		using (_sync.EnterScope())
			channels = [.. (isPrivate ? _privateChannels : _publicChannels)];
		foreach (var channel in channels)
			await SendSubscriptionAsync(client, channel, true, cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client, string channel,
		bool isSubscribe, CancellationToken cancellationToken)
		=> SendAsync(client, new OurbitSpotWsCommand
		{
			Method = isSubscribe ? "SUBSCRIPTION" : "UNSUBSCRIPTION",
			Parameters = [channel],
			Id = Interlocked.Increment(ref _requestId),
		}, cancellationToken);

	private async ValueTask SendAsync(WebSocketClient client, OurbitSpotWsCommand command,
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
		bool isPrivate, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			if (payload.EqualsIgnoreCase("PING"))
			{
				await SendAsync(client, new OurbitSpotWsCommand { Method = "PONG" }, cancellationToken);
				return;
			}
			var header = Deserialize<OurbitSpotWsHeader>(payload);
			if (header?.Method.EqualsIgnoreCase("PING") == true || header?.Message.EqualsIgnoreCase("PING") == true)
			{
				await SendAsync(client, new OurbitSpotWsCommand { Method = "PONG" }, cancellationToken);
				return;
			}
			if (header?.Message.EqualsIgnoreCase("PONG") == true)
				return;
			if (header?.Code is int code && code != 0)
				throw new InvalidOperationException($"Ourbit spot WebSocket error {code}: {header.Message}");
			if (header?.Channel.IsEmpty() != false)
				return;

			var channel = header.Channel;
			if (channel.Contains("public.bookTicker", StringComparison.OrdinalIgnoreCase))
			{
				var data = Deserialize<OurbitSpotWsEnvelope<OurbitSpotWsBookTicker>>(payload);
				if (data?.Data is not null && TickerReceived is { } handler)
					await handler(data.Symbol, data.Data, data.Time, cancellationToken);
			}
			else if (channel.Contains("public.limit.depth", StringComparison.OrdinalIgnoreCase) ||
				channel.Contains("public.increase.depth", StringComparison.OrdinalIgnoreCase))
			{
				var data = Deserialize<OurbitSpotWsEnvelope<OurbitSpotWsDepth>>(payload);
				if (data?.Data is not null && DepthReceived is { } handler)
					await handler(data.Symbol, data.Data, data.Time, cancellationToken);
			}
			else if (channel.Contains("public.deals", StringComparison.OrdinalIgnoreCase))
			{
				var data = Deserialize<OurbitSpotWsEnvelope<OurbitSpotWsTrades>>(payload);
				if (TradeReceived is { } handler)
				{
					foreach (var trade in data?.Data?.Trades ?? [])
						await handler(data.Symbol, trade, cancellationToken);
				}
			}
			else if (channel.Contains("public.kline", StringComparison.OrdinalIgnoreCase))
			{
				var data = Deserialize<OurbitSpotWsEnvelope<OurbitSpotWsKlineContainer>>(payload);
				if (data?.Data?.Candle is not null && CandleReceived is { } handler)
					await handler(data.Symbol, data.Data.Candle, data.Time, cancellationToken);
			}
			else if (isPrivate && channel.Contains("private.account", StringComparison.OrdinalIgnoreCase))
			{
				var data = Deserialize<OurbitSpotWsEnvelope<OurbitSpotWsPrivateAccount>>(payload);
				if (data?.Data is not null && AccountReceived is { } handler)
					await handler(data.Data, cancellationToken);
			}
			else if (isPrivate && channel.Contains("private.orders", StringComparison.OrdinalIgnoreCase))
			{
				var data = Deserialize<OurbitSpotWsEnvelope<OurbitSpotWsPrivateOrder>>(payload);
				if (data?.Data is not null && OrderReceived is { } handler)
					await handler(data.Symbol, data.Data, data.Time, cancellationToken);
			}
			else if (isPrivate && channel.Contains("private.deals", StringComparison.OrdinalIgnoreCase))
			{
				var data = Deserialize<OurbitSpotWsEnvelope<OurbitSpotWsPrivateFill>>(payload);
				if (data?.Data is not null && FillReceived is { } handler)
					await handler(data.Symbol, data.Data, cancellationToken);
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
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
