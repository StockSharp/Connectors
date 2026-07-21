namespace StockSharp.Polymarket;

/// <summary>The message adapter for Polymarket CLOB.</summary>
[MediaIcon(Media.MediaNames.polymarket)]
[Doc("topics/api/connectors/crypto_exchanges/polymarket.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PolymarketKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks)]
public partial class PolymarketMessageAdapter : MessageAdapter
{
	private sealed class PolymarketOrderSubscription
	{
		public SecurityId SecurityId { get; init; }
		public SecurityId[] SecurityIds { get; init; }
		public string OrderId { get; init; }
		public Sides? Side { get; init; }
		public decimal? Volume { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Limit { get; init; }
	}

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	private readonly Lock _sync = new();
	private readonly Dictionary<string, PolymarketMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PolymarketMarket> _marketsByToken =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, PolymarketBookState> _books =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, PolymarketMarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, PolymarketDepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, PolymarketMarketSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<string, int> _socketReferences =
		new(StringComparer.Ordinal);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, PolymarketOrderSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, long> _transactionsByOrder =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _knownPositions =
		new(StringComparer.Ordinal);
	private PolymarketAuthenticator _authenticator;
	private PolymarketSigner _signer;
	private PolymarketRestClient _restClient;
	private PolymarketSocketClient _socketClient;
	private string _portfolioAddress;
	private string _portfolioName;
	private int _orderVersion;
	private DateTime _serverTime;
	private DateTime _nextPing;
	private DateTime _nextPrivatePoll;
	private DateTime _nextMarketRefresh;

	/// <summary>Initializes a new instance.</summary>
	public PolymarketMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(_timeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Polymarket];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Polymarket) ||
			securityId.IsAssociated(BoardCodes.Polymarket);

	private PolymarketRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private PolymarketSocketClient SocketClient => _socketClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private PolymarketSigner Signer => _signer ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private void UpdateServerTime(DateTime time)
	{
		time = time.EnsureUtc();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private PolymarketMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode +
				"' is not Polymarket.");
		using (_sync.EnterScope())
		{
			if (securityId.Native is string tokenId &&
				_marketsByToken.TryGetValue(tokenId, out var nativeMarket))
				return nativeMarket;
			var code = securityId.SecurityCode.ThrowIfEmpty(
				nameof(securityId.SecurityCode)).Trim();
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					"Unknown Polymarket outcome '" + code + "'.");
		}
	}

	private PolymarketMarket GetMarketByToken(string tokenId)
	{
		if (tokenId.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _marketsByToken.TryGetValue(tokenId, out var market)
				? market
				: null;
	}

	private PolymarketMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private void EnsureConnected()
	{
		if (_restClient is null || _socketClient is null || _signer is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (_authenticator?.IsAvailable != true)
			throw new InvalidOperationException(
				"Polymarket API credentials are required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (!Signer.IsAvailable)
			throw new InvalidOperationException(
				"A Polymarket EVM private key is required for trading.");
		if (_portfolioAddress.IsEmpty())
			throw new InvalidOperationException(
				"A Polymarket funder address is required for trading.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.Equals(_portfolioName,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Unknown Polymarket portfolio '" + portfolioName + "'.");
	}

	private bool AddSocketReference(string tokenId)
	{
		if (_socketReferences.TryGetValue(tokenId, out var count))
		{
			_socketReferences[tokenId] = count + 1;
			return false;
		}
		_socketReferences.Add(tokenId, 1);
		return true;
	}

	private bool ReleaseSocketReference(string tokenId)
	{
		if (!_socketReferences.TryGetValue(tokenId, out var count))
			return false;
		if (count > 1)
		{
			_socketReferences[tokenId] = count - 1;
			return false;
		}
		_socketReferences.Remove(tokenId);
		return true;
	}

	private long GetOrderTransactionId(string orderId, long fallback)
	{
		using (_sync.EnterScope())
			return !orderId.IsEmpty() &&
				_transactionsByOrder.TryGetValue(orderId, out var id)
					? id
					: fallback;
	}

	private void TrackOrder(string orderId, long transactionId)
	{
		if (orderId.IsEmpty() || transactionId == 0)
			return;
		using (_sync.EnterScope())
			_transactionsByOrder[orderId] = transactionId;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByToken.Clear();
			_books.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_socketReferences.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_transactionsByOrder.Clear();
			_seenAccountTrades.Clear();
			_knownPositions.Clear();
			_serverTime = default;
			_nextPing = default;
			_nextPrivatePoll = default;
			_nextMarketRefresh = default;
		}
		_authenticator = null;
		_signer = null;
		_portfolioAddress = null;
		_portfolioName = null;
		_orderVersion = 0;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
