namespace StockSharp.SynFutures.Native;

sealed class SynFuturesSocketClient : BaseLogReceiver
{
	private const int _maximumMessageBytes = 16 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly Lock _sync = new();
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private ClientWebSocket _socket;
	private CancellationTokenSource _receiveCancellation;
	private Task _receiveTask;
	private long _requestId;
	private bool _isDisconnecting;

	public SynFuturesSocketClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("ws" or "wss"))
			throw new ArgumentException(
				"SynFutures WebSocket endpoint must use WS or WSS.",
				nameof(endpoint));
	}

	public override string Name => "SynFutures_WebSocket";

	public event Func<SynFuturesMarket, CancellationToken, ValueTask>
		MarketChanged;
	public event Func<string, uint, SynFuturesDepthSteps, CancellationToken,
		ValueTask> DepthReceived;
	public event Func<string, uint, SynFuturesTrade[], CancellationToken,
		ValueTask> TradesReceived;
	public event Func<string, uint, SynFuturesCandle, CancellationToken,
		ValueTask> KlineReceived;
	public event Func<SynFuturesPortfolioNotification, CancellationToken,
		ValueTask> PortfolioChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (_socket is not null)
				throw new InvalidOperationException(
					"SynFutures WebSocket is already initialized.");
			_isDisconnecting = false;
			_receiveCancellation = new();
			_socket = CreateSocket();
		}
		await RaiseStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			await _socket.ConnectAsync(_endpoint, cancellationToken);
			var socket = _socket;
			var token = _receiveCancellation.Token;
			_receiveTask = Task.Run(() => ReceiveLoopAsync(socket, token),
				CancellationToken.None);
			await RaiseStateAsync(ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
			await DisposeSocketAsync();
			await RaiseStateAsync(ConnectionStates.Disconnected,
				cancellationToken);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		ClientWebSocket socket;
		Task receiveTask;
		using (_sync.EnterScope())
		{
			socket = _socket;
			receiveTask = _receiveTask;
			if (socket is null)
				return;
			_isDisconnecting = true;
		}
		await RaiseStateAsync(ConnectionStates.Disconnecting, cancellationToken);
		try
		{
			if (socket.State is WebSocketState.Open or
				WebSocketState.CloseReceived)
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
					"disconnect", cancellationToken);
		}
		catch (Exception error) when (
			!cancellationToken.IsCancellationRequested)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
		_receiveCancellation?.Cancel();
		if (receiveTask is not null)
			try
			{
				await receiveTask;
			}
			catch (OperationCanceledException)
			{
			}
		await DisposeSocketAsync();
		await RaiseStateAsync(ConnectionStates.Disconnected, cancellationToken);
	}

	public ValueTask SubscribeMarketAsync(SynFuturesMarket market,
		CancellationToken cancellationToken)
		=> SendPairAsync("SUBSCRIBE", market, "instrument",
			cancellationToken);

	public ValueTask UnsubscribeMarketAsync(SynFuturesMarket market,
		CancellationToken cancellationToken)
		=> SendPairAsync("UNSUBSCRIBE", market, "instrument",
			cancellationToken);

	public ValueTask SubscribeDepthAsync(SynFuturesMarket market,
		CancellationToken cancellationToken)
		=> SendPairAsync("SUBSCRIBE", market, "orderBook",
			cancellationToken);

	public ValueTask UnsubscribeDepthAsync(SynFuturesMarket market,
		CancellationToken cancellationToken)
		=> SendPairAsync("UNSUBSCRIBE", market, "orderBook",
			cancellationToken);

	public ValueTask SubscribeTradesAsync(SynFuturesMarket market,
		CancellationToken cancellationToken)
		=> SendTradesAsync("SUBSCRIBE", market, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(SynFuturesMarket market,
		CancellationToken cancellationToken)
		=> SendTradesAsync("UNSUBSCRIBE", market, cancellationToken);

	public ValueTask SubscribeKlineAsync(SynFuturesMarket market,
		TimeSpan timeFrame, CancellationToken cancellationToken)
		=> SendKlineAsync("SUBSCRIBE", market, timeFrame, cancellationToken);

	public ValueTask UnsubscribeKlineAsync(SynFuturesMarket market,
		TimeSpan timeFrame, CancellationToken cancellationToken)
		=> SendKlineAsync("UNSUBSCRIBE", market, timeFrame, cancellationToken);

	public ValueTask SubscribePortfolioAsync(string wallet,
		CancellationToken cancellationToken)
		=> SendAsync("SUBSCRIBE", new SynFuturesSocketPortfolioParameters
		{
			ChainId = SynFuturesExtensions.ChainId,
			UserAddress = wallet.NormalizeAddress(),
			Type = "portfolio",
		}, cancellationToken);

	public ValueTask UnsubscribePortfolioAsync(string wallet,
		CancellationToken cancellationToken)
		=> SendAsync("UNSUBSCRIBE", new SynFuturesSocketPortfolioParameters
		{
			ChainId = SynFuturesExtensions.ChainId,
			UserAddress = wallet.NormalizeAddress(),
			Type = "portfolio",
		}, cancellationToken);

	private ValueTask SendPairAsync(string method, SynFuturesMarket market,
		string type, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		return SendAsync(method, new SynFuturesSocketPairParameters
		{
			ChainId = SynFuturesExtensions.ChainId,
			Instrument = market.InstrumentAddress.NormalizeAddress(),
			Expiry = market.Expiry,
			Type = type,
		}, cancellationToken);
	}

	private ValueTask SendTradesAsync(string method, SynFuturesMarket market,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		return SendAsync(method, new SynFuturesSocketTradesParameters
		{
			ChainId = SynFuturesExtensions.ChainId,
			Pairs = [market.InstrumentAddress.NormalizeAddress() + "_" +
				market.Expiry.ToString(CultureInfo.InvariantCulture)],
			Type = "trades",
		}, cancellationToken);
	}

	private ValueTask SendKlineAsync(string method, SynFuturesMarket market,
		TimeSpan timeFrame, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		return SendAsync(method, new SynFuturesSocketKlineParameters
		{
			ChainId = SynFuturesExtensions.ChainId,
			Instrument = market.InstrumentAddress.NormalizeAddress(),
			Expiry = market.Expiry,
			Interval = timeFrame.ToApiInterval(),
			Type = "kline",
		}, cancellationToken);
	}

	private async ValueTask SendAsync<T>(string method, T parameters,
		CancellationToken cancellationToken)
	{
		ClientWebSocket socket;
		using (_sync.EnterScope())
			socket = _socket;
		if (socket?.State != WebSocketState.Open)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var payload = JsonConvert.SerializeObject(
			new SynFuturesSocketRequest<T>
			{
				Id = Interlocked.Increment(ref _requestId),
				Method = method,
				Parameters = parameters,
			}, _settings);
		await SendTextAsync(socket, payload, cancellationToken);
	}

	private async ValueTask SendTextAsync(ClientWebSocket socket, string text,
		CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(text);
		await _sendGate.WaitAsync(cancellationToken);
		try
		{
			await socket.SendAsync(bytes, WebSocketMessageType.Text, true,
				cancellationToken);
		}
		finally
		{
			_sendGate.Release();
		}
	}

	private async Task ReceiveLoopAsync(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[81920];
		try
		{
			while (!cancellationToken.IsCancellationRequested &&
				socket.State is WebSocketState.Open or
					WebSocketState.CloseReceived)
			{
				using var message = new MemoryStream();
				WebSocketReceiveResult result;
				do
				{
					result = await socket.ReceiveAsync(buffer, cancellationToken);
					if (result.MessageType == WebSocketMessageType.Close)
						break;
					if (message.Length + result.Count > _maximumMessageBytes)
						throw new InvalidDataException(
							"SynFutures WebSocket message exceeds 16 MiB.");
					message.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage);
				if (result.MessageType == WebSocketMessageType.Close)
					break;
				if (result.MessageType != WebSocketMessageType.Text)
					continue;
				var text = Encoding.UTF8.GetString(message.ToArray());
				if (text.Equals("ping", StringComparison.OrdinalIgnoreCase))
				{
					await SendTextAsync(socket, "pong", cancellationToken);
					continue;
				}
				if (text.Equals("pong", StringComparison.OrdinalIgnoreCase))
					continue;
				await ProcessAsync(text, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (
			cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, CancellationToken.None);
		}
		finally
		{
			bool isUnexpected;
			using (_sync.EnterScope())
				isUnexpected = !_isDisconnecting;
			if (isUnexpected)
				await RaiseStateAsync(ConnectionStates.Failed,
					CancellationToken.None);
		}
	}

	private async ValueTask ProcessAsync(string text,
		CancellationToken cancellationToken)
	{
		SynFuturesSocketHeader header;
		try
		{
			header = JsonConvert.DeserializeObject<SynFuturesSocketHeader>(text,
				_settings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"SynFutures WebSocket returned malformed JSON.", error);
		}
		if (header is null)
			throw new InvalidDataException(
				"SynFutures WebSocket returned an empty message.");
		if (header.Id != 0)
		{
			if (!header.Result.Equals("success",
				StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"SynFutures WebSocket rejected request " + header.Id + ".");
			return;
		}
		switch (header.Stream)
		{
			case "marketPairInfoChanged":
			{
				var envelope = Deserialize<SynFuturesMarket>(text);
				if (envelope.Data is not null && MarketChanged is not null)
					await MarketChanged(envelope.Data, cancellationToken);
				break;
			}
			case "orderBook":
			{
				var envelope = Deserialize<SynFuturesDepthSteps>(text);
				if (envelope.Data is not null && DepthReceived is not null)
					await DepthReceived(envelope.Instrument, envelope.Expiry,
						envelope.Data, cancellationToken);
				break;
			}
			case "trades":
			{
				var envelope = Deserialize<SynFuturesTrade[]>(text);
				if (envelope.Data is not null && TradesReceived is not null)
					await TradesReceived(envelope.Instrument, envelope.Expiry,
						envelope.Data, cancellationToken);
				break;
			}
			case "kline":
			{
				var envelope = Deserialize<SynFuturesCandle>(text);
				if (envelope.Data is not null && KlineReceived is not null)
					await KlineReceived(envelope.Instrument, envelope.Expiry,
						envelope.Data, cancellationToken);
				break;
			}
			case "portfolio":
			{
				var envelope = Deserialize<SynFuturesPortfolioNotification>(text);
				if (envelope.Data is not null && PortfolioChanged is not null)
					await PortfolioChanged(envelope.Data, cancellationToken);
				break;
			}
			case "instrument":
				break;
			default:
				this.AddWarningLog("Unknown SynFutures WebSocket stream '{0}'.",
					header.Stream);
				break;
		}
	}

	private SynFuturesSocketEnvelope<T> Deserialize<T>(string text)
		=> JsonConvert.DeserializeObject<SynFuturesSocketEnvelope<T>>(text,
			_settings) ?? throw new InvalidDataException(
			"SynFutures WebSocket returned an empty stream envelope.");

	private ClientWebSocket CreateSocket()
	{
		var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		socket.Options.SetRequestHeader("Origin", "https://app.synfutures.com");
		socket.Options.SetRequestHeader("User-Agent",
			"Mozilla/5.0 (compatible; StockSharp-SynFutures/1.0)");
		return socket;
	}

	private async ValueTask DisposeSocketAsync()
	{
		ClientWebSocket socket;
		CancellationTokenSource cancellation;
		using (_sync.EnterScope())
		{
			socket = _socket;
			cancellation = _receiveCancellation;
			_socket = null;
			_receiveCancellation = null;
			_receiveTask = null;
		}
		cancellation?.Cancel();
		socket?.Dispose();
		cancellation?.Dispose();
		await Task.CompletedTask;
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is null ? default : Error(error, cancellationToken);

	private ValueTask RaiseStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
		=> StateChanged is null ? default : StateChanged(state,
			cancellationToken);

	protected override void DisposeManaged()
	{
		_receiveCancellation?.Cancel();
		_socket?.Dispose();
		_receiveCancellation?.Dispose();
		_sendGate.Dispose();
		base.DisposeManaged();
	}
}
