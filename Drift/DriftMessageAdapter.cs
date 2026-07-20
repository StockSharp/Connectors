namespace StockSharp.Drift;

/// <summary>
/// The message adapter for Drift Protocol's Velocity-compatible relaunch.
/// </summary>
[MediaIcon(Media.MediaNames.drift)]
[Doc("topics/api/connectors/crypto_exchanges/drift.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DriftKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(DriftOrderCondition))]
public partial class DriftMessageAdapter : MessageAdapter
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
		public string Resolution { get; init; }
		public DriftCandle LastCandle { get; set; }
	}

	private sealed class OrderStatusSubscription
	{
		public string Symbol { get; init; }
		public long? OrderId { get; init; }
		public Sides? Side { get; init; }
		public OrderStates[] States { get; init; }
		public int Skip { get; init; }
		public int Limit { get; init; }
	}

	private readonly record struct AccountFingerprint(decimal TotalCollateral,
		decimal FreeCollateral, decimal InitialMargin,
		decimal MaintenanceMargin, decimal Leverage, decimal Health);
	private readonly record struct BalanceFingerprint(decimal Current,
		int OpenOrders, decimal LiquidationPrice);
	private readonly record struct PositionFingerprint(decimal Current,
		decimal AveragePrice, decimal SettledPnl, decimal FeesAndFunding,
		decimal LiquidationPrice, Sides Side);
	private readonly record struct OrderFingerprint(OrderStates State,
		decimal Price, decimal Volume, decimal Balance);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, DriftMarket> _markets =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<string, int> _depthReferences =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _tradeReferences =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _candleReferences =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenTrades = new(StringComparer.Ordinal);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderStatusSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<long, AccountFingerprint>
		_accountFingerprints = [];
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, PositionFingerprint>
		_positionFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, DriftOrder> _knownOrders =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.Ordinal);
	private DriftRestClient _restClient;
	private DriftDataSocketClient _dataSocket;
	private DriftDlobSocketClient _dlobSocket;
	private DriftSigner _signer;
	private string _accountAddress;
	private string _portfolioName;
	private DateTime _serverTime;
	private DateTime _nextPrivatePoll;

	/// <summary>Supported candle time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	/// <summary>Initializes a new instance.</summary>
	public DriftMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.Drift];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Drift) ||
			securityId.IsAssociated(BoardCodes.Drift);

	private DriftRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DriftDataSocketClient DataSocket => _dataSocket ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DriftDlobSocketClient DlobSocket => _dlobSocket ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DriftSigner Signer => _signer ?? throw new
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
			"A Drift account address is required for account data.");

	private void EnsureConnected()
	{
		if (_restClient is null || _dataSocket is null || _dlobSocket is null ||
			_signer is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (_accountAddress.IsEmpty())
			throw new InvalidOperationException(
				"A Drift account address is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (!Signer.IsSigningAvailable)
			throw new InvalidOperationException(
				"A Solana private key is required for Drift trading.");
	}

	private DriftMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Drift.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Drift market '{symbol}'.");
	}

	private DriftMarket GetMarket(string symbol)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol ?? string.Empty, out var market)
				? market
				: null;
	}

	private DriftMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private void UpdateServerTime(DateTime time)
	{
		time = time.Kind == DateTimeKind.Utc
			? time
			: time.ToUniversalTime();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private static bool AddReference(Dictionary<string, int> references,
		string key)
	{
		if (references.TryGetValue(key, out var count))
		{
			references[key] = count + 1;
			return false;
		}
		references.Add(key, 1);
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

	private bool AcceptTrade(DriftTrade trade)
	{
		var key = trade.TransactionSignature + ":" +
			trade.TransactionIndex.ToString(CultureInfo.InvariantCulture);
		using (_sync.EnterScope())
		{
			if (!_seenTrades.Add(key))
				return false;
			if (_seenTrades.Count > 20_000)
				_seenTrades.Clear();
			return true;
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
