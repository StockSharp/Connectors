namespace StockSharp.FalconX;

/// <summary>The message adapter for FalconX institutional trading.</summary>
[MediaIcon(Media.MediaNames.falconx)]
[Doc("topics/api/connectors/crypto_exchanges/falconx.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FalconXKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth)]
[OrderCondition(typeof(FalconXOrderCondition))]
public partial class FalconXMessageAdapter : MessageAdapter, IKeySecretAdapter, IPassphraseAdapter
{
	private sealed class MarketSubscription
	{
		public FalconXTokenPair Pair { get; init; }
		public DataType DataType { get; init; }
		public int Depth { get; init; }
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

	private sealed class PendingOrderRequest
	{
		public long TransactionId { get; init; }
		public long OrderTransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string PortfolioName { get; init; }
		public Sides? Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public FalconXOrderTypes? NativeOrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public string ClientOrderId { get; init; }
	}

	private sealed class TrackedOrder
	{
		public string OrderId { get; init; }
		public string ClientOrderId { get; set; }
		public long TransactionId { get; set; }
		public SecurityId SecurityId { get; set; }
		public Sides? Side { get; set; }
		public FalconXOrderTypes? OrderType { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, FalconXTokenPair> _pairs =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _marketSubscriptions =
		[];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<string, PendingOrderRequest> _pendingOrders =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenFills =
		new(StringComparer.OrdinalIgnoreCase);
	private FalconXAuthenticator _authenticator;
	private FalconXRestClient _restClient;
	private FalconXMarketSocketClient _marketSocket;
	private FalconXOrderSocketClient _orderSocket;
	private string _portfolioName;
	private DateTime _serverTime;
	private DateTime _nextPoll;

	/// <summary>Initializes a new instance.</summary>
	public FalconXMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
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
	public override string[] AssociatedBoards => [BoardCodes.FalconX];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.FalconX) ||
			securityId.IsAssociated(BoardCodes.FalconX);

	private FalconXRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private FalconXMarketSocketClient MarketSocket => _marketSocket ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private FalconXOrderSocketClient OrderSocket => _orderSocket ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private void UpdateServerTime(DateTime time)
	{
		time = time.EnsureUtc();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private void AddPair(FalconXTokenPair pair)
	{
		if (pair?.BaseToken.IsEmpty() != false || pair.QuoteToken.IsEmpty())
			throw new InvalidDataException(
				"FalconX returned a token pair without both tokens.");
		using (_sync.EnterScope())
			_pairs[pair.GetKey()] = pair;
	}

	private FalconXTokenPair GetCachedPair(string code)
	{
		if (code.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _pairs.TryGetValue(code, out var pair) ? pair : null;
	}

	private FalconXTokenPair GetPair(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode + "' is not FalconX.");
		var code = securityId.Native as string;
		if (code.IsEmpty())
			code = securityId.SecurityCode;
		var requested = code.ParseFalconXPair();
		var pair = GetCachedPair(requested.GetKey());
		return pair ?? throw new InvalidOperationException(
			$"FalconX token pair '{requested.GetKey()}' is not enabled for this API key.");
	}

	private void EnsureConnected()
	{
		if (_restClient is null || _marketSocket is null || _orderSocket is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.Equals(_portfolioName,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Unknown FalconX portfolio '" + portfolioName + "'.");
	}

	private void TrackOrder(string orderId, string clientOrderId,
		long transactionId, SecurityId securityId, Sides? side,
		FalconXOrderTypes? orderType)
	{
		if (orderId.IsEmpty())
			return;
		using (_sync.EnterScope())
		{
			if (!_trackedOrders.TryGetValue(orderId, out var order))
				_trackedOrders[orderId] = order = new()
				{
					OrderId = orderId,
				};
			if (!clientOrderId.IsEmpty())
				order.ClientOrderId = clientOrderId;
			if (transactionId != 0)
				order.TransactionId = transactionId;
			if (!securityId.SecurityCode.IsEmpty())
				order.SecurityId = securityId;
			if (side is not null)
				order.Side = side;
			if (orderType is not null)
				order.OrderType = orderType;
		}
	}

	private TrackedOrder GetTrackedOrder(string orderId)
	{
		using (_sync.EnterScope())
		{
			if (orderId.IsEmpty() || !_trackedOrders.TryGetValue(orderId,
				out var order))
				return null;
			return new()
			{
				OrderId = order.OrderId,
				ClientOrderId = order.ClientOrderId,
				TransactionId = order.TransactionId,
				SecurityId = order.SecurityId,
				Side = order.Side,
				OrderType = order.OrderType,
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
			_pairs.Clear();
			_marketSubscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_pendingOrders.Clear();
			_trackedOrders.Clear();
			_seenFills.Clear();
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
