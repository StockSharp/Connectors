namespace StockSharp.CoinDCX;

public partial class CoinDCXMessageAdapter
{
	private enum StreamTypes
	{
		Trades,
		Depth,
		Candles,
	}

	private sealed class MarketDefinition
	{
		public string Market { get; init; }
		public string Pair { get; init; }
		public string BaseAsset { get; init; }
		public string QuoteAsset { get; init; }
		public decimal PriceStep { get; init; }
		public decimal QuantityStep { get; init; }
		public decimal MinimumQuantity { get; init; }
		public decimal MaximumQuantity { get; init; }
		public decimal MaximumMarketQuantity { get; init; }
		public decimal MinimumPrice { get; init; }
		public decimal MaximumPrice { get; init; }
		public decimal MinimumNotional { get; init; }
		public bool IsTrading { get; init; }
		public HashSet<CoinDCXOrderTypes> OrderTypes { get; init; }
	}

	private class MarketSubscription
	{
		public string Market { get; init; }
		public string Pair { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
	}

	private sealed class OrderSubscription
	{
		public string Market { get; init; }
		public string OrderId { get; init; }
		public Sides? Side { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string Market { get; init; }
		public string ClientOrderId { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; set; }
	}

	private sealed class OrderBookState
	{
		public SortedDictionary<decimal, decimal> Bids { get; } =
			new(Comparer<decimal>.Create(static (left, right) =>
				right.CompareTo(left)));
		public SortedDictionary<decimal, decimal> Asks { get; } = [];
		public bool IsInitialized { get; set; }
		public long Version { get; set; }
	}

	private readonly record struct StreamKey(StreamTypes Type, string Pair,
		TimeSpan TimeFrame);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, MarketDefinition> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, MarketDefinition> _pairs =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, OrderBookState> _orderBooks =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _activeOrderIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPublicTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private CoinDCXRestClient _restClient;
	private CoinDCXSocketClient _socketClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="CoinDCXMessageAdapter"/> class.
	/// </summary>
	public CoinDCXMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.CoinDCX];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinDCX) ||
			securityId.IsAssociated(BoardCodes.CoinDCX);

	private CoinDCXRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private CoinDCXSocketClient SocketClient
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
				"CoinDCX API key and secret are required for private operations.");
	}

	private MarketDefinition GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinDCX) &&
			!securityId.IsAssociated(BoardCodes.CoinDCX))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not CoinDCX.");
		return GetMarket(securityId.SecurityCode);
	}

	private MarketDefinition GetMarket(string market)
	{
		market = market.NormalizeMarket();
		using (_sync.EnterScope())
			return _markets.TryGetValue(market, out var definition)
				? definition
				: throw new InvalidOperationException(
					$"Unknown CoinDCX market '{market}'.");
	}

	private MarketDefinition GetMarketByPair(string pair)
	{
		pair = pair.NormalizePair();
		using (_sync.EnterScope())
			return _pairs.TryGetValue(pair, out var definition)
				? definition
				: throw new InvalidOperationException(
					$"Unknown CoinDCX pair '{pair}'.");
	}

	private void RegisterMarkets(IEnumerable<CoinDCXMarket> markets)
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_pairs.Clear();
			foreach (var market in markets ?? [])
			{
				if (market?.Name.IsEmpty() != false || market.Pair.IsEmpty() ||
					market.BaseAsset.IsEmpty() || market.QuoteAsset.IsEmpty())
					continue;
				var definition = new MarketDefinition
				{
					Market = market.Name.NormalizeMarket(),
					Pair = market.Pair.NormalizePair(),
					BaseAsset = market.BaseAsset.ToUpperInvariant(),
					QuoteAsset = market.QuoteAsset.ToUpperInvariant(),
					PriceStep = GetStep(market.QuotePrecision),
					QuantityStep = market.QuantityStep > 0
						? market.QuantityStep
						: GetStep(market.BasePrecision),
					MinimumQuantity = market.MinimumQuantity,
					MaximumQuantity = market.MaximumQuantity,
					MaximumMarketQuantity = market.MaximumMarketQuantity,
					MinimumPrice = market.MinimumPrice,
					MaximumPrice = market.MaximumPrice,
					MinimumNotional = market.MinimumNotional,
					IsTrading = market.Status.EqualsIgnoreCase("active"),
					OrderTypes = [.. market.OrderTypes ?? []],
				};
				_markets[definition.Market] = definition;
				_pairs[definition.Pair] = definition;
			}
		}
	}

	private static decimal GetStep(int precision)
	{
		if (precision <= 0)
			return 1m;
		var value = 1m;
		for (var i = 0; i < precision.Min(28); i++)
			value /= 10m;
		return value;
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
		=> $"CoinDCX_{Key.ToId()}";

	private void TrackOrder(string orderId, TrackedOrder order)
	{
		if (orderId.IsEmpty() || order is null)
			return;
		using (_sync.EnterScope())
		{
			_trackedOrders[orderId] = order;
			_activeOrderIds.Add(orderId);
		}
	}

	private TrackedOrder GetTrackedOrder(string orderId)
	{
		if (orderId.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(orderId, out var order) ? order : null;
	}

	private bool AddPublicTrade(string pair, long timestamp, decimal price,
		decimal volume)
	{
		using (_sync.EnterScope())
			return _seenPublicTrades.Add(
				$"{pair}:{timestamp}:{price}:{volume}");
	}

	private bool AddAccountTrade(string tradeId)
	{
		if (tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
			return _seenAccountTrades.Add(tradeId);
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_pairs.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_orderBooks.Clear();
			_trackedOrders.Clear();
			_activeOrderIds.Clear();
			_seenPublicTrades.Clear();
			_seenAccountTrades.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
		}
	}

	private static string ResolveOrderId(long? numericOrderId, string stringOrderId,
		string operation)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId;
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			$"CoinDCX {operation} requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
