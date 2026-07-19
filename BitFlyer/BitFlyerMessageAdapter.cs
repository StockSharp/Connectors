namespace StockSharp.BitFlyer;

public partial class BitFlyerMessageAdapter
{
	private enum StreamTypes
	{
		Ticker,
		Executions,
		Board,
	}

	private sealed class MarketDefinition
	{
		public string ProductCode { get; init; }
		public string BaseAsset { get; init; }
		public string QuoteAsset { get; init; }
		public BitFlyerMarketTypes Type { get; init; }
		public BitFlyerMarketStates State { get; set; }
	}

	private class MarketSubscription
	{
		public string ProductCode { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class BookState
	{
		public SortedDictionary<decimal, decimal> Bids { get; } =
			new(Comparer<decimal>.Create(static (left, right) =>
				right.CompareTo(left)));
		public SortedDictionary<decimal, decimal> Asks { get; } = [];
		public bool IsInitialized { get; set; }
	}

	private sealed class OrderSubscription
	{
		public string ProductCode { get; init; }
		public string OrderId { get; init; }
		public Sides? Side { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string ProductCode { get; init; }
		public string AcceptanceId { get; set; }
		public string NativeOrderId { get; set; }
		public long? NumericId { get; set; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public bool IsParent { get; init; }
		public BitFlyerOrderCondition Condition { get; init; }
	}

	private readonly record struct StreamKey(StreamTypes Type,
		string ProductCode);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, MarketDefinition> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BookState> _books =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPublicTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private BitFlyerRestClient _restClient;
	private BitFlyerSocketClient _socketClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="BitFlyerMessageAdapter"/> class.
	/// </summary>
	public BitFlyerMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override bool IsReplaceCommandEditCurrent => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.BitFlyer];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BitFlyer) ||
			securityId.IsAssociated(BoardCodes.BitFlyer);

	private BitFlyerRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private BitFlyerSocketClient SocketClient
		=> _socketClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _socketClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"bitFlyer API key and secret are required for private operations.");
	}

	private void RegisterMarkets(IEnumerable<BitFlyerMarket> markets)
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets ?? [])
			{
				if (market?.ProductCode.IsEmpty() != false)
					continue;
				var productCode = market.ProductCode.NormalizeProductCode();
				var parts = productCode.Split('_',
					StringSplitOptions.RemoveEmptyEntries);
				var offset = market.Type == BitFlyerMarketTypes.Fx &&
					parts.Length >= 3 && parts[0].Equals("FX",
						StringComparison.OrdinalIgnoreCase)
						? 1
						: 0;
				if (parts.Length - offset < 2)
					continue;
				_markets[productCode] = new()
				{
					ProductCode = productCode,
					BaseAsset = parts[offset],
					QuoteAsset = parts[offset + 1],
					Type = market.Type,
					State = BitFlyerMarketStates.Running,
				};
			}
		}
	}

	private MarketDefinition GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BitFlyer) &&
			!securityId.IsAssociated(BoardCodes.BitFlyer))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not bitFlyer.");
		return GetMarket(securityId.SecurityCode);
	}

	private MarketDefinition GetMarket(string productCode)
	{
		productCode = productCode.NormalizeProductCode();
		using (_sync.EnterScope())
		{
			if (_markets.TryGetValue(productCode, out var market))
				return market;
			var compact = productCode.CompactProductCode();
			market = _markets.Values.FirstOrDefault(value =>
				value.ProductCode.CompactProductCode().Equals(compact,
					StringComparison.OrdinalIgnoreCase));
			return market ?? throw new InvalidOperationException(
				$"Unknown bitFlyer product '{productCode}'.");
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
		=> $"BitFlyer_{Key.ToId()}";

	private void TrackOrder(TrackedOrder order, params string[] identifiers)
	{
		if (order is null)
			return;
		using (_sync.EnterScope())
		{
			foreach (var identifier in identifiers.Where(static value =>
				!value.IsEmpty()))
				_trackedOrders[identifier] = order;
			if (!order.AcceptanceId.IsEmpty())
				_trackedOrders[order.AcceptanceId] = order;
			if (!order.NativeOrderId.IsEmpty())
				_trackedOrders[order.NativeOrderId] = order;
			if (order.NumericId is > 0)
				_trackedOrders[order.NumericId.Value.ToString(
					CultureInfo.InvariantCulture)] = order;
		}
	}

	private TrackedOrder GetTrackedOrder(string identifier)
	{
		if (identifier.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(identifier, out var order)
				? order
				: null;
	}

	private bool AddPublicTrade(string productCode, long tradeId)
	{
		if (tradeId <= 0)
			return false;
		using (_sync.EnterScope())
		{
			if (_seenPublicTrades.Count > 100000)
				_seenPublicTrades.Clear();
			return _seenPublicTrades.Add(
				$"{productCode}:{tradeId.ToString(CultureInfo.InvariantCulture)}");
		}
	}

	private bool AddAccountTrade(long tradeId)
	{
		if (tradeId <= 0)
			return false;
		using (_sync.EnterScope())
		{
			if (_seenAccountTrades.Count > 100000)
				_seenAccountTrades.Clear();
			return _seenAccountTrades.Add(
				tradeId.ToString(CultureInfo.InvariantCulture));
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_books.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_streamReferences.Clear();
			_trackedOrders.Clear();
			_seenPublicTrades.Clear();
			_seenAccountTrades.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
		}
	}

	private static string ResolveOrderIdentifier(long? numericOrderId,
		string stringOrderId, string operation)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId.Trim();
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			$"bitFlyer {operation} requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
