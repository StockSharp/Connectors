namespace StockSharp.Paxos.Native;

using StockSharp.Paxos.Native.Model;

[JsonConverter(typeof(PaxosEnumConverter<PaxosSocketFeeds>))]
enum PaxosSocketFeeds
{
	[EnumMember(Value = "market_data")]
	MarketData,
	[EnumMember(Value = "execution_data")]
	Executions,
}

sealed class PaxosSocketClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly PaxosSocketFeeds _feed;
	private readonly string _market;
	private readonly Func<CancellationToken, ValueTask<string>> _getAccessToken;
	private readonly List<PaxosBookMessage> _pendingUpdates = [];
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly SemaphoreSlim _connectionGate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private WebSocketClient _client;
	private bool _isDisposed;
	private bool _hasSnapshot;

	public PaxosSocketClient(string endpoint, string market,
		PaxosSocketFeeds feed, WorkingTime workingTime, int reconnectAttempts,
		Func<CancellationToken, ValueTask<string>> getAccessToken = null)
	{
		market = market.ThrowIfEmpty(nameof(market)).Trim();
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/'), UriKind.Absolute,
			out var root) || root.Scheme != "wss" || root.Host.IsEmpty())
			throw new ArgumentException(
				"A valid secure WebSocket endpoint is required.", nameof(endpoint));
		_endpoint = root;
		_feed = feed;
		_market = market;
		_getAccessToken = getAccessToken;
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => _feed == PaxosSocketFeeds.MarketData
		? "Paxos_MarketData_WS"
		: "Paxos_Executions_WS";

	public event Func<PaxosBookMessage, CancellationToken, ValueTask>
		BookSnapshotReceived;
	public event Func<PaxosBookMessage, CancellationToken, ValueTask>
		BookUpdateReceived;
	public event Func<PaxosPublicExecution, CancellationToken, ValueTask>
		ExecutionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_getAccessToken is null)
			throw new InvalidOperationException("Paxos market data requires an OAuth token provider.");
		await _connectionGate.WaitAsync(cancellationToken);
		try
		{
			EnsureClient();
			if (!_client.IsConnected)
				await _client.ConnectAsync(cancellationToken);
		}
		finally
		{
			_connectionGate.Release();
		}
	}

	private void EnsureClient()
	{
		if (_client is not null)
			return;
		var client = new WebSocketClient(_endpoint.AbsoluteUri,
			OnStateChangedAsync, RaiseErrorAsync, OnProcessAsync,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		client.InitAsync += async (socket, cancellationToken) =>
		{
			var token = await _getAccessToken(cancellationToken);
			if (token.IsEmpty())
				throw new InvalidOperationException("Paxos returned no OAuth access token.");
			socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
			socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Paxos/1.0");
		};
		_client = client;
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		this.AddInfoLog("{0} state: {1}.", Name, state);
		if (state is ConnectionStates.Connected or ConnectionStates.Restored)
		{
			_hasSnapshot = false;
			_pendingUpdates.Clear();
			await _client.SendAsync(new PaxosSocketEnvelope
			{
				Type = PaxosSocketOperations.Subscribe,
				Channels = [new() { Type = _feed, Parameters = new() { Market = _market } }],
			}, cancellationToken);
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var json = message.AsString();
		if (json.IsEmpty())
			return;
		try
		{
			var envelope = JsonConvert.DeserializeObject<PaxosSocketEnvelope>(json,
				_settings) ?? throw new InvalidDataException("Paxos returned an empty WebSocket message.");
			if (envelope.Type is { } operation)
			{
				if (operation == PaxosSocketOperations.Error)
					throw new InvalidOperationException($"Paxos WebSocket error: {envelope.Error}");
				var channel = envelope.Channels?.SingleOrDefault(item =>
					item.Type == _feed && item.Parameters?.Market.EqualsIgnoreCase(_market) == true);
				if (channel?.Success != true)
					throw new InvalidOperationException($"Paxos WebSocket subscription failed: {channel?.Error ?? "missing acknowledgement"}");
				return;
			}
			if (envelope.Channel != _feed || envelope.Payload is null)
				throw new InvalidDataException("Paxos returned an unexpected WebSocket channel or missing payload.");
			var serializer = JsonSerializer.Create(_settings);
			if (_feed == PaxosSocketFeeds.Executions)
			{
				var execution = envelope.Payload.ToObject<PaxosPublicExecution>(
					serializer) ?? throw new InvalidDataException(
						"Paxos execution WebSocket returned an empty message.");
				if (!execution.Market.EqualsIgnoreCase(_market))
					throw new InvalidDataException(
						"Paxos execution WebSocket returned an incomplete message.");
				if (ExecutionReceived is { } executionHandler)
					await executionHandler.InvokeAsync(execution, cancellationToken);
				return;
			}

			var book = envelope.Payload.ToObject<PaxosBookMessage>(serializer) ??
				throw new InvalidDataException("Paxos returned an empty order-book message.");
			if (!book.Market.EqualsIgnoreCase(_market))
				throw new InvalidDataException("Paxos returned an unexpected order-book market.");
			var timestamp = ReadBookTimestamp(book.Time);
			switch (book.Type)
			{
				case PaxosSocketMessageTypes.Snapshot:
				{
					if (BookSnapshotReceived is { } snapshotHandler)
						await snapshotHandler.InvokeAsync(book, cancellationToken);
					_hasSnapshot = true;
					foreach (var update in _pendingUpdates)
					{
						if (ReadBookTimestamp(update.Time) > timestamp && BookUpdateReceived is { } updateHandler)
							await updateHandler.InvokeAsync(update, cancellationToken);
					}
					_pendingUpdates.Clear();
					break;
				}
				case PaxosSocketMessageTypes.Update:
				{
					if (!_hasSnapshot)
						_pendingUpdates.Add(book);
					else if (BookUpdateReceived is { } updateHandler)
						await updateHandler.InvokeAsync(book, cancellationToken);
					break;
				}
				default:
					throw new InvalidDataException(
						$"Unsupported Paxos WebSocket message type '{book.Type}'.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or FormatException or InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private static decimal ReadBookTimestamp(string value)
	{
		if (value.IsEmpty())
			throw new InvalidDataException("Paxos order-book timestamp is missing.");
		var whole = value;
		var fraction = 0m;
		var point = value.IndexOf('.');
		if (point >= 0)
		{
			var end = point + 1;
			while (end < value.Length && char.IsAsciiDigit(value[end]))
				end++;
			if (end == point + 1 || end - point - 1 > 9)
				throw new InvalidDataException("Paxos returned an invalid fractional timestamp.");
			fraction = decimal.Parse(value[point..end], NumberStyles.AllowDecimalPoint,
				CultureInfo.InvariantCulture);
			whole = value.Remove(point, end - point);
		}
		if (!DateTime.TryParse(whole, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
			throw new InvalidDataException("Paxos returned an invalid order-book timestamp.");
		// Preserve sub-tick precision when deciding which buffered updates follow the snapshot.
		return timestamp.Ticks + fraction * TimeSpan.TicksPerSecond;
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler.InvokeAsync(error, cancellationToken) : default;

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
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

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
		_connectionGate.Dispose();
		base.DisposeManaged();
	}
}
