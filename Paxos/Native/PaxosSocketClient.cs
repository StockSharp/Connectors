namespace StockSharp.Paxos.Native;

using StockSharp.Paxos.Native.Model;

enum PaxosSocketFeeds
{
	MarketData,
	Executions,
}

sealed class PaxosSocketClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly PaxosSocketFeeds _feed;
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

	public PaxosSocketClient(string endpoint, string market,
		PaxosSocketFeeds feed, WorkingTime workingTime, int reconnectAttempts)
	{
		market = market.ThrowIfEmpty(nameof(market)).Trim();
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/'), UriKind.Absolute,
			out var root) || root.Scheme != "wss" || root.Host.IsEmpty())
			throw new ArgumentException(
				"A valid secure WebSocket endpoint is required.", nameof(endpoint));
		var path = feed == PaxosSocketFeeds.MarketData
			? "marketdata"
			: "executiondata";
		_endpoint = new(root.AbsoluteUri.TrimEnd('/') + "/" + path + "/" +
			market.DataEscape(), UriKind.Absolute);
		_feed = feed;
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => _feed == PaxosSocketFeeds.MarketData
		? "Paxos_MarketData_WS"
		: "Paxos_Executions_WS";

	public event Func<PaxosBookSnapshot, CancellationToken, ValueTask>
		BookSnapshotReceived;
	public event Func<PaxosBookUpdate, CancellationToken, ValueTask>
		BookUpdateReceived;
	public event Func<PaxosPublicExecution, CancellationToken, ValueTask>
		ExecutionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
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
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Paxos/1.0");
		_client = client;
	}

	private ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		this.AddInfoLog("{0} state: {1}.", Name, state);
		return default;
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
			if (_feed == PaxosSocketFeeds.Executions)
			{
				var execution = JsonConvert.DeserializeObject<PaxosPublicExecution>(
					json, _settings) ?? throw new InvalidDataException(
						"Paxos execution WebSocket returned an empty message.");
				if (execution.Market.IsEmpty() || execution.MatchNumber.IsEmpty())
					throw new InvalidDataException(
						"Paxos execution WebSocket returned an incomplete message.");
				if (ExecutionReceived is { } executionHandler)
					await executionHandler(execution, cancellationToken);
				return;
			}

			var envelope = JsonConvert.DeserializeObject<PaxosSocketEnvelope>(json,
				_settings) ?? throw new InvalidDataException(
					"Paxos market-data WebSocket returned an empty message.");
			switch (envelope.Type)
			{
				case PaxosSocketMessageTypes.Snapshot:
				{
					var snapshot = JsonConvert.DeserializeObject<PaxosBookSnapshot>(
						json, _settings) ?? throw new InvalidDataException(
							"Paxos returned an empty order-book snapshot.");
					if (BookSnapshotReceived is { } snapshotHandler)
						await snapshotHandler(snapshot, cancellationToken);
					break;
				}
				case PaxosSocketMessageTypes.Update:
				{
					var update = JsonConvert.DeserializeObject<PaxosBookUpdate>(json,
						_settings) ?? throw new InvalidDataException(
							"Paxos returned an empty order-book update.");
					if (BookUpdateReceived is { } updateHandler)
						await updateHandler(update, cancellationToken);
					break;
				}
				default:
					throw new InvalidDataException(
						$"Unsupported Paxos WebSocket message type '{envelope.Type}'.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or FormatException or InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

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
