namespace StockSharp.Deepcoin;

public partial class DeepcoinMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, string> _spotWireSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _swapWireSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _orderInstruments = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenFillIds = new(StringComparer.OrdinalIgnoreCase);
	private DeepcoinRestClient _restClient;
	private DeepcoinPublicWsClient _spotWsClient;
	private DeepcoinPublicWsClient _swapWsClient;
	private DeepcoinPrivateWsClient _privateWsClient;
	private string _listenKey;
	private DateTime _nextListenKeyExtension;
	private bool _instrumentMapsLoaded;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private class MarketSubscription
	{
		public string InstrumentId { get; init; }
		public DeepcoinProductTypes ProductType { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
	}

	private readonly record struct StreamKey(DeepcoinProductTypes ProductType,
		DeepcoinWsTopics Topic, string InstrumentId, TimeSpan TimeFrame);

	/// <summary>
	/// Initializes a new instance of the <see cref="DeepcoinMessageAdapter"/>.
	/// </summary>
	public DeepcoinMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(15);
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
	public override string[] AssociatedBoards => [BoardCodes.Deepcoin];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Deepcoin)
			|| securityId.IsAssociated(BoardCodes.Deepcoin);

	private DeepcoinRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DeepcoinPrivateWsClient PrivateWsClient
		=> _privateWsClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _spotWsClient is null || _swapWsClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable || _privateWsClient is null)
			throw new InvalidOperationException(
				"Deepcoin API key, secret, and passphrase are required for private operations.");
	}

	private DeepcoinPublicWsClient GetPublicClient(DeepcoinProductTypes productType)
		=> productType == DeepcoinProductTypes.Spot
			? _spotWsClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk)
			: _swapWsClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private static string GetSymbol(SecurityId securityId)
		=> securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode))
			.Replace('_', '-')
			.ToUpperInvariant();

	private static DeepcoinProductTypes ResolveProductType(string instrumentId)
		=> instrumentId.EndsWith("-SWAP", StringComparison.OrdinalIgnoreCase)
			? DeepcoinProductTypes.Swap
			: DeepcoinProductTypes.Spot;

	private static string GetPortfolioName(DeepcoinProductTypes productType, SecureString key)
		=> $"Deepcoin_{productType}_{(key.IsEmpty() ? "Public" : key.ToId())}";

	private string GetPortfolioName(DeepcoinProductTypes productType)
		=> GetPortfolioName(productType, Key);

	private static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && userOrderId.Length <= 20 &&
			userOrderId.All(static c => char.IsLetterOrDigit(c)))
			return userOrderId;
		return $"ss{transactionId.ToString(CultureInfo.InvariantCulture)}";
	}

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientOrderId.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture,
				out var transactionId)
				? transactionId
				: 0;

	private static int NormalizeDepth(int? requestedDepth)
		=> (requestedDepth ?? 60).Max(1).Min(60);

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

	private void RegisterInstrument(string instrumentId)
	{
		if (instrumentId.IsEmpty())
			return;
		var productType = ResolveProductType(instrumentId);
		var wireSymbol = instrumentId.ToDeepcoinWsSymbol(productType);
		using (_sync.EnterScope())
			(productType == DeepcoinProductTypes.Spot ? _spotWireSymbols : _swapWireSymbols)
				[wireSymbol] = instrumentId;
	}

	private string ResolvePrivateInstrument(string wireSymbol, bool isSwap)
	{
		if (wireSymbol.IsEmpty())
			return null;
		using (_sync.EnterScope())
		{
			var primary = isSwap ? _swapWireSymbols : _spotWireSymbols;
			var secondary = isSwap ? _spotWireSymbols : _swapWireSymbols;
			if (primary.TryGetValue(wireSymbol, out var instrumentId) ||
				secondary.TryGetValue(wireSymbol, out instrumentId))
				return instrumentId;
		}
		return null;
	}
}
