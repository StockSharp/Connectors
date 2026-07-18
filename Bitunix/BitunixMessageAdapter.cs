namespace StockSharp.Bitunix;

public partial class BitunixMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, BitunixSpotPair> _spotPairs =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BitunixFuturesProduct> _futuresProducts =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _spotOrderSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenSpotFillIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenFuturesFillIds = new(StringComparer.OrdinalIgnoreCase);
	private BitunixRestClient _spotRestClient;
	private BitunixRestClient _futuresRestClient;
	private BitunixFuturesWsClient _futuresWsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private long _tradeIdSeed;
	private DateTime _lastPollingTime;
	private DateTime _lastPingTime;

	private class MarketSubscription
	{
		public string Symbol { get; init; }
		public BitunixSections Section { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
	}

	private readonly record struct StreamKey(string Topic, string Symbol, string Argument = null);

	/// <summary>
	/// Initializes a new instance of the <see cref="BitunixMessageAdapter"/>.
	/// </summary>
	public BitunixMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
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
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Bitunix, BoardCodes.BitunixFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bitunix)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BitunixFutures)
			|| securityId.IsAssociated(BoardCodes.Bitunix)
			|| securityId.IsAssociated(BoardCodes.BitunixFutures);

	private bool IsSectionEnabled(BitunixSections section) => Sections.Contains(section);

	private BitunixRestClient SpotRestClient
		=> _spotRestClient ?? throw new InvalidOperationException("The Bitunix spot section is not connected.");

	private BitunixRestClient FuturesRestClient
		=> _futuresRestClient ?? throw new InvalidOperationException("The Bitunix futures section is not connected.");

	private void EnsureConnected()
	{
		if (_spotRestClient is null && _futuresRestClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady(BitunixSections section)
	{
		EnsureConnected();
		var ready = section == BitunixSections.Spot
			? _spotRestClient?.IsCredentialsAvailable == true
			: _futuresRestClient?.IsCredentialsAvailable == true;
		if (!ready)
			throw new InvalidOperationException(
				$"Bitunix API key and secret are required for private {section} operations.");
	}

	private BitunixSections ResolveSection(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty())
		{
			var section = securityId.BoardCode.ToSection();
			if (!IsSectionEnabled(section))
				throw new InvalidOperationException($"Bitunix {section} section is not enabled.");
			return section;
		}

		var enabled = Sections.Distinct().ToArray();
		if (enabled.Length == 1)
			return enabled[0];

		var symbol = securityId.SecurityCode;
		using (_sync.EnterScope())
		{
			if (_futuresProducts.ContainsKey(symbol ?? string.Empty) &&
				!_spotPairs.ContainsKey(symbol ?? string.Empty))
				return BitunixSections.Futures;
			if (_spotPairs.ContainsKey(symbol ?? string.Empty) &&
				!_futuresProducts.ContainsKey(symbol ?? string.Empty))
				return BitunixSections.Spot;
		}

		throw new InvalidOperationException(
			"SecurityId.BoardCode must identify the Bitunix spot or futures section.");
	}

	private static string GetSymbol(SecurityId securityId)
		=> securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode))
			.Replace("_", string.Empty, StringComparison.Ordinal)
			.ToUpperInvariant();

	private static string GetPortfolioName(BitunixSections section)
		=> section == BitunixSections.Spot ? "Bitunix_Spot" : "Bitunix_Futures";

	private static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && userOrderId.Length <= 32)
			return userOrderId;
		return $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
	}

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture,
				out var transactionId)
				? transactionId
				: 0;

	private static int NormalizeDepth(int? depth)
	{
		var value = depth ?? 15;
		return value <= 1 ? 1 : value <= 5 ? 5 : 15;
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
