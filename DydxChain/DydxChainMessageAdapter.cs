namespace StockSharp.DydxChain;

/// <summary>The message adapter for dYdX Chain perpetual markets.</summary>
[MediaIcon(Media.MediaNames.dydx_chain)]
[Doc("topics/api/connectors/crypto_exchanges/dydx_chain.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DydxChainKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(DydxChainOrderCondition))]
public partial class DydxChainMessageAdapter : MessageAdapter
{
	private sealed class DepthSubscription
	{
		public string Ticker { get; init; }
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription
	{
		public string Ticker { get; init; }
		public DydxChainCandleResolutions Resolution { get; init; }
	}

	private sealed class OrderStatusSubscription
	{
		public string OrderId { get; init; }
		public string ClientId { get; init; }
		public string[] Tickers { get; init; }
		public Sides? Side { get; init; }
		public decimal? Volume { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Maximum { get; init; }
	}

	private sealed class BookState
	{
		private static readonly IComparer<decimal> _descending =
			Comparer<decimal>.Create(static (left, right) =>
				right.CompareTo(left));

		public SortedDictionary<decimal, decimal> Bids { get; } =
			new(_descending);
		public SortedDictionary<decimal, decimal> Asks { get; } = [];

		public void Reset(DydxChainOrderbookResponse snapshot)
		{
			ArgumentNullException.ThrowIfNull(snapshot);
			Bids.Clear();
			Asks.Clear();
			Apply(Bids, snapshot.Bids);
			Apply(Asks, snapshot.Asks);
		}

		public void Apply(DydxChainOrderbookUpdate update)
		{
			ArgumentNullException.ThrowIfNull(update);
			Apply(Bids, update.Bids);
			Apply(Asks, update.Asks);
		}

		private static void Apply(SortedDictionary<decimal, decimal> side,
			DydxChainPriceLevel[] levels)
		{
			foreach (var level in levels ?? [])
			{
				if (level is null)
					continue;
				var price = level.Price.ParseDecimal("order-book price");
				var size = level.Size.ParseDecimal("order-book size", true);
				if (price <= 0)
					throw new InvalidDataException(
						"dYdX returned a non-positive order-book price.");
				if (size == 0)
					side.Remove(price);
				else
					side[price] = size;
			}
		}
	}

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _transactionSync = new(1, 1);
	private readonly Dictionary<string, DydxChainMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<uint, DydxChainMarket> _marketsByPairId = [];
	private readonly Dictionary<string, decimal> _oraclePrices =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BookState> _books =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, string> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, string> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<DydxChainSocketSubscriptionKey, int>
		_streamReferences = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderStatusSubscription>
		_orderStatusSubscriptions = [];
	private readonly Dictionary<uint, long> _transactionByClientId = [];
	private readonly Dictionary<string, DydxChainOrderIdentity> _ordersById =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<uint, DydxChainOrderIdentity> _ordersByClientId = [];
	private readonly Dictionary<string, DydxChainOrder> _knownOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenFillIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _knownPositionTickers =
		new(StringComparer.OrdinalIgnoreCase);
	private DydxChainApiClient _apiClient;
	private DydxChainSocketClient _socketClient;
	private DydxChainSigner _signer;
	private string _portfolioName;
	private DateTime _serverTime;
	private uint _currentHeight;

	/// <summary>Initializes a new instance.</summary>
	public DydxChainMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(15);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(DydxChainExtensions.TimeFrames);
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
	public override string[] AssociatedBoards => [BoardCodes.DydxChain];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.DydxChain) ||
			securityId.IsAssociated(BoardCodes.DydxChain);

	private DydxChainApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DydxChainSocketClient SocketClient => _socketClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DydxChainSigner Signer => _signer ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private uint CurrentHeight
	{
		get
		{
			using (_sync.EnterScope())
				return _currentHeight;
		}
	}

	private void UpdateServer(DateTime time, uint? height = null)
	{
		time = time.EnsureUtc();
		using (_sync.EnterScope())
		{
			if (time > _serverTime)
				_serverTime = time;
			if (height is uint value && value > _currentHeight)
				_currentHeight = value;
		}
	}

	private DydxChainMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not dYdX Chain.");
		var ticker = securityId.SecurityCode.NormalizeTicker();
		using (_sync.EnterScope())
			return _markets.TryGetValue(ticker, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown dYdX market '{ticker}'.");
	}

	private bool TryGetMarket(string ticker, out DydxChainMarket market)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(ticker ?? string.Empty, out market);
	}

	private DydxChainMarket GetMarket(uint clobPairId)
	{
		using (_sync.EnterScope())
			return _marketsByPairId.TryGetValue(clobPairId, out var market)
				? market
				: null;
	}

	private DydxChainMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private decimal? GetOraclePrice(string ticker)
	{
		using (_sync.EnterScope())
			return _oraclePrices.TryGetValue(ticker ?? string.Empty,
				out var price) ? price : null;
	}

	private void EnsureConnected()
	{
		if (_apiClient is null || _socketClient is null || _signer is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (!Signer.IsWalletAvailable)
			throw new InvalidOperationException(
				"A dYdX wallet address is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (!Signer.IsSigningAvailable)
			throw new InvalidOperationException(
				"A dYdX private key is required for transactions.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.Equals(_portfolioName,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				$"Unknown dYdX portfolio '{portfolioName}'.");
	}

	private long GetTransactionId(uint clientId)
	{
		using (_sync.EnterScope())
			return _transactionByClientId.TryGetValue(clientId,
				out var transactionId) ? transactionId : 0;
	}

	private bool TryAcceptFill(string fillId)
	{
		if (fillId.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			var isAdded = _seenFillIds.Add(fillId);
			if (_seenFillIds.Count > 16384)
				foreach (var old in _seenFillIds.Take(
					_seenFillIds.Count - 8192).ToArray())
					_seenFillIds.Remove(old);
			return isAdded;
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByPairId.Clear();
			_oraclePrices.Clear();
			_books.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_portfolioSubscriptions.Clear();
			_orderStatusSubscriptions.Clear();
			_transactionByClientId.Clear();
			_ordersById.Clear();
			_ordersByClientId.Clear();
			_knownOrders.Clear();
			_seenFillIds.Clear();
			_knownPositionTickers.Clear();
			_serverTime = default;
			_currentHeight = 0;
		}
		_portfolioName = null;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_socketClient?.Dispose();
		_apiClient?.Dispose();
		_signer?.Dispose();
		_transactionSync.Dispose();
		base.DisposeManaged();
	}
}
