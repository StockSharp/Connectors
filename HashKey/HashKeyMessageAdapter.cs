namespace StockSharp.HashKey;

public partial class HashKeyMessageAdapter
{
	private sealed class MarketDefinition
	{
		public HashKeySections Section { get; init; }
		public string Symbol { get; init; }
		public string Name { get; init; }
		public string BaseAsset { get; init; }
		public string QuoteAsset { get; init; }
		public HashKeyTradingStatuses Status { get; init; }
		public decimal? PriceStep { get; init; }
		public decimal? VolumeStep { get; init; }
		public decimal? MinimumVolume { get; init; }
		public decimal? MaximumVolume { get; init; }
		public decimal? Multiplier { get; init; }
	}

	private class MarketSubscription
	{
		public string Symbol { get; init; }
		public HashKeySections Section { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public string Interval { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public HashKeySections Section { get; init; }
		public string Symbol { get; init; }
		public string ClientOrderId { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public decimal? StopPrice { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public OrderPositionEffects? PositionEffect { get; init; }
		public bool IsPostOnly { get; init; }
	}

	private readonly record struct StreamKey(HashKeyWsTopics Topic, string Symbol,
		string Interval);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, MarketDefinition> _spotMarkets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, MarketDefinition> _futuresMarkets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenTradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<HashKeyWsClient> _failedWsClients = [];
	private HashKeyRestClient _restClient;
	private HashKeyWsClient _publicWsClient;
	private HashKeyWsClient _privateWsClient;
	private string _listenKey;
	private DateTime _lastListenKeyRenewal;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	/// <summary>
	/// Initializes a new instance of the <see cref="HashKeyMessageAdapter"/> class.
	/// </summary>
	public HashKeyMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override bool IsSupportExecutionsPnL => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.HashKey, BoardCodes.HashKeyFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.HashKey) ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.HashKeyFutures) ||
			securityId.IsAssociated(BoardCodes.HashKey) ||
			securityId.IsAssociated(BoardCodes.HashKeyFutures);

	private HashKeyRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private HashKeyWsClient PublicWsClient
		=> _publicWsClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private bool IsSectionEnabled(HashKeySections section) => Sections.Contains(section);

	private void EnsureConnected()
	{
		if (_restClient is null || _publicWsClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"HashKey API key and secret are required for private operations.");
	}

	private HashKeySections ResolveSection(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty())
		{
			var section = securityId.BoardCode.ToSection();
			if (!IsSectionEnabled(section))
				throw new InvalidOperationException($"HashKey {section} section is not enabled.");
			return section;
		}

		var symbol = NormalizeSymbol(securityId.SecurityCode);
		using (_sync.EnterScope())
		{
			var isSpot = _spotMarkets.ContainsKey(symbol);
			var isFutures = _futuresMarkets.ContainsKey(symbol);
			if (isSpot != isFutures)
				return isSpot ? HashKeySections.Spot : HashKeySections.Futures;
		}
		throw new InvalidOperationException(
			"SecurityId.BoardCode must identify the HashKey spot or futures section.");
	}

	private string GetSymbol(SecurityId securityId, HashKeySections section)
	{
		var symbol = NormalizeSymbol(securityId.SecurityCode);
		using (_sync.EnterScope())
		{
			var markets = section == HashKeySections.Spot ? _spotMarkets : _futuresMarkets;
			if (markets.ContainsKey(symbol))
				return symbol;
		}
		throw new InvalidOperationException($"Unknown HashKey {section} market '{symbol}'.");
	}

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).Trim().Replace("/", string.Empty,
			StringComparison.Ordinal).Replace("_", string.Empty,
			StringComparison.Ordinal).ToUpperInvariant();

	private static string GetPortfolioName(HashKeySections section, SecureString key)
		=> $"HashKey_{section}_{key.ToId()}";

	private string GetPortfolioName(HashKeySections section)
		=> GetPortfolioName(section, Key);

