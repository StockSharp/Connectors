namespace StockSharp.Phemex.Native;

readonly record struct PhemexWsChannel(string Topic, string Symbol);

sealed class PhemexBookState
{
	public SortedDictionary<decimal, decimal> Bids { get; } = new(
		Comparer<decimal>.Create(static (left, right) => right.CompareTo(left)));
	public SortedDictionary<decimal, decimal> Asks { get; } = [];
}

sealed class PhemexWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly PhemexSections? _section;
	private readonly bool _isPrivate;
	private readonly PhemexRestClient _restClient;
	private readonly string _apiKey;
	private readonly HMACSHA256 _hasher;
	private readonly Lock _signSync = new();
	private readonly Lock _sync = new();
	private readonly HashSet<PhemexWsChannel> _channels = [];
	private readonly Dictionary<PhemexSymbolKey, PhemexBookState> _books = [];
	private readonly SemaphoreSlim _sessionSync = new(1, 1);
	private readonly SemaphoreSlim _connectionSync = new(1, 1);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private DateTime _nextSendTime;
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _authentication;
	private long _authenticationId;
	private long _requestId;
	private bool _isReady;

	public PhemexWsClient(string endpoint, PhemexSections? section, bool isPrivate,
		SecureString key, SecureString secret, PhemexRestClient restClient, WorkingTime workingTime)
	{
		_endpoint = NormalizeEndpoint(endpoint);
		_section = section;
		_isPrivate = isPrivate;
		_restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));

		if (isPrivate && (section is null || _apiKey.IsEmpty() || _hasher is null))
			throw new ArgumentException(
				"Phemex private WebSocket requires a market section, API key, and secret.");
	}

	private WorkingTime WorkingTime { get; }

	public override string Name => nameof(Phemex) + "_" +
		(_isPrivate ? $"{_section}_UserWs" : "MarketWs");

	public event Func<PhemexWsTradeMessage, CancellationToken, ValueTask> TradesReceived;
	public event Func<PhemexWsDepthMessage, CancellationToken, ValueTask> DepthReceived;
	public event Func<PhemexWsIndexMessage, CancellationToken, ValueTask> IndexReceived;
	public event Func<PhemexSections, PhemexWsOrderMessage, CancellationToken, ValueTask> OrderReceived;
	public event Func<PhemexSections, PhemexWsFillMessage, CancellationToken, ValueTask> FillReceived;
	public event Func<PhemexSections, PhemexWsBalanceMessage, CancellationToken, ValueTask> BalanceReceived;
	public event Func<PhemexWsPositionMessage, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
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
				throw new InvalidOperationException("Phemex WebSocket is already initialized.");
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

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendRequestAsync(client, "server.ping", [], cancellationToken)
			: default;

	public ValueTask SubscribeAsync(string topic, string symbol, int? limit,
		CancellationToken cancellationToken)
	{
		_ = limit;
		if (_isPrivate)
			return default;
		var channel = new PhemexWsChannel(topic.ThrowIfEmpty(nameof(topic)).ToUpperInvariant(),
			symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant());
		bool isAdded;
		bool shouldSend;
		using (_sync.EnterScope())
		{
			isAdded = _channels.Add(channel);
			shouldSend = isAdded && (channel.Topic != "INDEX" || !_channels.Any(existing =>
				existing != channel && existing.Topic == "INDEX" &&
				_restClient.ResolveSection(existing.Symbol) == _restClient.ResolveSection(channel.Symbol)));
		}
		return shouldSend && _client is { IsConnected: true } client
			? SendSubscriptionAsync(client, channel, true, cancellationToken)
			: default;
	}

	public ValueTask UnsubscribeAsync(string topic, string symbol, int? limit,
		CancellationToken cancellationToken)
	{
		_ = limit;
		if (_isPrivate)
			return default;
		var channel = new PhemexWsChannel(topic?.ToUpperInvariant(), symbol?.ToUpperInvariant());
		bool shouldSend;
		using (_sync.EnterScope())
		{
			if (!_channels.Remove(channel))
				return default;
			var section = _restClient.ResolveSection(channel.Symbol);
			shouldSend = !_channels.Any(existing => existing.Topic == channel.Topic &&
				_restClient.ResolveSection(existing.Symbol) == section);
			if (channel.Topic == "DEPTH")
				_books.Remove(new(channel.Symbol));
		}
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
			socket.Options.SetRequestHeader("User-Agent", "StockSharp-Phemex-Connector/1.0");
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
			using (_sync.EnterScope())
				_books.Clear();
			if (_isPrivate)
			{
				await AuthenticateAsync(client, cancellationToken);
				await SendRequestAsync(client,
					_section == PhemexSections.Spot ? "wo.subscribe" : "aop_p.subscribe",
					[], cancellationToken);
				return;
			}

			PhemexWsChannel[] channels;
			using (_sync.EnterScope())
				channels = [.. _channels];
			foreach (var channel in channels.Where(static channel => channel.Topic != "INDEX"))
				await SendSubscriptionAsync(client, channel, true, cancellationToken);
			foreach (var channel in channels.Where(static channel => channel.Topic == "INDEX")
				.GroupBy(channel => _restClient.ResolveSection(channel.Symbol))
				.Select(static group => group.First()))
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
		var expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
		var id = NextRequestId();
		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
		{
			_authenticationId = id;
			_authentication = completion;
		}
		try
		{
			await SendWireAsync(client, new PhemexWsAuthRequest
			{
				Id = id,
				Parameters = new()
				{
					ApiKey = _apiKey,
					Expiry = expiry,
					Signature = Sign(_apiKey + expiry.ToString(CultureInfo.InvariantCulture)),
				},
			}, cancellationToken);
			await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
			{
				if (ReferenceEquals(_authentication, completion))
				{
					_authentication = null;
					_authenticationId = 0;
				}
			}
		}
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client, PhemexWsChannel channel,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		var section = _restClient.ResolveSection(channel.Symbol);
		var method = (section, channel.Topic, isSubscribe) switch
		{
			(PhemexSections.Spot, "TRADE", true) => "trade.subscribe",
			(PhemexSections.Spot, "TRADE", false) => "trade.unsubscribe",
			(PhemexSections.Spot, "DEPTH", true) => "orderbook.subscribe",
			(PhemexSections.Spot, "DEPTH", false) => "orderbook.unsubscribe",
			(PhemexSections.Spot, "INDEX", true) => "spot_market24h.subscribe",
			(PhemexSections.Spot, "INDEX", false) => "spot_market24h.unsubscribe",
			(PhemexSections.Futures, "TRADE", true) => "trade_p.subscribe",
			(PhemexSections.Futures, "TRADE", false) => "trade_p.unsubscribe",
			(PhemexSections.Futures, "DEPTH", true) => "orderbook_p.subscribe",
			(PhemexSections.Futures, "DEPTH", false) => "orderbook_p.unsubscribe",
			(PhemexSections.Futures, "INDEX", true) => "perp_market24h_pack_p.subscribe",
			(PhemexSections.Futures, "INDEX", false) => "perp_market24h_pack_p.unsubscribe",
			_ => throw new NotSupportedException($"Unsupported Phemex stream topic {channel.Topic}."),
		};
		var parameters = isSubscribe && channel.Topic is ("TRADE" or "DEPTH")
			? new[] { channel.Symbol }
			: [];
		return SendRequestAsync(client, method, parameters, cancellationToken);
	}

	private ValueTask SendRequestAsync(WebSocketClient client, string method,
		string[] parameters, CancellationToken cancellationToken)
		=> SendWireAsync(client, new PhemexWsRequest
		{
			Id = NextRequestId(),
			Method = method,
			Parameters = parameters,
		}, cancellationToken);

	private long NextRequestId() => Interlocked.Increment(ref _requestId);

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
			_nextSendTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(55);
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
			var envelope = Deserialize<PhemexWsEnvelope>(payload);
			TaskCompletionSource<bool> authentication;
			long authenticationId;
			using (_sync.EnterScope())
			{
				authentication = _authentication;
				authenticationId = _authenticationId;
			}
			if (authentication is not null && envelope.Id == authenticationId)
			{
				if (envelope.Error is null)
					authentication.TrySetResult(true);
				else
					authentication.TrySetException(CreateError(envelope.Error));
				return;
			}
			if (envelope.Error is not null)
				throw CreateError(envelope.Error);

			if (envelope.SpotTrades is { Length: > 0 })
				await RaiseTradesAsync(envelope.Symbol, envelope.Timestamp, envelope.SpotTrades,
					cancellationToken);
			if (envelope.FuturesTrades is { Length: > 0 })
				await RaiseTradesAsync(envelope.Symbol, envelope.Timestamp, envelope.FuturesTrades,
					cancellationToken);
			if (envelope.SpotBook is not null)
				await RaiseDepthAsync(envelope.Symbol, envelope.Timestamp, envelope.Type,
					envelope.SpotBook, cancellationToken);
			if (envelope.FuturesBook is not null)
				await RaiseDepthAsync(envelope.Symbol, envelope.Timestamp, envelope.Type,
					envelope.FuturesBook, cancellationToken);
			if (envelope.SpotTicker is not null)
				await RaiseSpotTickerAsync(envelope.SpotTicker, envelope.Timestamp, cancellationToken);
			if (envelope.FuturesTickers is { Length: > 0 })
				await RaiseFuturesTickersAsync(envelope.FuturesTickers, envelope.Timestamp,
					cancellationToken);

			if (_section == PhemexSections.Spot)
				await ProcessSpotPrivateAsync(envelope, cancellationToken);
			else if (_section == PhemexSections.Futures)
				await ProcessFuturesPrivateAsync(envelope, cancellationToken);
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or TimeoutException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask RaiseTradesAsync(string symbol, long timestamp,
		PhemexWireTrade[] trades, CancellationToken cancellationToken)
	{
		if (TradesReceived is not { } handler || symbol.IsEmpty())
			return;
		var normalized = trades.Select((trade, index) => _restClient.NormalizeTrade(symbol, trade, index))
			.Select(static trade => new PhemexWsTrade
			{
				Symbol = trade.Symbol,
				TradeId = trade.TradeId,
				Price = trade.Price,
				Size = trade.Size,
				Side = trade.Side,
				Timestamp = trade.Timestamp,
			}).ToArray();
		await handler(new()
		{
			Symbol = symbol.ToUpperInvariant(),
			Timestamp = NanosecondsToMilliseconds(timestamp),
			Data = normalized,
		}, cancellationToken);
	}

	private async ValueTask RaiseDepthAsync(string symbol, long timestamp, string type,
		PhemexWireBook wireBook, CancellationToken cancellationToken)
	{
		if (DepthReceived is not { } handler || symbol.IsEmpty())
			return;
		var delta = _restClient.NormalizeDepth(symbol, wireBook, timestamp, 30);
		PhemexDepthData snapshot;
		using (_sync.EnterScope())
		{
			var key = new PhemexSymbolKey(symbol.ToUpperInvariant());
			if (!_books.TryGetValue(key, out var state))
			{
				state = new();
				_books.Add(key, state);
			}
			if (type.EqualsIgnoreCase("snapshot"))
			{
				state.Bids.Clear();
				state.Asks.Clear();
			}
			ApplyLevels(state.Bids, delta.Bids);
			ApplyLevels(state.Asks, delta.Asks);
			snapshot = new()
			{
				Bids = [.. state.Bids.Take(30).Select(static pair => new PhemexBookLevel
				{
					Price = pair.Key.ToWire(),
					Size = pair.Value.ToWire(),
				})],
				Asks = [.. state.Asks.Take(30).Select(static pair => new PhemexBookLevel
				{
					Price = pair.Key.ToWire(),
					Size = pair.Value.ToWire(),
				})],
				UpdateTime = delta.UpdateTime,
			};
		}
		await handler(new()
		{
			Symbol = symbol.ToUpperInvariant(),
			Timestamp = snapshot.UpdateTime,
			Data = snapshot,
		}, cancellationToken);
	}

	private static void ApplyLevels(SortedDictionary<decimal, decimal> target,
		PhemexBookLevel[] levels)
	{
		foreach (var level in levels ?? [])
		{
			if (level.Price.ToDecimal() is not decimal price ||
				level.Size.ToDecimal() is not decimal size)
				continue;
			if (size == 0m)
				target.Remove(price);
			else
				target[price] = size;
		}
	}

	private async ValueTask RaiseSpotTickerAsync(PhemexWireSpotTicker ticker, long timestamp,
		CancellationToken cancellationToken)
	{
		if (IndexReceived is not { } handler)
			return;
		var normalized = _restClient.NormalizeSpotTicker(ticker);
		await handler(ToIndexMessage(normalized, timestamp), cancellationToken);
	}

	private async ValueTask RaiseFuturesTickersAsync(PhemexWsTickerPack[] tickers, long timestamp,
		CancellationToken cancellationToken)
	{
		if (IndexReceived is not { } handler)
			return;
		foreach (var ticker in tickers)
		{
			var normalized = PhemexRestClient.NormalizeFuturesTicker(new()
			{
				Symbol = ticker.Symbol,
				Timestamp = timestamp,
				Open = ticker.Open,
				High = ticker.High,
				Low = ticker.Low,
				Close = ticker.Close,
				Volume = ticker.Volume,
				Turnover = ticker.Turnover,
				IndexPrice = ticker.IndexPrice,
				MarkPrice = ticker.MarkPrice,
				FundingRate = ticker.FundingRate,
				BidPrice = ticker.BidPrice,
				AskPrice = ticker.AskPrice,
			});
			await handler(ToIndexMessage(normalized, timestamp), cancellationToken);
		}
	}

	private static PhemexWsIndexMessage ToIndexMessage(PhemexTicker ticker, long timestamp)
		=> new()
		{
			Symbol = ticker.Symbol,
			Timestamp = NanosecondsToMilliseconds(timestamp),
			Data =
			[
				new()
				{
					Symbol = ticker.Symbol,
					Open = ticker.Open,
					High = ticker.High,
					Low = ticker.Low,
					LastPrice = ticker.Close,
					Volume = ticker.Volume,
					Turnover = ticker.Amount,
					IndexPrice = ticker.IndexPrice,
					MarkPrice = ticker.MarkPrice,
					NextFundingRate = ticker.FundingRate,
					BidPrice = ticker.BidPrice,
					AskPrice = ticker.AskPrice,
					UpdateTime = ticker.Time,
				},
			],
		};

	private async ValueTask ProcessSpotPrivateAsync(PhemexWsEnvelope envelope,
		CancellationToken cancellationToken)
	{
		if (envelope.SpotWallets is { Length: > 0 } && BalanceReceived is { } balanceHandler)
		{
			await balanceHandler(PhemexSections.Spot, new()
			{
				Timestamp = NanosecondsToMilliseconds(envelope.Timestamp),
				Data = new()
				{
					Type = "SPOT",
					Timestamp = NanosecondsToMilliseconds(envelope.Timestamp),
					Balances = [.. envelope.SpotWallets.Select(_restClient.NormalizeSpotBalance)],
				},
			}, cancellationToken);
		}
		if (envelope.SpotOrders is null)
			return;
		if (OrderReceived is { } orderHandler)
		{
			foreach (var order in (envelope.SpotOrders.Open ?? [])
				.Concat(envelope.SpotOrders.Closed ?? []))
			{
				var normalized = _restClient.NormalizeOrder(PhemexSections.Spot, order);
				await orderHandler(PhemexSections.Spot, new()
				{
					Symbol = normalized.Symbol,
					Timestamp = normalized.UpdateTime,
					Data = normalized,
				}, cancellationToken);
			}
		}
		if (FillReceived is { } fillHandler)
		{
			foreach (var fill in envelope.SpotOrders.Fills ?? [])
			{
				var normalized = _restClient.NormalizeFill(PhemexSections.Spot, fill.Symbol, fill);
				await fillHandler(PhemexSections.Spot, new()
				{
					Symbol = normalized.Symbol,
					Timestamp = normalized.Timestamp,
					Data = normalized,
				}, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessFuturesPrivateAsync(PhemexWsEnvelope envelope,
		CancellationToken cancellationToken)
	{
		if (envelope.FuturesAccounts is { Length: > 0 } && BalanceReceived is { } balanceHandler)
		{
			await balanceHandler(PhemexSections.Futures, new()
			{
				Timestamp = NanosecondsToMilliseconds(envelope.Timestamp),
				Data = new()
				{
					Type = "FUTURES",
					Timestamp = NanosecondsToMilliseconds(envelope.Timestamp),
					Balances = [.. envelope.FuturesAccounts.Select(
						PhemexRestClient.NormalizeFuturesBalance)],
				},
			}, cancellationToken);
		}

		foreach (var wireOrder in envelope.FuturesOrders ?? [])
		{
			var order = _restClient.NormalizeOrder(PhemexSections.Futures, wireOrder);
			if (OrderReceived is { } orderHandler)
				await orderHandler(PhemexSections.Futures, new()
				{
					Symbol = order.Symbol,
					Timestamp = order.UpdateTime,
					Data = order,
				}, cancellationToken);

			if (FillReceived is { } fillHandler && !wireOrder.ExecutionId.IsEmpty() &&
				wireOrder.ExecutionId != "00000000-0000-0000-0000-000000000000" &&
				wireOrder.ExecutionQuantity.ToDecimal() is > 0m)
			{
				var fill = _restClient.NormalizeFill(PhemexSections.Futures, wireOrder.Symbol,
					new()
					{
						ExecutionId = wireOrder.ExecutionId,
						OrderId = wireOrder.OrderId,
						Symbol = wireOrder.Symbol,
						Side = wireOrder.Side,
						FuturesPrice = wireOrder.ExecutionPrice,
						FuturesQuantity = wireOrder.ExecutionQuantity,
						FuturesFee = wireOrder.FuturesFee,
						Timestamp = wireOrder.UpdateTime,
					});
				await fillHandler(PhemexSections.Futures, new()
				{
					Symbol = fill.Symbol,
					Timestamp = fill.Timestamp,
					Data = fill,
				}, cancellationToken);
			}
		}

		if (PositionReceived is { } positionHandler)
		{
			foreach (var wirePosition in envelope.FuturesPositions ?? [])
			{
				var position = PhemexRestClient.NormalizePosition(wirePosition);
				await positionHandler(new()
				{
					Symbol = position.Symbol,
					Timestamp = position.UpdateTime,
					Data = position,
				}, cancellationToken);
			}
		}
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
			?? throw new InvalidDataException("Phemex WebSocket returned an empty JSON value.");

	private static Exception CreateError(PhemexWsError error)
		=> new InvalidOperationException($"Phemex WebSocket error {error.Code}: {error.Message}.");

	private async ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
	{
		this.AddErrorLog(error);
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static long NanosecondsToMilliseconds(long value)
		=> value <= 0 ? 0 : value / 1_000_000;

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}
}
