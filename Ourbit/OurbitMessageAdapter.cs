namespace StockSharp.Ourbit;

public partial class OurbitMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, OurbitSpotSymbol> _spotSymbols =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OurbitFuturesProduct> _futuresProducts =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, FuturesBookState> _futuresBooks =
		new(StringComparer.OrdinalIgnoreCase);
	private OurbitSpotRestClient _spotRestClient;
	private OurbitFuturesRestClient _futuresRestClient;
	private OurbitSpotWsClient _spotWsClient;
	private OurbitFuturesWsClient _futuresWsClient;
	private string _spotListenKey;
	private DateTime _spotListenKeyTime;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private long _tradeIdSeed;

	private class MarketSubscription
	{
		public string Symbol { get; init; }
		public OurbitSections Section { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
	}

	private readonly record struct StreamKey(OurbitSections Section, string Topic,
		string Symbol, string Argument = null);

	private sealed class FuturesBookState
	{
		public long Version { get; set; }
		public bool IsRestoring { get; set; }
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="OurbitMessageAdapter"/>.
	/// </summary>
	public OurbitMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
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
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Ourbit, BoardCodes.OurbitFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Ourbit)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.OurbitFutures)
			|| securityId.IsAssociated(BoardCodes.Ourbit)
			|| securityId.IsAssociated(BoardCodes.OurbitFutures);

	private bool IsSectionEnabled(OurbitSections section) => Sections.Contains(section);

	private OurbitSpotRestClient SpotRestClient
		=> _spotRestClient ?? throw new InvalidOperationException("The Ourbit spot section is not connected.");

	private OurbitFuturesRestClient FuturesRestClient
		=> _futuresRestClient ?? throw new InvalidOperationException("The Ourbit futures section is not connected.");

	private void EnsureConnected()
	{
		if (_spotRestClient is null && _futuresRestClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady(OurbitSections section)
	{
		EnsureConnected();
		var ready = section == OurbitSections.Spot
			? _spotRestClient?.IsCredentialsAvailable == true
			: _futuresRestClient?.IsCredentialsAvailable == true;
		if (!ready)
			throw new InvalidOperationException(
				$"Ourbit API key and secret are required for private {section} operations.");
	}

	private OurbitSections ResolveSection(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty())
		{
			var section = securityId.BoardCode.ToSection();
			if (!IsSectionEnabled(section))
				throw new InvalidOperationException($"Ourbit {section} section is not enabled.");
			return section;
		}
		var enabled = Sections.Distinct().ToArray();
		if (enabled.Length == 1)
			return enabled[0];
		var symbol = securityId.SecurityCode;
		using (_sync.EnterScope())
		{
			if (_futuresProducts.ContainsKey(symbol ?? string.Empty) &&
				!_spotSymbols.ContainsKey(symbol ?? string.Empty))
				return OurbitSections.Futures;
			if (_spotSymbols.ContainsKey(symbol ?? string.Empty) &&
				!_futuresProducts.ContainsKey(symbol ?? string.Empty))
				return OurbitSections.Spot;
		}
		throw new InvalidOperationException(
			"SecurityId.BoardCode must identify the Ourbit spot or futures section.");
	}

	private static string GetSymbol(SecurityId securityId, OurbitSections section)
	{
		var symbol = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		return section == OurbitSections.Futures
			? symbol.ToFuturesSymbol()
			: symbol.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
	}

	private static string GetPortfolioName(OurbitSections section)
		=> section == OurbitSections.Spot ? "Ourbit_Spot" : "Ourbit_Futures";

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

	private static int NormalizeSpotDepth(int? depth)
	{
		var value = depth ?? 20;
		return value <= 5 ? 5 : value <= 10 ? 10 : 20;
	}

	private static int NormalizeFuturesDepth(int? depth) => (depth ?? 20).Min(100).Max(1);

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
