namespace StockSharp.ZeroHash;

/// <summary>The message adapter for the Zero Hash central limit order book.</summary>
[MediaIcon(Media.MediaNames.zerohash)]
[Doc("topics/api/connectors/crypto_exchanges/zerohash.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ZeroHashKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth)]
[OrderCondition(typeof(ZeroHashOrderCondition))]
public partial class ZeroHashMessageAdapter : MessageAdapter, IKeySecretAdapter, IPassphraseAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public ZeroHashInstrument Instrument { get; init; }
		public DataType DataType { get; init; }
		public int Depth { get; init; }
		public CancellationTokenSource Cancellation { get; init; }
		public Task Task { get; set; }
		public DateTime LastTradeTime { get; set; }
		public string LastTradeKey { get; set; }
	}

	private sealed class OrderSubscription
	{
		public SecurityId SecurityId { get; init; }
		public SecurityId[] SecurityIds { get; init; }
		public string OrderId { get; init; }
		public Sides? Side { get; init; }
		public decimal? Volume { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Limit { get; init; }
	}

	private sealed class TrackedOrder
	{
		public string OrderId { get; init; }
		public string ClientOrderId { get; set; }
		public long TransactionId { get; set; }
		public SecurityId SecurityId { get; set; }
		public Sides? Side { get; set; }
		public ZeroHashOrderTypes? Type { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, ZeroHashInstrument> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _marketSubscriptions =
		[];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TrackedOrder> _clientOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _pendingCancels =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenOrderUpdates =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenExecutions =
		new(StringComparer.OrdinalIgnoreCase);
	private ZeroHashAuthenticator _authenticator;
	private ZeroHashRestClient _restClient;
	private CancellationTokenSource _connectionCancellation;
	private CancellationTokenSource _orderStreamCancellation;
	private Task _orderStreamTask;
	private string _portfolioName;
	private DateTime _serverTime;
	private DateTime _nextPoll;

	/// <summary>Initializes a new instance.</summary>
	public ZeroHashMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.ZeroHash];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.ZeroHash) ||
			securityId.IsAssociated(BoardCodes.ZeroHash);

	private ZeroHashRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private string AccountCode
	{
		get
		{
			var account = Account?.Trim();
			if (account.IsEmpty())
				return null;
			var separator = account.LastIndexOf('/');
			return separator < 0 ? account : account[(separator + 1)..];
		}
	}

	private void UpdateServerTime(DateTime time)
	{
		time = time.EnsureUtc();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private void AddInstrument(ZeroHashInstrument instrument)
	{
		if (instrument?.Symbol.IsEmpty() != false)
			throw new InvalidDataException(
				"Zero Hash returned an instrument without a symbol.");
		_ = instrument.GetPriceScale();
		_ = instrument.GetQuantityScale();
		using (_sync.EnterScope())
			_instruments[instrument.Symbol] = instrument;
	}

	private ZeroHashInstrument GetInstrument(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode + "' is not Zero Hash.");
		var symbol = securityId.Native as string;
		if (symbol.IsEmpty())
			symbol = securityId.SecurityCode;
		using (_sync.EnterScope())
			return !symbol.IsEmpty() && _instruments.TryGetValue(symbol, out var value)
				? value
				: throw new InvalidOperationException(
					"Unknown Zero Hash CLOB symbol '" + symbol + "'.");
	}

	private ZeroHashInstrument GetInstrument(string symbol)
	{
		using (_sync.EnterScope())
			return !symbol.IsEmpty() && _instruments.TryGetValue(symbol, out var value)
				? value
				: null;
	}

	private void EnsureConnected()
	{
		if (_restClient is null || _connectionCancellation is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTradingConfigured()
	{
		if (Account.IsEmpty() || User.IsEmpty() || _portfolioName.IsEmpty())
			throw new InvalidOperationException(
				"Zero Hash account and fully-qualified user are required for private operations.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		EnsureTradingConfigured();
		if (!portfolioName.IsEmpty() && !portfolioName.Equals(_portfolioName,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Unknown Zero Hash portfolio '" + portfolioName + "'.");
	}

	private void TrackOrder(string orderId, string clientOrderId,
		long transactionId, SecurityId securityId, Sides? side,
		ZeroHashOrderTypes? type)
	{
		if (orderId.IsEmpty() && clientOrderId.IsEmpty())
			return;
		using (_sync.EnterScope())
		{
			TrackedOrder order = null;
			if (!orderId.IsEmpty())
				_trackedOrders.TryGetValue(orderId, out order);
			if (order is null && !clientOrderId.IsEmpty())
				_clientOrders.TryGetValue(clientOrderId, out order);
			if (order is null)
				order = new() { OrderId = orderId };
			if (!clientOrderId.IsEmpty())
				order.ClientOrderId = clientOrderId;
			if (transactionId != 0)
				order.TransactionId = transactionId;
			if (!securityId.SecurityCode.IsEmpty())
				order.SecurityId = securityId;
			if (side is not null)
				order.Side = side;
			if (type is not null)
				order.Type = type;
			if (!orderId.IsEmpty())
				_trackedOrders[orderId] = order;
			if (!order.ClientOrderId.IsEmpty())
				_clientOrders[order.ClientOrderId] = order;
		}
	}

	private TrackedOrder GetTrackedOrder(string orderId, string clientOrderId)
	{
		using (_sync.EnterScope())
		{
			TrackedOrder order = null;
			if (!orderId.IsEmpty())
				_trackedOrders.TryGetValue(orderId, out order);
			if (order is null && !clientOrderId.IsEmpty())
				_clientOrders.TryGetValue(clientOrderId, out order);
			return order is null ? null : new()
			{
				OrderId = order.OrderId,
				ClientOrderId = order.ClientOrderId,
				TransactionId = order.TransactionId,
				SecurityId = order.SecurityId,
				Side = order.Side,
				Type = order.Type,
			};
		}
	}

	private void SchedulePoll()
	{
		using (_sync.EnterScope())
			_nextPoll = DateTime.UtcNow;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			_marketSubscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_trackedOrders.Clear();
			_clientOrders.Clear();
			_pendingCancels.Clear();
			_seenOrderUpdates.Clear();
			_seenExecutions.Clear();
			_serverTime = default;
			_nextPoll = default;
		}
		_portfolioName = null;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
