namespace StockSharp.BitGo;

/// <summary>The message adapter for BitGo Prime trading.</summary>
[MediaIcon(Media.MediaNames.bitgo)]
[Doc("topics/api/connectors/crypto_exchanges/bitgo.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitGoKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth)]
[OrderCondition(typeof(BitGoOrderCondition))]
public partial class BitGoMessageAdapter : MessageAdapter, ITokenAdapter
{
	private sealed class MarketSubscription
	{
		public BitGoProduct Product { get; init; }
		public DataType DataType { get; init; }
		public int Depth { get; init; }
	}

	private sealed class BookState
	{
		public SortedDictionary<decimal, decimal> Bids { get; } =
			new(Comparer<decimal>.Create(static (left, right) =>
				right.CompareTo(left)));
		public SortedDictionary<decimal, decimal> Asks { get; } = [];
		public DateTime Time { get; set; }
		public bool IsInitialized { get; set; }
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
		public string OrderId { get; set; }
		public string ClientOrderId { get; set; }
		public long TransactionId { get; set; }
		public SecurityId SecurityId { get; set; }
		public Sides? Side { get; set; }
		public BitGoOrderTypes? Type { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, BitGoProduct> _products =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _marketSubscriptions =
		[];
	private readonly Dictionary<string, BookState> _books =
		new(StringComparer.OrdinalIgnoreCase);
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
	private readonly HashSet<string> _seenTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private BitGoRestClient _restClient;
	private BitGoSocketClient _socketClient;
	private BitGoAccount _selectedAccount;
	private string _portfolioName;
	private DateTime _serverTime;
	private DateTime _nextPoll;

	/// <summary>Initializes a new instance.</summary>
	public BitGoMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.BitGo];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BitGo) ||
			securityId.IsAssociated(BoardCodes.BitGo);

	private BitGoRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private BitGoSocketClient SocketClient => _socketClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private string AccountId => _selectedAccount?.Id ?? throw new
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

	private void AddProduct(BitGoProduct product)
	{
		if (product?.GetKey().IsEmpty() != false || product.Name.IsEmpty() ||
			(product.BaseCurrency.IsEmpty() && product.BaseCurrencyId.IsEmpty()) ||
			(product.QuoteCurrency.IsEmpty() && product.QuoteCurrencyId.IsEmpty()))
			throw new InvalidDataException(
				"BitGo returned an incomplete trading product definition.");
		using (_sync.EnterScope())
		{
			_products[product.GetKey()] = product;
			_products[product.Name] = product;
			if (!product.Id.IsEmpty())
				_products[product.Id] = product;
		}
	}

	private BitGoProduct GetProduct(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode + "' is not BitGo.");
		var code = securityId.Native as string;
		if (code.IsEmpty())
			code = securityId.SecurityCode;
		return GetProduct(code) ?? throw new InvalidOperationException(
			"Unknown BitGo product '" + code + "'.");
	}

	private BitGoProduct GetProduct(string code)
	{
		using (_sync.EnterScope())
			return !code.IsEmpty() && _products.TryGetValue(code, out var product)
				? product
				: null;
	}

	private BitGoProduct[] GetProducts()
	{
		using (_sync.EnterScope())
			return [.. _products.Values.Distinct()
				.OrderBy(static product => product.Name,
					StringComparer.OrdinalIgnoreCase)];
	}

	private void EnsureConnected()
	{
		if (_restClient is null || _socketClient is null ||
			_selectedAccount is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.Equals(_portfolioName,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Unknown BitGo portfolio '" + portfolioName + "'.");
	}

	private void TrackOrder(string orderId, string clientOrderId,
		long transactionId, SecurityId securityId, Sides? side,
		BitGoOrderTypes? type)
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
			order ??= new();
			if (!orderId.IsEmpty())
				order.OrderId = orderId;
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
			if (!order.OrderId.IsEmpty())
				_trackedOrders[order.OrderId] = order;
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
			return CloneOrder(order);
		}
	}

	private TrackedOrder GetTrackedOrder(long transactionId)
	{
		using (_sync.EnterScope())
			return CloneOrder(_trackedOrders.Values.FirstOrDefault(order =>
				order.TransactionId == transactionId));
	}

	private static TrackedOrder CloneOrder(TrackedOrder order)
		=> order is null ? null : new()
		{
			OrderId = order.OrderId,
			ClientOrderId = order.ClientOrderId,
			TransactionId = order.TransactionId,
			SecurityId = order.SecurityId,
			Side = order.Side,
			Type = order.Type,
		};

	private void SchedulePoll()
	{
		using (_sync.EnterScope())
			_nextPoll = DateTime.UtcNow;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_products.Clear();
			_marketSubscriptions.Clear();
			_books.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_trackedOrders.Clear();
			_clientOrders.Clear();
			_pendingCancels.Clear();
			_seenOrderUpdates.Clear();
			_seenTrades.Clear();
			_serverTime = default;
			_nextPoll = default;
		}
		_selectedAccount = null;
		_portfolioName = null;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