	private static bool AddReference(IDictionary<StreamKey, int> references, StreamKey key)
	{
		if (references.TryGetValue(key, out var count))
		{
			references[key] = count + 1;
			return false;
		}
		references.Add(key, 1);
		return true;
	}

	private static bool ReleaseReference(IDictionary<StreamKey, int> references, StreamKey key)
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

	private void RegisterMarkets(HashKeyExchangeInfo info)
	{
		using (_sync.EnterScope())
		{
			_spotMarkets.Clear();
			if (IsSectionEnabled(HashKeySections.Spot))
				foreach (var symbol in info?.Symbols ?? [])
				{
					if (symbol?.Symbol.IsEmpty() != false)
						continue;
					var price = symbol.Filters?.FirstOrDefault(static filter =>
						filter.Type == HashKeyFilterTypes.Price);
					var lot = symbol.Filters?.FirstOrDefault(static filter =>
						filter.Type == HashKeyFilterTypes.LotSize);
					_spotMarkets[NormalizeSymbol(symbol.Symbol)] = new()
					{
						Section = HashKeySections.Spot,
						Symbol = NormalizeSymbol(symbol.Symbol),
						Name = symbol.Name,
						BaseAsset = symbol.BaseAsset,
						QuoteAsset = symbol.QuoteAsset,
						Status = symbol.Status,
						PriceStep = price?.TickSize.ToNullableDecimal(),
						VolumeStep = lot?.StepSize.ToNullableDecimal(),
						MinimumVolume = lot?.MinimumQuantity.ToNullableDecimal(),
						MaximumVolume = lot?.MaximumQuantity.ToNullableDecimal(),
					};
				}

			_futuresMarkets.Clear();
			if (IsSectionEnabled(HashKeySections.Futures))
				foreach (var contract in info?.Contracts ?? [])
				{
					if (contract?.Symbol.IsEmpty() != false)
						continue;
					var price = contract.Filters?.FirstOrDefault(static filter =>
						filter.Type == HashKeyFilterTypes.Price);
					var lot = contract.Filters?.FirstOrDefault(static filter =>
						filter.Type == HashKeyFilterTypes.LotSize);
					_futuresMarkets[NormalizeSymbol(contract.Symbol)] = new()
					{
						Section = HashKeySections.Futures,
						Symbol = NormalizeSymbol(contract.Symbol),
						Name = contract.Name,
						BaseAsset = contract.Underlying.IsEmpty(contract.BaseAsset),
						QuoteAsset = contract.QuoteAsset,
						Status = contract.Status,
						PriceStep = price?.TickSize.ToNullableDecimal(),
						VolumeStep = lot?.StepSize.ToNullableDecimal(),
						MinimumVolume = lot?.MinimumQuantity.ToNullableDecimal(),
						MaximumVolume = lot?.MaximumQuantity.ToNullableDecimal(),
						Multiplier = contract.ContractMultiplier.ToNullableDecimal(),
					};
				}
		}
	}

	private MarketDefinition GetMarket(HashKeySections section, string symbol)
	{
		using (_sync.EnterScope())
		{
			var markets = section == HashKeySections.Spot ? _spotMarkets : _futuresMarkets;
			return markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown HashKey {section} market '{symbol}'.");
		}
	}

	private void TrackOrder(string orderId, TrackedOrder tracked)
	{
		if (orderId.IsEmpty() || tracked is null)
			return;
		using (_sync.EnterScope())
			_trackedOrders[orderId] = tracked;
	}

	private TrackedOrder GetTrackedOrder(string orderId)
	{
		if (orderId.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(orderId, out var tracked) ? tracked : null;
	}

	private bool AddTradeId(string tradeId)
	{
		if (tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
			return _seenTradeIds.Add(tradeId);
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_spotMarkets.Clear();
			_futuresMarkets.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_trackedOrders.Clear();
			_seenTradeIds.Clear();
			_failedWsClients.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
	}

	private static string ResolveOrderId(long? numericId, string stringId)
	{
		if (!stringId.IsEmpty())
			return stringId;
		if (numericId is > 0)
			return numericId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException("HashKey order identifier is required.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
