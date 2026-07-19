namespace StockSharp.Bitso;

public partial class BitsoMessageAdapter
{
	private sealed class MarketDefinition
	{
		public string Book { get; init; }
		public string Major { get; init; }
		public string Minor { get; init; }
		public decimal PriceStep { get; init; }
		public decimal MinimumAmount { get; init; }
		public decimal MaximumAmount { get; init; }
		public decimal MinimumValue { get; init; }
		public decimal MaximumValue { get; init; }
		public bool IsMarginEnabled { get; init; }
	}

	private class MarketSubscription
	{
		public string Book { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string Book { get; init; }
		public string OriginId { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public decimal? StopPrice { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public bool IsPostOnly { get; init; }
	}

	private readonly record struct StreamKey(BitsoWsChannels Channel, string Book);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, MarketDefinition> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _knownActiveOrderIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenTradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPublicTradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, decimal> _filledVolumes =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _privatePollSync = new(1, 1);
	private BitsoRestClient _restClient;
	private BitsoWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPrivatePoll;

	/// <summary>
	/// Initializes a new instance of the <see cref="BitsoMessageAdapter"/> class.
	/// </summary>
	public BitsoMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Bitso];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bitso) ||
			securityId.IsAssociated(BoardCodes.Bitso);

	private BitsoRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private BitsoWsClient WsClient
		=> _wsClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _wsClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Bitso API key and secret are required for private operations.");
	}

	private MarketDefinition GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bitso) &&
			!securityId.IsAssociated(BoardCodes.Bitso))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Bitso.");
		var book = securityId.SecurityCode.NormalizeBook();
		using (_sync.EnterScope())
			return _markets.TryGetValue(book, out var market)
				? market
				: throw new InvalidOperationException($"Unknown Bitso book '{book}'.");
	}

	private string[] GetBooks()
	{
		using (_sync.EnterScope())
			return [.. _markets.Keys];
	}

	private void RegisterMarkets(IEnumerable<BitsoBook> books)
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var book in books ?? [])
			{
				if (book?.Name.IsEmpty() != false)
					continue;
				var name = book.Name.NormalizeBook();
				var (major, minor) = name.SplitBook();
				_markets[name] = new()
				{
					Book = name,
					Major = major,
					Minor = minor,
					PriceStep = book.TickSize,
					MinimumAmount = book.MinimumAmount,
					MaximumAmount = book.MaximumAmount,
					MinimumValue = book.MinimumValue,
					MaximumValue = book.MaximumValue,
					IsMarginEnabled = book.IsMarginEnabled,
				};
			}
		}
	}

	private static bool AddReference(IDictionary<StreamKey, int> references,
		StreamKey key)
	{
		if (references.TryGetValue(key, out var count))
		{
			references[key] = count + 1;
			return false;
		}
		references.Add(key, 1);
		return true;
	}

	private static bool ReleaseReference(IDictionary<StreamKey, int> references,
		StreamKey key)
	{
		if (!references.TryGetValue(key, out var count))
			return false;
		if (count > 1)
		{
			references[key] = count - 1;
			return false;
		}
		references.Remove(key);
		return true;
	}

	private string GetPortfolioName()
		=> $"Bitso_{Key.ToId()}";

	private void TrackOrder(string orderId, TrackedOrder order)
	{
		if (orderId.IsEmpty() || order is null)
			return;
		using (_sync.EnterScope())
		{
			_trackedOrders[orderId] = order;
			_knownActiveOrderIds.Add(orderId);
		}
	}

	private TrackedOrder GetTrackedOrder(string orderId)
	{
		if (orderId.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(orderId, out var order) ? order : null;
	}

	private bool AddTrade(string tradeId)
	{
		if (tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
			return _seenTradeIds.Add(tradeId);
	}

	private bool AddPublicTrade(string book, string tradeId)
	{
		if (book.IsEmpty() || tradeId.IsEmpty())
			return true;
		using (_sync.EnterScope())
			return _seenPublicTradeIds.Add($"{book}:{tradeId}");
	}

	private decimal AddFilledVolume(string orderId, decimal volume)
	{
		if (orderId.IsEmpty() || volume <= 0)
			return 0m;
		using (_sync.EnterScope())
		{
			var total = _filledVolumes.TryGetValue(orderId, out var current)
				? current + volume
				: volume;
			_filledVolumes[orderId] = total;
			return total;
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_streamReferences.Clear();
			_trackedOrders.Clear();
			_knownActiveOrderIds.Clear();
			_seenTradeIds.Clear();
			_seenPublicTradeIds.Clear();
			_filledVolumes.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPrivatePoll = default;
	}

	private static string ResolveOrderId(long? numericOrderId, string stringOrderId,
		string operation)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId;
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			$"Bitso {operation} requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_privatePollSync.Dispose();
		base.DisposeManaged();
	}
}
