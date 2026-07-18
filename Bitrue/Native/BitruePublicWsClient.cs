namespace StockSharp.Bitrue.Native;

readonly record struct BitrueWsSubscriptionKey(BitrueWsTopics Topic, string Symbol,
	TimeSpan TimeFrame);

sealed class BitruePublicWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly BitrueSections _section;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<BitrueWsSubscriptionKey, long> _subscriptions = [];
	private readonly Dictionary<string, BitrueWsSubscriptionKey> _channels =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private long _localNumber;
	private string _pendingTradeChannel;
	private TaskCompletionSource<BitrueWsTradeHistoryEnvelope> _pendingTradeRequest;
	private string _pendingCandleChannel;
	private TaskCompletionSource<BitrueWsCandleHistoryEnvelope> _pendingCandleRequest;

	public BitruePublicWsClient(string endpoint, BitrueSections section,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint));
		_section = section;
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Bitrue) + "_" + _section + "_PublicWs";

	public event Func<BitrueSections, string, BitrueWsBook, long,
		CancellationToken, ValueTask> BookReceived;
	public event Func<string, BitrueWsTicker, long, CancellationToken, ValueTask> TickerReceived;
	public event Func<string, BitrueWsTrade, long, CancellationToken, ValueTask> TradeReceived;
	public event Func<string, TimeSpan, BitrueFuturesCandle, long,
		CancellationToken, ValueTask> CandleReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<BitrueSections, ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		if (_client is not null)
			_client.PreProcess2 -= PreProcess;
		_client?.Dispose();
		_sendSync.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException("Bitrue public WebSocket is already initialized.");
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

	public ValueTask SubscribeDepthAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitrueWsTopics.Depth, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeDepthAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitrueWsTopics.Depth, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitrueWsTopics.Ticker, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitrueWsTopics.Ticker, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitrueWsTopics.Trades, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitrueWsTopics.Trades, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitrueWsTopics.Candles, symbol, timeFrame), true,
			cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(BitrueWsTopics.Candles, symbol, timeFrame), false,
			cancellationToken);

	public async ValueTask<BitrueWsTrade[]> RequestTradesAsync(string symbol, int count,
		CancellationToken cancellationToken)
	{
		EnsureFutures();
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var channel = CreateChannel(new(BitrueWsTopics.Trades, symbol, default));
			var completion = new TaskCompletionSource<BitrueWsTradeHistoryEnvelope>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			using (_sync.EnterScope())
			{
				_pendingTradeChannel = channel;
				_pendingTradeRequest = completion;
			}
			await SendAsync(GetConnectedClient(), new BitrueWsCommand
			{
				Action = BitrueWsActions.Request,
				Parameters = new()
				{
					Channel = channel,
					CallbackId = NextCallbackId(),
					Top = count.Min(300).Max(1),
				},
			}, cancellationToken);
			var result = await completion.Task.WaitAsync(cancellationToken);
			return result.Data ?? [];
		}
		finally
		{
			using (_sync.EnterScope())
			{
				_pendingTradeChannel = null;
				_pendingTradeRequest = null;
			}
			_requestSync.Release();
		}
	}

	public async ValueTask<BitrueFuturesCandle[]> RequestCandlesAsync(string symbol,
		TimeSpan timeFrame, long endIndex, int count, CancellationToken cancellationToken)
	{
		EnsureFutures();
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var channel = CreateChannel(new(BitrueWsTopics.Candles, symbol, timeFrame));
			var completion = new TaskCompletionSource<BitrueWsCandleHistoryEnvelope>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			using (_sync.EnterScope())
			{
				_pendingCandleChannel = channel;
				_pendingCandleRequest = completion;
			}
			await SendAsync(GetConnectedClient(), new BitrueWsCommand
			{
				Action = BitrueWsActions.Request,
				Parameters = new()
				{
					Channel = channel,
					CallbackId = NextCallbackId(),
					EndIndex = endIndex,
					PageSize = count.Min(300).Max(1),
				},
			}, cancellationToken);
			var result = await completion.Task.WaitAsync(cancellationToken);
			return result.Data ?? [];
		}
		finally
		{
			using (_sync.EnterScope())
			{
				_pendingCandleChannel = null;
				_pendingCandleRequest = null;
			}
			_requestSync.Release();
		}
	}

	public ValueTask SendPongAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendAsync(client, new BitrueWsPong
			{
				Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
					.ToString(CultureInfo.InvariantCulture),
			}, cancellationToken)
			: default;

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
			BufferSize = 2 * 1024 * 1024,
			BufferSizeUncompress = 20 * 1024 * 1024,
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Bitrue-Connector/1.0");
		client.PreProcess2 += PreProcess;
		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		if (client is null)
			return;
		client.PreProcess2 -= PreProcess;
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
			KeyValuePair<BitrueWsSubscriptionKey, long>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription.Key, subscription.Value,
					true, cancellationToken);
		}
		else if (state is ConnectionStates.Failed or ConnectionStates.Disconnected)
			FailHistoryRequests(new InvalidOperationException(
				$"Bitrue {_section} WebSocket disconnected during a history request."));

		if (StateChanged is { } handler)
			await handler(_section, state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(BitrueWsSubscriptionKey subscription,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		ValidateTopic(subscription.Topic);
		long localNumber;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				if (_subscriptions.ContainsKey(subscription))
					return;
				localNumber = Interlocked.Increment(ref _localNumber);
				_subscriptions.Add(subscription, localNumber);
				_channels[CreateChannel(subscription)] = subscription;
			}
			else
			{
				if (!_subscriptions.Remove(subscription, out localNumber))
					return;
				_channels.Remove(CreateChannel(subscription));
			}
		}

		if (_client?.IsConnected == true)
			await SendSubscriptionAsync(_client, subscription, localNumber, isSubscribe,
				cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		BitrueWsSubscriptionKey subscription, long localNumber, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new BitrueWsCommand
		{
			Action = isSubscribe ? BitrueWsActions.Subscribe : BitrueWsActions.Unsubscribe,
			Parameters = new()
			{
				Channel = CreateChannel(subscription),
				CallbackId = localNumber.ToString(CultureInfo.InvariantCulture),
			},
		}, cancellationToken);

	private string CreateChannel(BitrueWsSubscriptionKey subscription)
	{
		var symbol = subscription.Symbol.ToPublicWsSymbol(_section);
		return (_section, subscription.Topic) switch
		{
			(BitrueSections.Spot, BitrueWsTopics.Depth) =>
				$"market_{symbol}_simple_depth_step0",
			(BitrueSections.Futures, BitrueWsTopics.Depth) =>
				$"market_{symbol}_depth_step0",
			(BitrueSections.Futures, BitrueWsTopics.Ticker) =>
				$"market_{symbol}_ticker",
			(BitrueSections.Futures, BitrueWsTopics.Trades) =>
				$"market_{symbol}_trade_ticker",
			(BitrueSections.Futures, BitrueWsTopics.Candles) =>
				$"market_{symbol}_kline_{subscription.TimeFrame.ToBitrueFuturesInterval().ToWsWire()}",
			_ => throw new NotSupportedException(
				$"Bitrue {_section} does not publish {subscription.Topic} over WebSocket."),
		};
	}

	private void ValidateTopic(BitrueWsTopics topic)
	{
		if (_section == BitrueSections.Spot && topic != BitrueWsTopics.Depth)
			throw new NotSupportedException(
				$"Bitrue spot does not publish {topic} over its market WebSocket.");
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
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			var header = Deserialize<BitrueWsHeader>(payload);
			if (!header.Ping.IsEmpty())
			{
				await SendAsync(client, new BitrueWsPong { Timestamp = header.Ping },
					cancellationToken);
				return;
			}
			if (!header.Status.IsEmpty() && !header.Status.EqualsIgnoreCase("ok"))
				throw new InvalidOperationException(
					$"Bitrue WebSocket request failed: {header.Status}".Trim());

			if (header.EventReport.EqualsIgnoreCase("rep"))
			{
				CompleteHistoryRequest(payload, header);
				return;
			}
			if (!header.EventReport.IsEmpty())
				return;
			if (!TryGetSubscription(header.Channel, out var subscription))
				return;

			switch (subscription.Topic)
			{
				case BitrueWsTopics.Depth:
					var book = Deserialize<BitrueWsBookEnvelope>(payload);
					if (book.Tick is not null && BookReceived is { } bookHandler)
						await bookHandler(_section, subscription.Symbol, book.Tick,
							book.Timestamp, cancellationToken);
					break;
				case BitrueWsTopics.Ticker:
					var ticker = Deserialize<BitrueWsTickerEnvelope>(payload);
					if (ticker.Tick is not null && TickerReceived is { } tickerHandler)
						await tickerHandler(subscription.Symbol, ticker.Tick,
							ticker.Timestamp, cancellationToken);
					break;
				case BitrueWsTopics.Trades:
					var trades = Deserialize<BitrueWsTradesEnvelope>(payload);
					if (TradeReceived is { } tradeHandler)
						foreach (var trade in trades.Tick?.Data ?? [])
							await tradeHandler(subscription.Symbol, trade,
								trades.Timestamp, cancellationToken);
					break;
				case BitrueWsTopics.Candles:
					var candle = Deserialize<BitrueWsCandleEnvelope>(payload);
					if (candle.Tick is not null && CandleReceived is { } candleHandler)
						await candleHandler(subscription.Symbol, subscription.TimeFrame,
							candle.Tick, candle.Timestamp, cancellationToken);
					break;
				default:
					throw new InvalidDataException(
						"Bitrue WebSocket returned an unsupported channel.");
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			FailHistoryRequests(error);
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private void FailHistoryRequests(Exception error)
	{
		TaskCompletionSource<BitrueWsTradeHistoryEnvelope> tradeCompletion;
		TaskCompletionSource<BitrueWsCandleHistoryEnvelope> candleCompletion;
		using (_sync.EnterScope())
		{
			tradeCompletion = _pendingTradeRequest;
			candleCompletion = _pendingCandleRequest;
		}
		tradeCompletion?.TrySetException(error);
		candleCompletion?.TrySetException(error);
	}

	private void CompleteHistoryRequest(string payload, BitrueWsHeader header)
	{
		TaskCompletionSource<BitrueWsTradeHistoryEnvelope> tradeCompletion = null;
		TaskCompletionSource<BitrueWsCandleHistoryEnvelope> candleCompletion = null;
		using (_sync.EnterScope())
		{
			if (header.Channel.EqualsIgnoreCase(_pendingTradeChannel))
				tradeCompletion = _pendingTradeRequest;
			else if (header.Channel.EqualsIgnoreCase(_pendingCandleChannel))
				candleCompletion = _pendingCandleRequest;
		}

		if (tradeCompletion is not null)
			tradeCompletion.TrySetResult(Deserialize<BitrueWsTradeHistoryEnvelope>(payload));
		else if (candleCompletion is not null)
			candleCompletion.TrySetResult(Deserialize<BitrueWsCandleHistoryEnvelope>(payload));
	}

	private bool TryGetSubscription(string channel, out BitrueWsSubscriptionKey subscription)
	{
		using (_sync.EnterScope())
			return _channels.TryGetValue(channel ?? string.Empty, out subscription);
	}

	private WebSocketClient GetConnectedClient()
		=> _client is { IsConnected: true } client
			? client
			: throw new InvalidOperationException("Bitrue public WebSocket is not connected.");

	private string NextCallbackId()
		=> Interlocked.Increment(ref _localNumber).ToString(CultureInfo.InvariantCulture);

	private void EnsureFutures()
	{
		if (_section != BitrueSections.Futures)
			throw new NotSupportedException(
				"Bitrue spot does not expose WebSocket history requests.");
	}

	private int PreProcess(ReadOnlyMemory<byte> source, Memory<byte> destination)
	{
		if (source.IsEmpty)
			return 0;
		var first = source.Span[0];
		if (first is (byte)'{' or (byte)'[')
		{
			source.CopyTo(destination);
			return source.Length;
		}
		return source.UnGzipTo(destination);
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			var result = JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings);
			if (result is null)
				throw new InvalidDataException("Bitrue WebSocket returned an empty message.");
			return result;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Bitrue WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
