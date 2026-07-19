namespace StockSharp.VALR;

public partial class VALRMessageAdapter
{
	private enum StreamTypes
	{
		MarketSummary,
		OrderBook,
		Trade,
		Candle,
	}

	private sealed class MarketDefinition
	{
		public string Symbol { get; init; }
		public string Name { get; init; }
		public string BaseAsset { get; init; }
		public string QuoteAsset { get; init; }
		public decimal PriceStep { get; init; }
		public decimal VolumeStep { get; init; }
		public decimal MinimumVolume { get; init; }
		public decimal MaximumVolume { get; init; }
		public decimal MinimumCost { get; init; }
		public decimal MaximumCost { get; init; }
		public bool IsMarginAllowed { get; init; }
		public bool IsFuture { get; init; }
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
		public string ExchangeOrderId { get; set; }
		public string CustomerOrderId { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public bool IsPostOnly { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public VALROrderCondition Condition { get; init; }
	}

	private readonly record struct StreamKey(StreamTypes Type, string Symbol);
	private readonly record struct BalanceFingerprint(decimal Available,
		decimal Reserved, decimal Total, decimal Borrowed);
	private readonly record struct OrderFingerprint(VALROrderStatuses Status,
		decimal Remaining, decimal Executed, string UpdatedAt);
	private readonly record struct PositionFingerprint(VALRSides Side,
		decimal Quantity, decimal AveragePrice, decimal RealizedPnL,
		decimal UnrealizedPnL, string UpdatedAt);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, MarketDefinition> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPublicTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<string, BalanceFingerprint> _balanceFingerprints =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PositionFingerprint> _positionFingerprints =
		new(StringComparer.OrdinalIgnoreCase);
	private VALRRestClient _restClient;
	private VALRSocketClient _tradeSocketClient;
	private VALRSocketClient _accountSocketClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="VALRMessageAdapter"/> class.
	/// </summary>
	public VALRMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.Valr];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Valr) ||
			securityId.IsAssociated(BoardCodes.Valr);

	private VALRRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private VALRSocketClient TradeSocketClient
		=> _tradeSocketClient ?? throw new InvalidOperationException(
			"VALR realtime streams require an API key and secret.");

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
				"VALR API key and secret are required for private operations.");
	}

	private void EnsureStreamingReady()
	{
		EnsureConnected();
		if (_tradeSocketClient is null)
			throw new InvalidOperationException(
				"VALR requires API credentials for WebSocket market streams. Use a history-only subscription without credentials.");
	}

	private string GetPortfolioName()
		=> SubAccountId.IsEmpty() ? "VALR" : $"VALR_{SubAccountId}";

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()) &&
			!portfolioName.EqualsIgnoreCase(SubAccountId))
			throw new InvalidOperationException(
				$"Unknown VALR portfolio '{portfolioName}'.");
	}

	private void RegisterMarkets(IEnumerable<VALRPair> pairs)
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var pair in pairs ?? [])
			{
				if (pair?.Symbol.IsEmpty() != false || !pair.IsActive ||
					pair.BaseCurrency.IsEmpty() || pair.QuoteCurrency.IsEmpty())
					continue;
				var volumeStep = pair.BaseDecimalPlaces is >= 0 and <= 28
					? 1m / Pow10(pair.BaseDecimalPlaces)
					: 0m;
				var market = new MarketDefinition
				{
					Symbol = pair.Symbol.NormalizeSymbol(),
					Name = pair.ShortName,
					BaseAsset = pair.BaseCurrency.ToUpperInvariant(),
					QuoteAsset = pair.QuoteCurrency.ToUpperInvariant(),
					PriceStep = pair.TickSize,
					VolumeStep = volumeStep,
					MinimumVolume = pair.MinimumBaseAmount,
					MaximumVolume = pair.MaximumBaseAmount,
					MinimumCost = pair.MinimumQuoteAmount,
					MaximumCost = pair.MaximumQuoteAmount,
					IsMarginAllowed = pair.IsMarginTradingAllowed,
					IsFuture = pair.PairType == VALRPairTypes.Future,
				};
				_markets[market.Symbol] = market;
			}
		}
	}

	private static decimal Pow10(int power)
	{
		var value = 1m;
		for (var i = 0; i < power; i++)
			value *= 10m;
		return value;
	}

	private MarketDefinition GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Valr) &&
			!securityId.IsAssociated(BoardCodes.Valr))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not VALR.");
		return GetMarket(securityId.SecurityCode);
	}

	private MarketDefinition GetMarket(string symbol)
	{
		symbol = symbol.NormalizeSymbol();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown VALR market '{symbol}'.");
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
		{
			foreach (var identifier in identifiers.Where(static value =>
				!value.IsEmpty()))
				_trackedOrders[identifier] = order;
			if (!order.ExchangeOrderId.IsEmpty())
				_trackedOrders[order.ExchangeOrderId] = order;
			if (!order.CustomerOrderId.IsEmpty())
				_trackedOrders[order.CustomerOrderId] = order;
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

	private bool AddPublicTrade(string identity, long transactionId)
	{
		if (identity.IsEmpty())
			return true;
		using (_sync.EnterScope())
		{
			if (_seenPublicTrades.Count > 100000)
				_seenPublicTrades.Clear();
			return _seenPublicTrades.Add($"{transactionId}:{identity}");
		}
	}

	private bool AddAccountTrade(string identity, long transactionId)
	{
		if (identity.IsEmpty())
			return true;
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
			_trackedOrders.Clear();
			_seenPublicTrades.Clear();
			_seenAccountTrades.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_balanceFingerprints.Clear();
			_orderFingerprints.Clear();
			_positionFingerprints.Clear();
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
			$"VALR {operation} requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
