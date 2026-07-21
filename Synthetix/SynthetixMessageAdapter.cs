namespace StockSharp.Synthetix;

/// <summary>The message adapter for Synthetix perpetual futures.</summary>
[MediaIcon(Media.MediaNames.synthetix)]
[Doc("topics/api/connectors/crypto_exchanges/synthetix.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SynthetixKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(SynthetixOrderCondition))]
public partial class SynthetixMessageAdapter : MessageAdapter
{
	private class MarketSubscription
	{
		public long TransactionId { get; init; }
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public string Interval { get; init; }
		public SynthetixCandleUpdate LastCandle { get; set; }
	}

	private sealed class OrderSubscription
	{
		public string Symbol { get; init; }
		public string OrderId { get; init; }
		public Sides? Side { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Limit { get; init; }
	}

	private readonly record struct AccountFingerprint(decimal Current,
		decimal Available, decimal InitialMargin, decimal MaintenanceMargin,
		decimal UnrealizedPnl, decimal Debt);
	private readonly record struct CollateralFingerprint(decimal Current,
		decimal Available, decimal Blocked);
	private readonly record struct PositionFingerprint(decimal Current,
		decimal AveragePrice, decimal RealizedPnl, decimal UnrealizedPnl,
		decimal LiquidationPrice, decimal UsedMargin, Sides Side);
	private readonly record struct OrderFingerprint(OrderStates State,
		decimal Price, decimal Volume, decimal Balance);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, SynthetixMarket> _markets =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, SynthetixMarketPrice> _prices =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<string, int> _streamReferences =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenTrades = new(StringComparer.Ordinal);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<long, AccountFingerprint>
		_accountFingerprints = [];
	private readonly Dictionary<string, CollateralFingerprint>
		_collateralFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, PositionFingerprint>
		_positionFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, long> _transactionByClientOrder =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _transactionByVenueOrder =
		new(StringComparer.Ordinal);
	private SynthetixApiClient _apiClient;
	private SynthetixSocketClient _socketClient;
	private SynthetixSigner _signer;
	private string _portfolioName;
	private DateTime _serverTime;
	private DateTime _nextPrivatePoll;
	private DateTime _nextPing;
	private bool _isPrivateSocketSubscribed;

	/// <summary>Supported candle time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
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
		TimeSpan.FromDays(3),
		TimeSpan.FromDays(7),
	];

	/// <summary>Initializes a new instance.</summary>
	public SynthetixMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.Synthetix];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Synthetix) ||
			securityId.IsAssociated(BoardCodes.Synthetix);

	private SynthetixApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private SynthetixSocketClient SocketClient => _socketClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private SynthetixSigner Signer => _signer ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private string PortfolioName => _portfolioName ?? throw new
		InvalidOperationException(
			"A Synthetix subaccount and private key are required.");

	private void EnsureConnected()
	{
		if (_apiClient is null || _socketClient is null || _signer is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (SubAccountId.IsEmpty() || !Signer.IsAvailable)
			throw new InvalidOperationException(
				"A Synthetix subaccount and EVM private key are required.");
	}

	private void EnsureTradingReady() => EnsureAccountReady();

	private SynthetixMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Synthetix.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Synthetix market '{symbol}'.");
	}

	private SynthetixMarket GetMarket(string symbol)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol ?? string.Empty, out var market)
				? market
				: null;
	}

	private SynthetixMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private void UpdateServerTime(DateTime time)
	{
		time = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private static string StreamKey(string symbol, string stream)
		=> symbol + "|" + stream;

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

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
