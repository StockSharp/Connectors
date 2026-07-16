namespace StockSharp.Etoro;

public partial class EtoroMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public int InstrumentId { get; init; }
	}

	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string PortfolioName { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Price { get; init; }
		public decimal Volume { get; init; }
		public EtoroOrderCondition Condition { get; init; }
	}

	private EtoroRestClient _rest;
	private EtoroWebSocketClient _stream;
	private readonly CachedSynchronizedDictionary<int, EtoroInstrument> _instruments = [];
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<long, OrderTracker> _orders = [];
	private readonly SynchronizedDictionary<string, (decimal volume, decimal commission, decimal pnl)> _executionStates =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly CachedSynchronizedSet<int> _portfolioInstruments = [];
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <summary>Initializes a new instance of the <see cref="EtoroMessageAdapter"/> class.</summary>
	public EtoroMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(EtoroExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override bool IsSupportExecutionsPnL => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [EtoroExtensions.BoardCode];

	private string PortfolioName => IsDemo ? "ETORO-DEMO" : "ETORO-REAL";

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_rest != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var apiKey = PublicApiKey?.UnSecure().ThrowIfEmpty(nameof(PublicApiKey));
		var userKey = UserKey?.UnSecure().ThrowIfEmpty(nameof(UserKey));
		var attempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);

		_rest = new(apiKey, userKey, attempts) { Parent = this };
		_stream = new(apiKey, userKey, attempts) { Parent = this };
		_stream.MessageReceived += OnStreamMessage;
		_stream.Error += SendOutErrorAsync;
		_stream.StateChanged += SendOutConnectionStateAsync;

		try
		{
			await _rest.Connect(cancellationToken);
			await _stream.Connect(cancellationToken);
			if (this.IsTransactional())
				await _stream.Subscribe(["private"], true, cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		await _stream.Disconnect();
		DisposeClients();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClients();
		_instruments.Clear();
		_marketSubscriptions.Clear();
		_orders.Clear();
		_executionStates.Clear();
		_portfolioInstruments.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void DisposeClients()
	{
		if (_stream != null)
		{
			_stream.MessageReceived -= OnStreamMessage;
			_stream.Error -= SendOutErrorAsync;
			_stream.StateChanged -= SendOutConnectionStateAsync;
			_stream.Dispose();
			_stream = null;
		}

		_rest?.Dispose();
		_rest = null;
	}

	private async ValueTask OnStreamMessage(EtoroSocketMessage message, CancellationToken cancellationToken)
	{
		if (message == null || message.Content.IsEmpty())
			return;

		try
		{
			if (message.Topic?.StartsWith("instrument:", StringComparison.OrdinalIgnoreCase) == true)
			{
				var rate = JsonConvert.DeserializeObject<EtoroRate>(message.Content);
				if (rate != null && int.TryParse(message.Topic["instrument:".Length..], NumberStyles.Integer,
					CultureInfo.InvariantCulture, out var instrumentId))
					await ProcessRate(instrumentId, rate, cancellationToken);
				return;
			}

			if (message.Topic.EqualsIgnoreCase("private"))
			{
				var update = JsonConvert.DeserializeObject<EtoroPrivateUpdate>(message.Content);
				if (update != null)
					await ProcessPrivateUpdate(message.Type, update, cancellationToken);
			}
		}
		catch (JsonException ex)
		{
			await SendOutErrorAsync(new InvalidDataException(
				$"Invalid eToro WebSocket payload for topic '{message.Topic}'.", ex), cancellationToken);
		}
	}

	private SecurityId GetSecurityId(int instrumentId, string symbol = null)
		=> _instruments.TryGetValue(instrumentId, out var instrument)
			? instrument.ToSecurityId()
			: new()
			{
				SecurityCode = symbol.IsEmpty(instrumentId > 0
					? instrumentId.ToString(CultureInfo.InvariantCulture) : null),
				BoardCode = EtoroExtensions.BoardCode,
				Native = instrumentId > 0 ? instrumentId : null,
			};
}
