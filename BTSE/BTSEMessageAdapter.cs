namespace StockSharp.BTSE;

public partial class BTSEMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _depthProcessing = new(1, 1);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, BTSEMarketSummary> _spotMarkets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BTSEMarketSummary> _futuresMarkets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenFillIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<BTSEWsClient> _failedWsClients = [];
	private BTSERestClient _spotRestClient;
	private BTSERestClient _futuresRestClient;
	private BTSEWsClient _spotWsClient;
	private BTSEWsClient _spotBookWsClient;
	private BTSEWsClient _futuresWsClient;
	private BTSEWsClient _futuresBookWsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private class MarketSubscription
	{
		public string Symbol { get; init; }
		public BTSESections Section { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
		public long LastSequence { get; set; }
	}

	private readonly record struct StreamKey(BTSESections Section, string Topic);

	/// <summary>
	/// Initializes a new instance of the <see cref="BTSEMessageAdapter"/>.
	/// </summary>
	public BTSEMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => false;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Btse, BoardCodes.BtseFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Btse)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BtseFutures)
			|| securityId.IsAssociated(BoardCodes.Btse)
			|| securityId.IsAssociated(BoardCodes.BtseFutures);

	private bool IsSectionEnabled(BTSESections section) => Sections.Contains(section);

	private BTSERestClient GetRestClient(BTSESections section)
		=> section == BTSESections.Spot
			? _spotRestClient ?? throw new InvalidOperationException(
				"The BTSE spot section is not connected.")
			: _futuresRestClient ?? throw new InvalidOperationException(
				"The BTSE futures section is not connected.");

	private BTSEWsClient GetGeneralWsClient(BTSESections section)
		=> section == BTSESections.Spot
			? _spotWsClient ?? throw new InvalidOperationException(
				"The BTSE spot WebSocket is not connected.")
			: _futuresWsClient ?? throw new InvalidOperationException(
				"The BTSE futures WebSocket is not connected.");

	private BTSEWsClient GetBookWsClient(BTSESections section)
		=> section == BTSESections.Spot
			? _spotBookWsClient ?? throw new InvalidOperationException(
				"The BTSE spot order-book WebSocket is not connected.")
			: _futuresBookWsClient ?? throw new InvalidOperationException(
				"The BTSE futures order-book WebSocket is not connected.");

	private void EnsureConnected()
	{
		if (_spotRestClient is null && _futuresRestClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady(BTSESections section)
	{
		EnsureConnected();
		if (!GetRestClient(section).IsCredentialsAvailable)
			throw new InvalidOperationException(
				$"BTSE API key and secret are required for private {section} operations.");
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		foreach (var section in Sections)
			EnsurePrivateReady(section);
	}

	private BTSESections ResolveSection(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty())
		{
			var section = securityId.BoardCode.ToSection();
			if (!IsSectionEnabled(section))
				throw new InvalidOperationException($"BTSE {section} section is not enabled.");
			return section;
		}

		var enabled = Sections.Distinct().ToArray();
		if (enabled.Length == 1)
			return enabled[0];

		var symbol = NormalizeSymbol(securityId.SecurityCode);
		using (_sync.EnterScope())
		{
			var isSpot = _spotMarkets.ContainsKey(symbol);
			var isFutures = _futuresMarkets.ContainsKey(symbol);
			if (isSpot != isFutures)
				return isSpot ? BTSESections.Spot : BTSESections.Futures;
		}

		throw new InvalidOperationException(
			"SecurityId.BoardCode must identify the BTSE spot or futures section.");
	}

	private string GetSymbol(SecurityId securityId, BTSESections section)
	{
		var symbol = NormalizeSymbol(
			securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode)));
		using (_sync.EnterScope())
		{
			var markets = section == BTSESections.Spot ? _spotMarkets : _futuresMarkets;
			if (markets.ContainsKey(symbol))
				return symbol;

			var compact = CompactSymbol(symbol);
			var candidates = markets.Keys
				.Where(candidate => CompactSymbol(candidate).EqualsIgnoreCase(compact))
				.Take(2).ToArray();
			if (candidates.Length == 1)
				return candidates[0];
		}

		throw new InvalidOperationException($"Unknown BTSE {section} market '{symbol}'.");
	}

	private BTSEMarketSummary GetMarket(BTSESections section, string symbol)
	{
		using (_sync.EnterScope())
		{
			var markets = section == BTSESections.Spot ? _spotMarkets : _futuresMarkets;
			return markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown BTSE {section} market '{symbol}'.");
		}
	}

	private static string NormalizeSymbol(string symbol)
		=> symbol?.Trim().Replace('_', '-').Replace('/', '-').ToUpperInvariant();

	private static string CompactSymbol(string symbol)
		=> symbol?.Replace("-", string.Empty, StringComparison.Ordinal)
			.Replace("_", string.Empty, StringComparison.Ordinal);

	private static string GetPortfolioName(BTSESections section)
		=> section == BTSESections.Spot ? "BTSE_Spot" : "BTSE_Futures";

	private string ResolvePositionSymbol(string marketName)
	{
		marketName = NormalizeSymbol(marketName);
		using (_sync.EnterScope())
		{
			if (_futuresMarkets.ContainsKey(marketName))
				return marketName;
			return _futuresMarkets.Keys
				.OrderByDescending(static symbol => symbol.Length)
				.FirstOrDefault(symbol => marketName.StartsWith(symbol + "-",
					StringComparison.OrdinalIgnoreCase));
		}
	}

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

	private void RegisterMarkets(BTSESections section,
		IEnumerable<BTSEMarketSummary> markets)
	{
		using (_sync.EnterScope())
		{
			var registry = section == BTSESections.Spot ? _spotMarkets : _futuresMarkets;
			foreach (var market in markets ?? [])
				if (market?.Symbol.IsEmpty() == false)
					registry[NormalizeSymbol(market.Symbol)] = market;
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_streamReferences.Clear();
			_spotMarkets.Clear();
			_futuresMarkets.Clear();
			_seenFillIds.Clear();
			_failedWsClients.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_depthProcessing.Dispose();
		base.DisposeManaged();
	}
}
