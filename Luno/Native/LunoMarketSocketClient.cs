namespace StockSharp.Luno.Native;

sealed class LunoMarketSocketClient : BaseLogReceiver
{
	private readonly string _pair;
	private readonly string _endpoint;
	private readonly string _apiKey;
	private readonly string _apiSecret;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, LunoMarketStreamOrder> _bids =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, LunoMarketStreamOrder> _asks =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};
	private WebSocketClient _client;
	private long _sequence;
	private LunoStreamStatuses _status;
	private CancellationTokenSource _heartbeatCancellation;
	private Task _heartbeatTask;

	public LunoMarketSocketClient(string endpoint, string pair, SecureString key,
		SecureString secret, WorkingTime workingTime, int reconnectAttempts)
	{
		_pair = pair.NormalizeSymbol();
		_endpoint = CreateEndpoint(endpoint,
			$"/api/1/stream/{Uri.EscapeDataString(_pair)}");
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		_apiSecret = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() || _apiSecret.IsEmpty())
			throw new ArgumentException(
				"Luno market streams require an API key and secret.");
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => $"Luno_Market_{_pair}";

	public event Func<LunoMarketStreamState, CancellationToken, ValueTask>
		StateReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_heartbeatCancellation?.Cancel();
		_heartbeatCancellation?.Dispose();
		_client?.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Luno market WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			StartHeartbeat();
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(state, token),
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
		};
		client.PostConnect += OnPostConnectAsync;
		return client;
	}

	private async ValueTask OnPostConnectAsync(bool isReconnect,
		CancellationToken cancellationToken)
	{
		_ = isReconnect;
		ResetBook();
		var payload = JsonConvert.SerializeObject(new LunoStreamCredentials
		{
			ApiKeyId = _apiKey,
			ApiKeySecret = _apiSecret,
		}, _jsonSettings);
		await _client.SendAsync(Encoding.UTF8.GetBytes(payload),
			WebSocketMessageType.Text, cancellationToken);
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Reconnecting or ConnectionStates.Failed)
			ResetBook();
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var envelope = JsonConvert.DeserializeObject<LunoMarketStreamEnvelope>(
				payload, _jsonSettings) ?? throw new InvalidDataException(
					"Luno market stream returned an empty JSON value.");
			LunoMarketStreamState state;
			if (envelope.Asks is not null && envelope.Bids is not null)
				state = Initialize(envelope);
			else
				state = ApplyUpdate(envelope);
			if (state is not null && StateReceived is { } handler)
				await handler(state, cancellationToken);
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, cancellationToken);
			client.Abort();
		}
	}

	private LunoMarketStreamState Initialize(LunoMarketStreamEnvelope envelope)
	{
		if (envelope.Sequence <= 0)
			throw new InvalidDataException(
				"Luno market stream returned an invalid initial sequence.");
		using (_sync.EnterScope())
		{
			_bids.Clear();
			_asks.Clear();
			foreach (var order in envelope.Bids)
				AddInitial(_bids, order);
			foreach (var order in envelope.Asks)
				AddInitial(_asks, order);
			_sequence = envelope.Sequence;
			_status = envelope.Status ?? LunoStreamStatuses.Disabled;
			return CreateState(envelope.Timestamp, []);
		}
	}

	private LunoMarketStreamState ApplyUpdate(LunoMarketStreamEnvelope envelope)
	{
		using (_sync.EnterScope())
		{
			if (_sequence == 0 || envelope.Sequence <= _sequence)
				return null;
			if (envelope.Sequence != _sequence + 1)
				throw new InvalidDataException(
					$"Luno {_pair} stream sequence gap: expected {_sequence + 1}, received {envelope.Sequence}.");

			var timestamp = envelope.Timestamp.ToLunoTime(DateTime.UtcNow);
			var trades = new List<LunoStreamTrade>();
			foreach (var trade in envelope.TradeUpdates ?? [])
				trades.Add(ApplyTrade(trade, timestamp));

			if (envelope.CreateUpdate is { } create)
				ApplyCreate(create);
			if (envelope.DeleteUpdate?.OrderId.IsEmpty() == false)
			{
				_bids.Remove(envelope.DeleteUpdate.OrderId);
				_asks.Remove(envelope.DeleteUpdate.OrderId);
			}
			if (envelope.StatusUpdate is { } status)
				_status = status.Status;
			_sequence = envelope.Sequence;
			return CreateState(envelope.Timestamp, [.. trades]);
		}
	}

	private LunoStreamTrade ApplyTrade(LunoMarketTradeUpdate trade,
		DateTime timestamp)
	{
		if (trade is null || trade.Base <= 0 || trade.Counter <= 0)
			throw new InvalidDataException(
				"Luno market stream returned a non-positive trade.");
		var makerOrderId = trade.MakerOrderId.IsEmpty()
			? trade.LegacyOrderId
			: trade.MakerOrderId;
		Dictionary<string, LunoMarketStreamOrder> book;
		Sides takerSide;
		if (_bids.TryGetValue(makerOrderId, out var maker))
		{
			book = _bids;
			takerSide = Sides.Sell;
		}
		else if (_asks.TryGetValue(makerOrderId, out maker))
		{
			book = _asks;
			takerSide = Sides.Buy;
		}
		else
			throw new InvalidDataException(
				$"Luno {_pair} stream trade references unknown maker order '{makerOrderId}'.");

		var remaining = maker.Volume - trade.Base;
		if (remaining < 0)
			throw new InvalidDataException(
				$"Luno {_pair} stream produced a negative maker volume.");
		if (remaining == 0)
			book.Remove(makerOrderId);
		else
			maker.Volume = remaining;

		return new()
		{
			Sequence = trade.Sequence,
			Timestamp = timestamp,
			Price = trade.Counter / trade.Base,
			Volume = trade.Base,
			TakerSide = takerSide,
			MakerOrderId = makerOrderId,
			TakerOrderId = trade.TakerOrderId,
		};
	}

	private void ApplyCreate(LunoMarketCreateUpdate create)
	{
		if (create.OrderId.IsEmpty() || create.Price <= 0 || create.Volume <= 0)
			throw new InvalidDataException(
				"Luno market stream returned an invalid create update.");
		var order = new LunoMarketStreamOrder
		{
			Id = create.OrderId,
			Price = create.Price,
			Volume = create.Volume,
		};
		(create.Type == LunoLimitSides.Bid ? _bids : _asks)[order.Id] = order;
	}

	private static void AddInitial(
		IDictionary<string, LunoMarketStreamOrder> book,
		LunoMarketStreamOrder order)
	{
		if (order?.Id.IsEmpty() != false || order.Price <= 0 || order.Volume <= 0)
			return;
		book[order.Id] = new()
		{
			Id = order.Id,
			Price = order.Price,
			Volume = order.Volume,
		};
	}

	private LunoMarketStreamState CreateState(long timestamp,
		LunoStreamTrade[] trades)
		=> new()
		{
			Pair = _pair,
			Sequence = _sequence,
			Timestamp = timestamp.ToLunoTime(DateTime.UtcNow),
			Status = _status,
			Bids = Aggregate(_bids.Values, true),
			Asks = Aggregate(_asks.Values, false),
			Trades = trades,
		};

	private static LunoStreamPriceLevel[] Aggregate(
		IEnumerable<LunoMarketStreamOrder> orders, bool isBid)
	{
		var levels = orders.GroupBy(static order => order.Price)
			.Select(static group => new LunoStreamPriceLevel
			{
				Price = group.Key,
				Volume = group.Sum(static order => order.Volume),
			});
		return [.. (isBid
			? levels.OrderByDescending(static level => level.Price)
			: levels.OrderBy(static level => level.Price))];
	}

	private void ResetBook()
	{
		using (_sync.EnterScope())
		{
			_bids.Clear();
			_asks.Clear();
			_sequence = 0;
			_status = LunoStreamStatuses.Disabled;
		}
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		await StopHeartbeatAsync();
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
			ResetBook();
		}
	}

	private void StartHeartbeat()
	{
		_heartbeatCancellation = new();
		_heartbeatTask = RunHeartbeatAsync(_heartbeatCancellation.Token);
	}

	private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
				if (_client?.IsConnected == true)
					await _client.SendOpCode();
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
	}

	private async ValueTask StopHeartbeatAsync()
	{
		var cancellation = _heartbeatCancellation;
		_heartbeatCancellation = null;
		var task = _heartbeatTask;
		_heartbeatTask = null;
		if (cancellation is null)
			return;
		cancellation.Cancel();
		try
		{
			if (task is not null)
				await task;
		}
		finally
		{
			cancellation.Dispose();
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static string CreateEndpoint(string value, string path)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"Luno WebSocket endpoint must be an absolute WSS URI.",
				nameof(value));
		return new Uri(endpoint, path.TrimStart('/')).ToString();
	}
}
