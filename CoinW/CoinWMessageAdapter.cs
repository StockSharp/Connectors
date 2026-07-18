namespace StockSharp.CoinW;

public partial class CoinWMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, StreamSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<(CoinWSections Section, string Symbol), int> _tickerReferences = [];
	private readonly Dictionary<(CoinWSections Section, string Symbol), int> _depthReferences = [];
	private readonly Dictionary<(CoinWSections Section, string Symbol), int> _tradeReferences = [];
	private readonly Dictionary<(CoinWSections Section, string Symbol, TimeSpan TimeFrame), int> _candleReferences = [];
	private readonly Dictionary<string, Sides> _orderSides = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _spotPairIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _spotSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _futuresNativeSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _futuresSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, decimal> _futuresContractSizes = new(StringComparer.OrdinalIgnoreCase);
	private CoinWRestClient _restClient;
	private CoinWWsClient _spotMarketClient;
	private CoinWWsClient _futuresMarketClient;
	private CoinWWsClient _spotUserClient;
	private CoinWWsClient _futuresUserClient;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private class StreamSubscription
	{
		public string Symbol { get; init; }
		public CoinWSections Section { get; init; }
	}

	private sealed class DepthSubscription : StreamSubscription
	{
		public int Depth { get; init; }
		public long LastSequence { get; set; }
	}

	private sealed class TickSubscription : StreamSubscription
	{
		public string LastTradeId { get; set; }
		public DateTime LastTime { get; set; }
	}

	private sealed class CandleSubscription : StreamSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="CoinWMessageAdapter"/>.
	/// </summary>
	public CoinWMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.CoinW, BoardCodes.CoinWFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinW)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinWFutures)
			|| securityId.IsAssociated(BoardCodes.CoinW)
			|| securityId.IsAssociated(BoardCodes.CoinWFutures);

	private CoinWRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null ||
			(IsSectionEnabled(CoinWSections.Spot) && _spotMarketClient is null) ||
			(IsSectionEnabled(CoinWSections.Futures) && _futuresMarketClient is null))
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady(CoinWSections? section = null)
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException("CoinW API key and secret are required for private operations.");
		if (section is CoinWSections.Spot && _spotUserClient is null ||
			section is CoinWSections.Futures && _futuresUserClient is null)
			throw new InvalidOperationException("The requested CoinW market section is not enabled.");
	}

	private bool IsSectionEnabled(CoinWSections section) => Sections.Contains(section);

	private CoinWWsClient GetMarketClient(CoinWSections section)
		=> section == CoinWSections.Spot
			? _spotMarketClient ?? throw new InvalidOperationException("CoinW spot market section is not connected.")
			: _futuresMarketClient ?? throw new InvalidOperationException("CoinW futures market section is not connected.");

	private CoinWSections ResolveSection(SecurityId securityId)
	{
		var section = securityId.BoardCode.ToSection();
		if (!IsSectionEnabled(section))
			throw new InvalidOperationException($"CoinW {section} market section is not enabled.");
		return section;
	}

	private static string GetPortfolioName(SecureString key)
		=> $"CoinW_{(key.IsEmpty() ? "Public" : key.ToId())}";

	private static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && userOrderId.Length <= 50)
			return userOrderId;
		return $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
	}

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true
			&& long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var id)
				? id
				: 0;

	private static int NormalizeDepth(int? depth) => (depth ?? 20).Min(100).Max(1);

	private string GetSpotPairId(string symbol)
	{
		using (_sync.EnterScope())
			return _spotPairIds.TryGetValue(symbol, out var pairId)
				? pairId
				: throw new InvalidOperationException($"CoinW did not publish a pair ID for '{symbol}'. Refresh the security lookup.");
	}

	private string GetFuturesNativeSymbol(string symbol)
	{
		using (_sync.EnterScope())
			return _futuresNativeSymbols.TryGetValue(symbol, out var native)
				? native
				: symbol.ToCoinWNativeFuturesSymbol();
	}

	private string GetStreamSymbol(CoinWSections section, string symbol)
		=> section == CoinWSections.Spot ? GetSpotPairId(symbol) : GetFuturesNativeSymbol(symbol);

	private string ResolveStreamSymbol(CoinWSections section, string pairCode)
	{
		if (pairCode.IsEmpty())
			return null;
		using (_sync.EnterScope())
		{
			var map = section == CoinWSections.Spot ? _spotSymbols : _futuresSymbols;
			return map.TryGetValue(pairCode, out var symbol) ? symbol :
				section == CoinWSections.Futures ? pairCode.ToCoinWFuturesSecurityCode() : null;
		}
	}

	private decimal GetContractSize(string symbol)
	{
		using (_sync.EnterScope())
			return _futuresContractSizes.TryGetValue(symbol, out var size) && size > 0 ? size : 1m;
	}

	private static bool AddReference<TKey>(IDictionary<TKey, int> references, TKey key)
	{
		if (references.TryGetValue(key, out var count))
		{
			references[key] = count + 1;
			return false;
		}
		references.Add(key, 1);
		return true;
	}

	private static bool ReleaseReference<TKey>(IDictionary<TKey, int> references, TKey key)
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
}
