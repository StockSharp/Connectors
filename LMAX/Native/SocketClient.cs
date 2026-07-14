namespace StockSharp.LMAX.Native;

using Ecng.ComponentModel;

class SocketClient : BaseLogReceiver
{
	private readonly string _marketDataWsUrl;
	private readonly string _accountWsUrl;
	private readonly Func<SecureString> _getToken;

	private WebSocketClient _marketDataClient;
	private WebSocketClient _accountClient;

	private readonly SynchronizedSet<string> _subscribedOrderBooks = [];
	private readonly SynchronizedSet<string> _subscribedTickers = [];
	private readonly SynchronizedSet<string> _subscribedTrades = [];

	// Events for market data
	public event Func<WsOrderBookMessage, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<WsTickerMessage, CancellationToken, ValueTask> TickerReceived;
	public event Func<WsTradeMessage, CancellationToken, ValueTask> TradeReceived;

	// Events for account data
	public event Func<WsOrderMessage, CancellationToken, ValueTask> OrderReceived;
	public event Func<WsExecutionMessage, CancellationToken, ValueTask> ExecutionReceived;
	public event Func<WsPositionMessage, CancellationToken, ValueTask> PositionReceived;
	public event Func<WsWalletMessage, CancellationToken, ValueTask> WalletReceived;
	public event Func<WsRejectionMessage, CancellationToken, ValueTask> RejectionReceived;

	// Error and connection events
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public SocketClient(string marketDataWsUrl, string accountWsUrl, Func<SecureString> getToken, WorkingTime workingTime)
	{
		_marketDataWsUrl = marketDataWsUrl ?? throw new ArgumentNullException(nameof(marketDataWsUrl));
		_accountWsUrl = accountWsUrl ?? throw new ArgumentNullException(nameof(accountWsUrl));
		_getToken = getToken ?? throw new ArgumentNullException(nameof(getToken));
		_marketDataClient = new WebSocketClient(
			_marketDataWsUrl,
			(state, token) => OnMarketDataStateChanged(state, token),
			(error, token) =>
			{
				this.AddErrorLog(error);
				if (Error is { } handler)
					return handler(error, token);
				return default;
			},
			ProcessMarketDataMessage,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = -1,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};

		_accountClient = new WebSocketClient(
			_accountWsUrl,
			(state, token) => OnAccountStateChanged(state, token),
			(error, token) =>
			{
				this.AddErrorLog(error);
				if (Error is { } handler)
					return handler(error, token);
				return default;
			},
			ProcessAccountMessage,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = -1,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};

		_accountClient.Init += ws =>
		{
			var token = _getToken();
			ws.Options.SetRequestHeader("Authorization", $"Bearer {token.UnSecure()}");
		};

		_accountClient.PostConnect += OnAccountPostConnect;
	}

	// to get readable name after obfuscation
	public override string Name => nameof(LMAX) + "_" + nameof(SocketClient);

	public bool IsConnected =>
		_marketDataClient?.IsConnected == true &&
		_accountClient?.IsConnected == true;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await _marketDataClient.ConnectAsync(cancellationToken);
		this.AddInfoLog("Connected to market data WebSocket");

