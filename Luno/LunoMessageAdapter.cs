namespace StockSharp.Luno;

public partial class LunoMessageAdapter
{
	private enum StreamTypes
	{
		Level1,
		OrderBook,
		Trade,
		Candle,
	}

	private sealed class MarketDefinition
	{
		public string Symbol { get; init; }
		public string BaseAsset { get; init; }
		public string QuoteAsset { get; init; }
		public decimal MinimumVolume { get; init; }
		public decimal MaximumVolume { get; init; }
		public decimal MinimumPrice { get; init; }
		public decimal MaximumPrice { get; init; }
		public decimal VolumeStep { get; init; }
		public decimal PriceStep { get; init; }
		public LunoTradingStatuses Status { get; init; }
	}

	private class MarketSubscription
	{
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public DateTime OpenTime { get; set; }
		public decimal Open { get; set; }
		public decimal High { get; set; }
		public decimal Low { get; set; }
		public decimal Close { get; set; }
		public decimal Volume { get; set; }
	}

	private sealed class OrderSubscription
	{
		public string Symbol { get; init; }
		public string OrderId { get; init; }
		public Sides? Side { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string Symbol { get; init; }
		public string ExchangeOrderId { get; init; }
		public string CustomerOrderId { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public bool IsPostOnly { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public LunoOrderCondition Condition { get; init; }
	}

	private sealed class MarketStreamHolder
	{
		public LunoMarketSocketClient Client { get; init; }
		public int References { get; set; }
	}

	private readonly record struct StreamKey(StreamTypes Type, string Symbol);
	private readonly record struct BalanceFingerprint(decimal Balance,
		decimal Reserved, decimal Unconfirmed);
	private readonly record struct OrderFingerprint(LunoOrderStatuses Status,
		decimal Filled, decimal Volume, long CompletedTime);

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _marketStreamGate = new(1, 1);
	private readonly Dictionary<string, MarketDefinition> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, MarketStreamHolder> _marketStreams =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPublicTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<string, string> _accountAssets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BalanceFingerprint> _balanceFingerprints =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
		new(StringComparer.OrdinalIgnoreCase);
	private LunoRestClient _restClient;
	private LunoUserSocketClient _userSocketClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="LunoMessageAdapter"/> class.
	/// </summary>
	public LunoMessageAdapter(IdGenerator transactionIdGenerator)
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
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Luno];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Luno) ||
			securityId.IsAssociated(BoardCodes.Luno);

	private LunoRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Luno API key and secret are required for this operation.");
	}

	private string GetPortfolioName() => "LUNO";

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown Luno portfolio '{portfolioName}'.");
	}

	private void RegisterMarkets(IEnumerable<LunoMarketInfo> markets)
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets ?? [])
			{
				if (market?.MarketId.IsEmpty() != false ||
					market.BaseCurrency.IsEmpty() ||
					market.CounterCurrency.IsEmpty())
					continue;
				var definition = new MarketDefinition
				{
					Symbol = market.MarketId.NormalizeSymbol(),
					BaseAsset = market.BaseCurrency.ToUpperInvariant(),
					QuoteAsset = market.CounterCurrency.ToUpperInvariant(),
					MinimumVolume = market.MinimumVolume,
					MaximumVolume = market.MaximumVolume,
					MinimumPrice = market.MinimumPrice,
					MaximumPrice = market.MaximumPrice,
					VolumeStep = Pow10Step(market.VolumeScale),
					PriceStep = Pow10Step(market.PriceScale),
					Status = market.TradingStatus,
				};
				_markets[definition.Symbol] = definition;
			}
		}
	}

	private static decimal Pow10Step(int scale)
	{
		if (scale is < 0 or > 28)
			return 0m;
		var value = 1m;
		for (var i = 0; i < scale; i++)
			value /= 10m;
		return value;
	}

	private MarketDefinition GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Luno) &&
			!securityId.IsAssociated(BoardCodes.Luno))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Luno.");
		return GetMarket(securityId.SecurityCode);
	}

	private MarketDefinition GetMarket(string symbol)
	{
		symbol = symbol.NormalizeSymbol();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Luno market '{symbol}'.");
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

	private void TrackOrder(TrackedOrder order, params string[] identifiers)
	{
		if (order is null)
			return;
		using (_sync.EnterScope())
			foreach (var identifier in identifiers.Where(static value =>
				!value.IsEmpty()))
				_trackedOrders[identifier] = order;
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

	private bool AddPublicTrade(string identity, long transactionId)
	{
		using (_sync.EnterScope())
		{
			if (_seenPublicTrades.Count > 100000)
				_seenPublicTrades.Clear();
			return _seenPublicTrades.Add($"{transactionId}:{identity}");
		}
	}

	private bool AddAccountTrade(string identity, long transactionId)
	{
		using (_sync.EnterScope())
		{
			if (_seenAccountTrades.Count > 100000)
				_seenAccountTrades.Clear();
			return _seenAccountTrades.Add($"{transactionId}:{identity}");
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
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_marketStreams.Clear();
			_trackedOrders.Clear();
			_seenPublicTrades.Clear();
			_seenAccountTrades.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_accountAssets.Clear();
			_balanceFingerprints.Clear();
			_orderFingerprints.Clear();
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
			$"Luno {operation} requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_marketStreamGate.Dispose();
		base.DisposeManaged();
	}
}
