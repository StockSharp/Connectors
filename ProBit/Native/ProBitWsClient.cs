namespace StockSharp.ProBit.Native;

readonly record struct ProBitWsChannel(string Channel, string MarketId, string Filter);

sealed class ProBitWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly Func<CancellationToken, ValueTask<string>> _tokenProvider;
	private readonly Lock _sync = new();
	private readonly HashSet<ProBitWsChannel> _channels = [];
	private readonly SemaphoreSlim _sessionSync = new(1, 1);
	private readonly SemaphoreSlim _connectionSync = new(1, 1);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private DateTime _nextSendTime;
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _authentication;
	private bool _isReady;

	public ProBitWsClient(string endpoint,
		Func<CancellationToken, ValueTask<string>> tokenProvider, WorkingTime workingTime)
	{
		_endpoint = NormalizeEndpoint(endpoint);
		_tokenProvider = tokenProvider;
		WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));

		if (tokenProvider is not null)
		{
			_channels.Add(new("balance", null, null));
			_channels.Add(new("open_order", null, null));
			_channels.Add(new("order_history", null, null));
			_channels.Add(new("trade_history", null, null));
		}
	}

	private WorkingTime WorkingTime { get; }

	public override string Name => nameof(ProBit) + "_WebSocket";

	public event Func<ProBitWsMarketDataMessage, CancellationToken, ValueTask> MarketDataReceived;
	public event Func<ProBitWsBalanceMessage, CancellationToken, ValueTask> BalanceReceived;
	public event Func<ProBitWsOrderMessage, CancellationToken, ValueTask> OpenOrdersReceived;
	public event Func<ProBitWsOrderMessage, CancellationToken, ValueTask> OrderHistoryReceived;
	public event Func<ProBitWsTradeMessage, CancellationToken, ValueTask> TradeHistoryReceived;
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
				throw new InvalidOperationException("ProBit WebSocket is already initialized.");
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

	public ValueTask SubscribeMarketAsync(string symbol, string filter,
		CancellationToken cancellationToken)
	{
		var channel = new ProBitWsChannel("marketdata",
			symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant(),
			filter.ThrowIfEmpty(nameof(filter)).ToLowerInvariant());
		bool shouldSend;
		using (_sync.EnterScope())
			shouldSend = _channels.Add(channel);
		return shouldSend && _client is { IsConnected: true } client
			? SendSubscriptionAsync(client, channel, true, cancellationToken)
			: default;
	}

	public ValueTask UnsubscribeMarketAsync(string symbol, string filter,
		CancellationToken cancellationToken)
	{
		var channel = new ProBitWsChannel("marketdata", symbol?.ToUpperInvariant(),
			filter?.ToLowerInvariant());
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
			socket.Options.SetRequestHeader("User-Agent", "StockSharp-ProBit-Connector/1.0");
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
			if (_tokenProvider is not null)
				await AuthenticateAsync(client, cancellationToken);

			ProBitWsChannel[] channels;
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

	private async ValueTask AuthenticateAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_authentication = completion;
		try
		{
			await SendWireAsync(client, new ProBitWsCommand
			{
				Type = "authorization",
				Token = await _tokenProvider(cancellationToken),
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

	private ValueTask SendSubscriptionAsync(WebSocketClient client, ProBitWsChannel channel,
		bool isSubscribe, CancellationToken cancellationToken)
		=> SendWireAsync(client, new ProBitWsCommand
		{
			Type = isSubscribe ? "subscribe" : "unsubscribe",
			Channel = channel.Channel,
			Interval = channel.Channel.EqualsIgnoreCase("marketdata") ? 500 : null,
			MarketId = channel.MarketId,
			Filter = channel.Filter.IsEmpty() ? null : [channel.Filter],
		}, cancellationToken);

	private async ValueTask SendWireAsync(WebSocketClient client, ProBitWsCommand command,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextSendTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			await client.SendAsync(command, cancellationToken);
			_nextSendTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
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
			var header = Deserialize<ProBitWsHeader>(payload);
			if (header.Type.EqualsIgnoreCase("authorization"))
			{
				TaskCompletionSource<bool> completion;
				using (_sync.EnterScope())
					completion = _authentication;
				if (header.Result.EqualsIgnoreCase("ok"))
					completion?.TrySetResult(true);
				else
					completion?.TrySetException(CreateError(header));
				return;
			}

			if (!header.ErrorCode.IsEmpty() || header.Result.EqualsIgnoreCase("error"))
				throw CreateError(header);

			switch (header.Channel?.ToLowerInvariant())
			{
				case "marketdata":
					if (MarketDataReceived is { } marketHandler)
						await marketHandler(Deserialize<ProBitWsMarketDataMessage>(payload), cancellationToken);
					break;
				case "balance":
					if (BalanceReceived is { } balanceHandler)
						await balanceHandler(Deserialize<ProBitWsBalanceMessage>(payload), cancellationToken);
					break;
				case "open_order":
					if (OpenOrdersReceived is { } openOrdersHandler)
						await openOrdersHandler(Deserialize<ProBitWsOrderMessage>(payload), cancellationToken);
					break;
				case "order_history":
					if (OrderHistoryReceived is { } orderHistoryHandler)
						await orderHistoryHandler(Deserialize<ProBitWsOrderMessage>(payload), cancellationToken);
					break;
				case "trade_history":
					if (TradeHistoryReceived is { } tradeHistoryHandler)
						await tradeHistoryHandler(Deserialize<ProBitWsTradeMessage>(payload), cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or TimeoutException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private static T Deserialize<T>(string payload)
		where T : class
		=> JsonConvert.DeserializeObject<T>(payload)
			?? throw new InvalidDataException("ProBit WebSocket returned an empty JSON value.");

	private static Exception CreateError(ProBitWsHeader header)
		=> new InvalidOperationException($"ProBit WebSocket error " +
			$"{header.ErrorCode.IsEmpty(header.Result)}: {header.Message}.");

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
