namespace StockSharp.ProBit;

public partial class ProBitMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, StreamSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<(string Filter, string Symbol), int> _streamReferences = [];
	private readonly Dictionary<string, BookState> _books = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TickSubscription> _streamTradeCursors = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly HashSet<string> _accountTradeIds = new(StringComparer.OrdinalIgnoreCase);
	private ProBitRestClient _restClient;
	private ProBitWsClient _webSocketClient;
	private string _portfolioName;

	private class StreamSubscription
	{
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription : StreamSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class TickSubscription : StreamSubscription
	{
		public string LastTradeId { get; set; }
		public DateTime LastTime { get; set; }
	}

	private sealed class CandleSubscription : StreamSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public DateTime OpenTime { get; set; }
		public decimal OpenPrice { get; set; }
		public decimal HighPrice { get; set; }
		public decimal LowPrice { get; set; }
		public decimal ClosePrice { get; set; }
		public decimal TotalVolume { get; set; }
		public bool IsInitialized { get; set; }
	}

	private sealed class OrderSubscription
	{
		public string Symbol { get; init; }
		public string OrderIdentifier { get; init; }
		public string ClientOrderId { get; init; }
		public Sides? Side { get; init; }
	}

	private sealed class BookState
	{
		public SortedDictionary<decimal, decimal> Bids { get; } = new(
			Comparer<decimal>.Create(static (left, right) => right.CompareTo(left)));
		public SortedDictionary<decimal, decimal> Asks { get; } = [];
	}

	private readonly record struct CandleEmission(long TransactionId, string Symbol,
		TimeSpan TimeFrame, DateTime OpenTime, decimal OpenPrice, decimal HighPrice,
		decimal LowPrice, decimal ClosePrice, decimal TotalVolume, CandleStates State);

	/// <summary>
	/// Initializes a new instance of the <see cref="ProBitMessageAdapter"/>.
	/// </summary>
	public ProBitMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.ProBit];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.ProBit)
			|| securityId.IsAssociated(BoardCodes.ProBit);

	private ProBitRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private ProBitWsClient WebSocketClient
		=> _webSocketClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _webSocketClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"ProBit client ID and client secret are required for private operations.");
	}

	private static string GetPortfolioName(SecureString key)
		=> $"ProBit_{(key.IsEmpty() ? "Public" : key.ToId())}";

	private static string GetSymbol(SecurityId securityId)
		=> securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode)).ToUpperInvariant();

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(_portfolioName))
			throw new InvalidOperationException($"Unknown ProBit portfolio '{portfolioName}'.");
	}

	private static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && userOrderId.Length <= 64)
			return userOrderId;
		return $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
	}

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
				? id
				: 0;

	private static int NormalizeDepth(int? depth) => (depth ?? 100).Min(500).Max(1);

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
