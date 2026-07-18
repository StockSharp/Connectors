namespace StockSharp.Bullish;

public partial class BullishMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, Level1Subscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<(BullishWsKinds Kind, string Topic, string Symbol), int> _streamReferences = [];
	private readonly Dictionary<string, BullishMarket> _markets = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BookState> _books = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BullishTradingAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
	private BullishRestClient _restClient;
	private BullishWsClient _bookClient;
	private BullishWsClient _tradeClient;
	private BullishWsClient _tickClient;
	private BullishWsClient _privateClient;
	private string _defaultTradingAccountId;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private class StreamSubscription
	{
		public string Symbol { get; init; }
		public BullishSections Section { get; init; }
	}

	private sealed class Level1Subscription : StreamSubscription
	{
		public bool IsTickStream { get; init; }
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

	private sealed class BookState
	{
		public SortedDictionary<decimal, decimal> Bids { get; } =
			new(Comparer<decimal>.Create(static (left, right) => right.CompareTo(left)));
		public SortedDictionary<decimal, decimal> Asks { get; } = [];
		public long Sequence { get; set; }
		public DateTime ServerTime { get; set; }
	}

	private readonly record struct CandleEmission(long TransactionId, string Symbol,
		BullishSections Section, TimeSpan TimeFrame, DateTime OpenTime, decimal OpenPrice,
		decimal HighPrice, decimal LowPrice, decimal ClosePrice, decimal TotalVolume,
		CandleStates State);

	/// <summary>
	/// Initializes a new instance of the <see cref="BullishMessageAdapter"/>.
	/// </summary>
	public BullishMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromMinutes(5);
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
	public override string[] AssociatedBoards => [BoardCodes.Bullish, BoardCodes.BullishDerivatives];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bullish)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BullishDerivatives)
			|| securityId.IsAssociated(BoardCodes.Bullish)
			|| securityId.IsAssociated(BoardCodes.BullishDerivatives);

	private BullishRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _bookClient is null || _tradeClient is null || _tickClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable || _privateClient is null)
			throw new InvalidOperationException(
				"Bullish HMAC public key and secret are required for private operations.");
	}

	private bool IsSectionEnabled(BullishSections section) => Sections.Contains(section);

	private static string GetSymbol(SecurityId securityId)
		=> securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode)).ToUpperInvariant();

	private BullishSections ResolveSection(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty())
		{
			var section = securityId.BoardCode.ToSection();
			if (!IsSectionEnabled(section))
				throw new InvalidOperationException($"Bullish {section} market section is not enabled.");
			return section;
		}
		return ResolveSection(securityId.SecurityCode);
	}

	private BullishSections ResolveSection(string symbol)
	{
		using (_sync.EnterScope())
		{
			if (_markets.TryGetValue(symbol ?? string.Empty, out var market))
				return market.MarketType.ToSectionByMarketType();
		}
		throw new InvalidOperationException($"Unknown Bullish market symbol '{symbol}'.");
	}

	private string GetMarketType(string symbol)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol ?? string.Empty, out var market)
				? market.MarketType
				: null;
	}

	private string ResolveTradingAccount(string portfolioName = null)
	{
		if (!portfolioName.IsEmpty())
		{
			using (_sync.EnterScope())
			{
				var account = _accounts.Keys.FirstOrDefault(id =>
					portfolioName.EqualsIgnoreCase(GetPortfolioName(id)) || portfolioName.EqualsIgnoreCase(id));
				if (!account.IsEmpty())
					return account;
			}
		}
		return _defaultTradingAccountId.ThrowIfEmpty("Bullish trading account");
	}

	private static string GetPortfolioName(string tradingAccountId)
		=> $"Bullish_{tradingAccountId}";

	private static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && long.TryParse(userOrderId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var userId) && userId > 0)
			return userId.ToString(CultureInfo.InvariantCulture);
		if (transactionId <= 0)
			throw new InvalidOperationException("Bullish requires a positive numeric client order ID.");
		return transactionId.ToString(CultureInfo.InvariantCulture);
	}

	private static long ParseTransactionId(string clientOrderId)
		=> long.TryParse(clientOrderId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
			? id
			: 0;

	private static int NormalizeDepth(int? depth) => (depth ?? 20).Min(1000).Max(1);

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
