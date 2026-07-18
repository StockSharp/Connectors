namespace StockSharp.CryptoCom;

public partial class CryptoComMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, string> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<string, int> _channelReferences = new(StringComparer.OrdinalIgnoreCase);
	private CryptoComRestClient _restClient;
	private CryptoComWsClient _marketWsClient;
	private CryptoComWsClient _userWsClient;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private sealed class DepthSubscription
	{
		public string Symbol { get; init; }
		public string Channel { get; init; }
		public int Depth { get; init; }
		public long LastSequence { get; set; }
	}

	private sealed class TickSubscription
	{
		public string Symbol { get; init; }
		public string LastTradeId { get; set; }
		public DateTime LastTime { get; set; }
	}

	private sealed class CandleSubscription
	{
		public string Symbol { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="CryptoComMessageAdapter"/>.
	/// </summary>
	public CryptoComMessageAdapter(IdGenerator transactionIdGenerator)
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
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.CryptoCom, BoardCodes.CryptoComDerivatives];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CryptoCom)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CryptoComDerivatives)
			|| securityId.IsAssociated(BoardCodes.CryptoCom)
			|| securityId.IsAssociated(BoardCodes.CryptoComDerivatives);

	private CryptoComRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private CryptoComWsClient MarketWsClient
		=> _marketWsClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _marketWsClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (Key.IsEmpty() || Secret.IsEmpty() || _userWsClient is null)
			throw new InvalidOperationException("Crypto.com Exchange API key and secret are required for private operations.");
	}

	private bool IsSectionEnabled(CryptoComSections section) => Sections.Contains(section);

	private static SecurityId ToSecurityId(string symbol)
		=> symbol.ToStockSharp(IsSpotSymbol(symbol) ? BoardCodes.CryptoCom : BoardCodes.CryptoComDerivatives);

	private static bool IsSpotSymbol(string symbol)
		=> symbol?.Contains('_') == true;

	private static int NormalizeDepth(int? depth)
		=> depth is > 10 ? 50 : 10;

	private static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && userOrderId.Length <= 36)
			return userOrderId;
		return $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
	}

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true
			&& long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
				? id
				: 0;

	private static long? ParseLong(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
			? number
			: null;

	private static bool AddReference(IDictionary<string, int> references, string key)
	{
		if (references.TryGetValue(key, out var count))
		{
			references[key] = count + 1;
			return false;
		}

		references.Add(key, 1);
		return true;
	}

	private static bool ReleaseReference(IDictionary<string, int> references, string key)
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
