namespace StockSharp.Injective;

/// <summary>The message adapter for Injective spot and derivative markets.</summary>
[MediaIcon(Media.MediaNames.injective)]
[Doc("topics/api/connectors/crypto_exchanges/injective.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.InjectiveKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(InjectiveOrderCondition))]
public partial class InjectiveMessageAdapter : MessageAdapter
{
	private class MarketSubscription
	{
		public long TransactionId { get; init; }
		public InjectiveMarket Market { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
		public DateTime NextPollTime { get; set; }
	}

	private sealed class OrderSubscription
	{
		public InjectiveMarket Market { get; init; }
		public string OrderId { get; init; }
		public Sides? Side { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Limit { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _transactionSync = new(1, 1);
	private readonly Dictionary<string, InjectiveMarket> _marketsByCode =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, InjectiveMarket> _marketsById =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, InjectiveTokenMeta> _tokensByDenom =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, decimal> _lastPrices =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<string, int> _streamReferences =
		new(StringComparer.Ordinal);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<string, InjectiveOrder> _knownOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private InjectiveRestClient _restClient;
	private InjectiveGrpcClient _grpcClient;
	private InjectiveChainSocketClient _chainSocketClient;
	private InjectiveSigner _signer;
	private string _subaccountId;
	private string _portfolioName;
	private DateTime _serverTime;
	private DateTime _nextBlockRefresh;
	private long _currentHeight;
	private ulong? _accountNumber;
	private ulong? _nextSequence;

	/// <summary>Supported candle time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(6),
		TimeSpan.FromHours(8),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	/// <summary>Initializes a new instance.</summary>
	public InjectiveMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Injective];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Injective) ||
			securityId.IsAssociated(BoardCodes.Injective);

	private InjectiveRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private InjectiveGrpcClient GrpcClient => _grpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private InjectiveSigner Signer => _signer ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private long CurrentHeight
	{
		get
		{
			using (_sync.EnterScope())
				return _currentHeight;
		}
	}

	private string PortfolioName => _portfolioName ?? throw new
		InvalidOperationException(
			"An Injective wallet address is required for account data.");

	private void EnsureConnected()
	{
		if (_restClient is null || _grpcClient is null ||
			_chainSocketClient is null || _signer is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (!Signer.IsWalletAvailable || _subaccountId.IsEmpty())
			throw new InvalidOperationException(
				"An Injective wallet address is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (!Signer.IsSigningAvailable)
			throw new InvalidOperationException(
				"An Injective private key is required for transactions.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		EnsureAccountReady();
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException(
				$"Unknown Injective portfolio '{portfolioName}'.");
	}

	private InjectiveMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Injective.");
		using (_sync.EnterScope())
		{
			if (securityId.Native is string marketId &&
				_marketsById.TryGetValue(marketId, out var byId))
				return byId;
			var code = securityId.SecurityCode.ThrowIfEmpty(
				nameof(securityId.SecurityCode)).Trim();
			return _marketsByCode.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Injective market '{code}'.");
		}
	}

	private InjectiveMarket GetMarket(string marketId)
	{
		using (_sync.EnterScope())
			return _marketsById.TryGetValue(marketId ?? string.Empty,
				out var market) ? market : null;
	}

	private InjectiveMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _marketsById.Values];
	}

	private void UpdateServerTime(DateTime value, long? height = null)
	{
		value = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
		using (_sync.EnterScope())
		{
			if (value > _serverTime)
				_serverTime = value;
			if (height is long currentHeight && currentHeight > _currentHeight)
				_currentHeight = currentHeight;
		}
	}

	private static string StreamKey(InjectiveMarket market, string stream)
		=> stream + ':' + market.Kind + ':' + market.MarketId;

	private static bool AddReference(Dictionary<string, int> references,
		string key)
	{
		if (references.TryGetValue(key, out var count))
		{
			references[key] = count + 1;
			return false;
		}
		references[key] = 1;
		return true;
	}

	private static bool ReleaseReference(Dictionary<string, int> references,
		string key)
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

	private static long ParseTransactionId(string value)
		=> long.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var id) ? id : 0;

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		_transactionSync.Dispose();
		base.DisposeManaged();
	}
}