		await _accountClient.ConnectAsync(cancellationToken);
		this.AddInfoLog("Connected to account WebSocket");
	}

	public void Disconnect()
	{
		_marketDataClient?.Disconnect();
		_accountClient?.Disconnect();

		_subscribedOrderBooks.Clear();
		_subscribedTickers.Clear();
		_subscribedTrades.Clear();

		this.AddInfoLog("Disconnected");
	}

	// Market data subscriptions

	public ValueTask SubscribeOrderBookAsync(string instrumentId, CancellationToken cancellationToken)
	{
		if (!_subscribedOrderBooks.TryAdd(instrumentId))
			return default;

		return SubscribeAsync(_marketDataClient, WsChannels.OrderBook, [instrumentId], cancellationToken);
	}

	public ValueTask UnsubscribeOrderBookAsync(string instrumentId, CancellationToken cancellationToken)
	{
		if (!_subscribedOrderBooks.Remove(instrumentId))
			return default;

		return UnsubscribeAsync(_marketDataClient, WsChannels.OrderBook, [instrumentId], cancellationToken);
	}

	public ValueTask SubscribeTickerAsync(string instrumentId, CancellationToken cancellationToken)
	{
		if (!_subscribedTickers.TryAdd(instrumentId))
			return default;

		return SubscribeAsync(_marketDataClient, WsChannels.Ticker, [instrumentId], cancellationToken);
	}

	public ValueTask UnsubscribeTickerAsync(string instrumentId, CancellationToken cancellationToken)
	{
		if (!_subscribedTickers.Remove(instrumentId))
			return default;

		return UnsubscribeAsync(_marketDataClient, WsChannels.Ticker, [instrumentId], cancellationToken);
	}

	public ValueTask SubscribeTradesAsync(string instrumentId, CancellationToken cancellationToken)
	{
		if (!_subscribedTrades.TryAdd(instrumentId))
			return default;

		return SubscribeAsync(_marketDataClient, WsChannels.Trade, [instrumentId], cancellationToken);
	}

	public ValueTask UnsubscribeTradesAsync(string instrumentId, CancellationToken cancellationToken)
	{
		if (!_subscribedTrades.Remove(instrumentId))
			return default;

		return UnsubscribeAsync(_marketDataClient, WsChannels.Trade, [instrumentId], cancellationToken);
	}

	// Private methods

	private ValueTask OnMarketDataStateChanged(ConnectionStates state, CancellationToken cancellationToken)
	{
		this.AddInfoLog("MarketData WebSocket state: {0}", state);

		if (StateChanged is { } handler)
			return handler(state, cancellationToken);

		return default;
	}

	private ValueTask OnAccountStateChanged(ConnectionStates state, CancellationToken cancellationToken)
	{
		this.AddInfoLog("Account WebSocket state: {0}", state);

		if (StateChanged is { } handler)
			return handler(state, cancellationToken);

		return default;
	}

	private async ValueTask OnAccountPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		// Subscribe to account channels after connection
		await SubscribeAccountChannelsAsync(cancellationToken);
	}

	private ValueTask SubscribeAccountChannelsAsync(CancellationToken cancellationToken)
	{
		var request = new WsSubscribeRequest
		{
			Channels =
			[
				new WsChannel { Name = WsChannels.Order, Instruments = [] },
				new WsChannel { Name = WsChannels.Execution, Instruments = [] },
				new WsChannel { Name = WsChannels.Position, Instruments = [] },
				new WsChannel { Name = WsChannels.Wallet, Instruments = [] },
				new WsChannel { Name = WsChannels.Rejection, Instruments = [] },
			]
		};

		return _accountClient.SendAsync(request, cancellationToken);
	}

	private static ValueTask SubscribeAsync(WebSocketClient client, string channel, string[] instruments, CancellationToken cancellationToken)
	{
		var request = new WsSubscribeRequest
		{
			Channels =
			[
				new WsChannel { Name = channel, Instruments = instruments }
			]
		};

		return client.SendAsync(request, cancellationToken);
	}

	private static ValueTask UnsubscribeAsync(WebSocketClient client, string channel, string[] instruments, CancellationToken cancellationToken)
	{
		var request = new WsUnsubscribeRequest
		{
			Channels =
			[
				new WsChannel { Name = channel, Instruments = instruments }
			]
		};

		return client.SendAsync(request, cancellationToken);
	}

	private async ValueTask ProcessMarketDataMessage(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		try
		{
			var obj = msg.AsObject();
			var baseMsg = ((JToken)obj).DeserializeObject<WsMessage>();

			switch (baseMsg.Type)
			{
				case WsMessageTypes.Snapshot:
				case WsMessageTypes.Update:
					await ProcessMarketDataUpdate((JToken)obj, baseMsg.Channel, cancellationToken);
					break;

				case WsMessageTypes.Subscribed:
				case WsMessageTypes.Unsubscribed:
					this.AddInfoLog("MarketData subscription: {0}", baseMsg.Type);
					break;

				case WsMessageTypes.Error:
					var error = ((JToken)obj).DeserializeObject<WsErrorMessage>();
					this.AddErrorLog("MarketData error: {0} - {1}", error.ErrorCode, error.ErrorMessage);
					break;

				case WsMessageTypes.Heartbeat:
					// Ignore heartbeats
					break;
			}
		}
		catch (Exception ex)
		{
			this.AddErrorLog("Error processing market data message: {0}", ex);
		}
	}

	private async ValueTask ProcessMarketDataUpdate(JToken obj, string channel, CancellationToken cancellationToken)
	{
		switch (channel)
		{
			case WsChannels.OrderBook:
				if (OrderBookReceived is { } obHandler)
					await obHandler(obj.DeserializeObject<WsOrderBookMessage>(), cancellationToken);
				break;

			case WsChannels.Ticker:
				if (TickerReceived is { } tickerHandler)
					await tickerHandler(obj.DeserializeObject<WsTickerMessage>(), cancellationToken);
				break;

			case WsChannels.Trade:
				if (TradeReceived is { } tradeHandler)
					await tradeHandler(obj.DeserializeObject<WsTradeMessage>(), cancellationToken);
				break;
		}
	}

	private async ValueTask ProcessAccountMessage(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		try
		{
			var obj = msg.AsObject();
			var baseMsg = ((JToken)obj).DeserializeObject<WsMessage>();

			switch (baseMsg.Type)
			{
				case WsMessageTypes.Snapshot:
				case WsMessageTypes.Update:
					await ProcessAccountUpdate((JToken)obj, baseMsg.Channel, cancellationToken);
					break;

				case WsMessageTypes.Subscribed:
				case WsMessageTypes.Unsubscribed:
					this.AddInfoLog("Account subscription: {0}", baseMsg.Type);
					break;

				case WsMessageTypes.Error:
					var error = ((JToken)obj).DeserializeObject<WsErrorMessage>();
					this.AddErrorLog("Account error: {0} - {1}", error.ErrorCode, error.ErrorMessage);
					break;

				case WsMessageTypes.Heartbeat:
					// Ignore heartbeats
					break;
			}
		}
		catch (Exception ex)
		{
			this.AddErrorLog("Error processing account message: {0}", ex);
		}
	}

	private async ValueTask ProcessAccountUpdate(JToken obj, string channel, CancellationToken cancellationToken)
	{
		switch (channel)
		{
			case WsChannels.Order:
				if (OrderReceived is { } orderHandler)
					await orderHandler(obj.DeserializeObject<WsOrderMessage>(), cancellationToken);
				break;

			case WsChannels.Execution:
				if (ExecutionReceived is { } execHandler)
					await execHandler(obj.DeserializeObject<WsExecutionMessage>(), cancellationToken);
				break;

			case WsChannels.Position:
				if (PositionReceived is { } posHandler)
					await posHandler(obj.DeserializeObject<WsPositionMessage>(), cancellationToken);
				break;

			case WsChannels.Wallet:
				if (WalletReceived is { } walletHandler)
					await walletHandler(obj.DeserializeObject<WsWalletMessage>(), cancellationToken);
				break;

			case WsChannels.Rejection:
				if (RejectionReceived is { } rejHandler)
					await rejHandler(obj.DeserializeObject<WsRejectionMessage>(), cancellationToken);
				break;
		}
	}

	protected override void DisposeManaged()
	{
		_marketDataClient?.Dispose();
		_accountClient?.Dispose();
		base.DisposeManaged();
	}
}
