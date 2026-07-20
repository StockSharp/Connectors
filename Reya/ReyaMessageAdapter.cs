namespace StockSharp.Reya;

/// <summary>The message adapter for Reya DEX spot and perpetual markets.</summary>
[MediaIcon(Media.MediaNames.reya)]
[Doc("topics/api/connectors/crypto_exchanges/reya.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ReyaKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(ReyaOrderCondition))]
public partial class ReyaMessageAdapter : MessageAdapter
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
		public CandleState Current { get; set; }
	}

	private sealed class OrderStatusSubscription
	{
		public string[] Symbols { get; init; }
		public long? OrderId { get; init; }
		public string OrderStringId { get; init; }
		public string UserOrderId { get; init; }
		public Sides? Side { get; init; }
		public decimal? Volume { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Limit { get; init; }
	}

	private sealed class CandleState
	{
		public DateTime OpenTime { get; init; }
		public decimal OpenPrice { get; init; }
		public decimal HighPrice { get; set; }
		public decimal LowPrice { get; set; }
		public decimal ClosePrice { get; set; }
		public decimal TotalVolume { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, ReyaMarket> _markets =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, ReyaPriceState> _prices =
		new(StringComparer.Ordinal);
	private readonly Dictionary<ReyaAccountTypes, BigInteger> _accounts = [];
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<string, int> _streamReferences =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, long> _transactionByOrderId =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _symbolByOrderId =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, long> _clientIdByOrderId =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, long> _transactionByClientId = [];
	private readonly HashSet<string> _seenPublicTrades =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _knownPositions =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _knownBalances =
		new(StringComparer.Ordinal);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderStatusSubscription>
		_orderSubscriptions = [];
	private ReyaRestClient _restClient;
	private ReyaSocketClient _socket;
	private ReyaSigner _signer;
	private BigInteger? _configuredAccountId;
	private string _ownerAddress;
	private string _portfolioName;
	private DateTime _serverTime;

	/// <summary>Supported candle time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => ReyaExtensions.TimeFrames;

	/// <summary>Initializes a new instance.</summary>
	public ReyaMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
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
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Reya];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Reya) ||
			securityId.IsAssociated(BoardCodes.Reya);

	private ReyaRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private ReyaSocketClient Socket => _socket ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private ReyaSigner Signer => _signer ?? throw new InvalidOperationException(
		"A Reya EVM signer private key is required for trading.");

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
			"A Reya owner wallet address is required for account data.");

	private void UpdateServerTime(long timestamp)
	{
		if (timestamp > 0)
			UpdateServerTime(timestamp.FromReyaMilliseconds());
	}

	private void UpdateServerTime(DateTime time)
	{
		time = time.EnsureReyaUtc();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private void EnsureConnected()
	{
		if (_restClient is null || _socket is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (_ownerAddress.IsEmpty())
			throw new InvalidOperationException(
				"A Reya owner wallet address is required for account data.");
	}

	private void EnsureTradingReady(ReyaMarket market)
	{
		EnsureAccountReady();
		_ = Signer;
		_ = GetAccountId(market);
	}

	private ReyaMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode + "' is not Reya.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					"Unknown or incorrectly cased Reya symbol '" + symbol + "'.");
	}

	private ReyaMarket GetMarket(string symbol)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol ?? string.Empty, out var market)
				? market
				: null;
	}

	private ReyaMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private ReyaPriceState GetPriceState(string symbol)
	{
		using (_sync.EnterScope())
			return _prices.TryGetValue(symbol ?? string.Empty, out var state)
				? state
				: null;
	}

	private BigInteger GetAccountId(ReyaMarket market)
	{
		if (_configuredAccountId is BigInteger configured)
			return configured;
		using (_sync.EnterScope())
		{
			var type = market.IsSpot
				? ReyaAccountTypes.Spot
				: ReyaAccountTypes.MainPerpetual;
			if (_accounts.TryGetValue(type, out var accountId))
				return accountId;
			if (!market.IsSpot &&
				_accounts.TryGetValue(ReyaAccountTypes.SubPerpetual,
					out accountId))
				return accountId;
		}
		throw new InvalidOperationException(
			"No Reya " + (market.IsSpot ? "spot" : "perpetual") +
			" account is available for the owner wallet.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.Equals(PortfolioName,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Unknown Reya portfolio '" + portfolioName + "'.");
	}

	private long GetTransactionId(string orderId, long? clientOrderId)
	{
		using (_sync.EnterScope())
		{
			if (!orderId.IsEmpty() && _transactionByOrderId.TryGetValue(orderId,
				out var transactionId))
				return transactionId;
			if (clientOrderId is long id &&
				_transactionByClientId.TryGetValue(id, out transactionId))
				return transactionId;
			return 0;
		}
	}

	private void TrackOrder(string orderId, long? clientOrderId,
		long transactionId, string symbol)
	{
		using (_sync.EnterScope())
		{
			if (!orderId.IsEmpty())
			{
				if (transactionId != 0)
					_transactionByOrderId[orderId] = transactionId;
				if (!symbol.IsEmpty())
					_symbolByOrderId[orderId] = symbol;
				if (clientOrderId is long clientId)
					_clientIdByOrderId[orderId] = clientId;
			}
			if (clientOrderId is long id && transactionId != 0)
				_transactionByClientId[id] = transactionId;
		}
	}

	private long? GetClientOrderId(string orderId)
	{
		using (_sync.EnterScope())
			return !orderId.IsEmpty() && _clientIdByOrderId.TryGetValue(orderId,
				out var clientOrderId)
					? clientOrderId
					: null;
	}

	private string GetOrderSymbol(string orderId)
	{
		using (_sync.EnterScope())
			return !orderId.IsEmpty() && _symbolByOrderId.TryGetValue(orderId,
				out var symbol)
				? symbol
				: null;
	}

	private bool TryAcceptTrade(HashSet<string> seen, string key)
	{
		if (key.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			var added = seen.Add(key);
			if (seen.Count > 16384)
				foreach (var old in seen.Take(8192).ToArray())
					seen.Remove(old);
			return added;
		}
	}

	private static bool AddReference(IDictionary<string, int> references,
		string channel)
	{
		if (references.TryGetValue(channel, out var count))
		{
			references[channel] = checked(count + 1);
			return false;
		}
		references.Add(channel, 1);
		return true;
	}

	private static bool ReleaseReference(IDictionary<string, int> references,
		string channel)
	{
		if (!references.TryGetValue(channel, out var count))
			return false;
		if (count > 1)
		{
			references[channel] = count - 1;
			return false;
		}
		references.Remove(channel);
		return true;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_prices.Clear();
			_accounts.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_transactionByOrderId.Clear();
			_symbolByOrderId.Clear();
			_clientIdByOrderId.Clear();
			_transactionByClientId.Clear();
			_seenPublicTrades.Clear();
			_seenAccountTrades.Clear();
			_knownPositions.Clear();
			_knownBalances.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_serverTime = default;
		}
		_configuredAccountId = null;
		_ownerAddress = null;
		_portfolioName = null;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_socket?.Dispose();
		_restClient?.Dispose();
		base.DisposeManaged();
	}
}
