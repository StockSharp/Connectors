namespace StockSharp.Bluefin;

/// <summary>The message adapter for Bluefin Pro.</summary>
[MediaIcon(Media.MediaNames.bluefin)]
[Doc("topics/api/connectors/crypto_exchanges/bluefin.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BluefinKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(BluefinOrderCondition))]
public partial class BluefinMessageAdapter : MessageAdapter
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
		public BluefinMarketStreamPayload LastCandle { get; set; }
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

	private sealed class OrderBookState
	{
		private readonly SortedDictionary<decimal, decimal> _bids =
			new(Comparer<decimal>.Create(static (left, right) =>
				right.CompareTo(left)));
		private readonly SortedDictionary<decimal, decimal> _asks = [];

		public bool IsInitialized { get; private set; }
		public long LastUpdateId { get; private set; }
		public DateTime ServerTime { get; private set; }

		public void ApplySnapshot(BluefinDepth depth)
		{
			ArgumentNullException.ThrowIfNull(depth);
			_bids.Clear();
			_asks.Clear();
			ApplyLevels(_bids, depth.BidsE9, "bid");
			ApplyLevels(_asks, depth.AsksE9, "ask");
			LastUpdateId = depth.LastUpdateId;
			ServerTime = GetTime(depth.UpdatedAtMillis,
				depth.ResponseSentAtMillis);
			IsInitialized = true;
		}

		public bool TryApply(BluefinMarketStreamPayload update)
		{
			ArgumentNullException.ThrowIfNull(update);
			if (!IsInitialized)
				return false;
			if (update.LastUpdateId <= LastUpdateId)
				return true;
			if (update.FirstUpdateId > LastUpdateId + 1)
				return false;
			ApplyLevels(_bids, update.BidsE9, "bid");
			ApplyLevels(_asks, update.AsksE9, "ask");
			LastUpdateId = update.LastUpdateId;
			if (update.UpdatedAtMillis > 0)
				ServerTime = update.UpdatedAtMillis.FromBluefinMilliseconds();
			return true;
		}

		public QuoteChange[] GetBids(int depth)
			=> [.. _bids.Take(depth).Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];

		public QuoteChange[] GetAsks(int depth)
			=> [.. _asks.Take(depth).Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];

		public void Invalidate()
		{
			IsInitialized = false;
			LastUpdateId = 0;
		}

		private static void ApplyLevels(
			SortedDictionary<decimal, decimal> target, string[][] levels,
			string side)
		{
			foreach (var level in levels ?? [])
			{
				if (level is not { Length: >= 2 })
					throw new InvalidDataException(
						$"Bluefin returned a malformed {side} level.");
				var price = level[0].ParseE9(side + " price");
				var volume = level[1].ParseE9(side + " volume");
				if (price <= 0 || volume < 0)
					throw new InvalidDataException(
						$"Bluefin returned an invalid {side} level.");
				if (volume == 0)
					target.Remove(price);
				else
					target[price] = volume;
			}
		}

		private static DateTime GetTime(long updatedAt, long responseSentAt)
		{
			var value = updatedAt > 0 ? updatedAt : responseSentAt;
			return value > 0 ? value.FromBluefinMilliseconds() : DateTime.UtcNow;
		}
	}

	private readonly record struct AccountFingerprint(decimal Current,
		decimal Available, decimal Blocked, decimal Maintenance,
		decimal UnrealizedPnl, decimal Leverage);
	private readonly record struct AssetFingerprint(decimal Current,
		decimal Available);
	private readonly record struct PositionFingerprint(decimal Current,
		decimal AveragePrice, decimal UnrealizedPnl, decimal LiquidationPrice,
		decimal Leverage, Sides Side, bool IsIsolated);
	private readonly record struct OrderFingerprint(OrderStates State,
		decimal Price, decimal Volume, decimal Balance);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, BluefinMarket> _markets =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrderBookState> _books =
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
	private readonly Dictionary<string, AssetFingerprint> _assetFingerprints =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, PositionFingerprint>
		_positionFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.Ordinal);
	private BluefinRestClient _restClient;
	private BluefinSocketClient _socketClient;
	private BluefinSigner _signer;
	private BluefinContractsConfig _contractsConfig;
	private string _accountAddress;
	private string _portfolioName;
	private DateTime _serverTime;
	private DateTime _nextPrivatePoll;
	private DateTime _tokenRefreshTime;
	private long _lastSalt;

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
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	/// <summary>Initializes a new instance.</summary>
	public BluefinMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.Bluefin];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bluefin) ||
			securityId.IsAssociated(BoardCodes.Bluefin);

	private BluefinRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private BluefinSocketClient SocketClient => _socketClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private BluefinSigner Signer => _signer ?? throw new
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
			"A Bluefin account address is required for account data.");

	private void EnsureConnected()
	{
		if (_restClient is null || _socketClient is null || _signer is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (_accountAddress.IsEmpty())
			throw new InvalidOperationException(
				"A Bluefin account address is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (!Signer.IsSigningAvailable || RestClient.AccessToken.IsEmpty())
			throw new InvalidOperationException(
				"A Sui Ed25519 private key is required for Bluefin trading.");
		if (_contractsConfig?.IdsId.IsEmpty() != false)
			throw new InvalidOperationException(
				"Bluefin did not return its internal datastore address.");
	}

	private BluefinMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Bluefin.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Bluefin market '{symbol}'.");
	}

	private BluefinMarket GetMarket(string symbol)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol ?? string.Empty, out var market)
				? market
				: null;
	}

	private BluefinMarket[] GetMarkets()
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
