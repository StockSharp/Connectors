namespace StockSharp.WazirX;

public partial class WazirXMessageAdapter
{
	private const int _maximumRememberedTradeIds = 10000;

	private class MarketSubscription
	{
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription :
		MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription :
		MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }

		public string Symbol { get; init; }

		public Sides Side { get; init; }

		public OrderTypes OrderType { get; init; }

		public decimal Volume { get; init; }

		public decimal Price { get; init; }
	}

	private sealed class BookState
	{
		public SortedDictionary<decimal, decimal> Bids { get; } =
			new(Comparer<decimal>.Create(
				static (left, right) => right.CompareTo(left)));

		public SortedDictionary<decimal, decimal> Asks { get; } = [];
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, WazirXMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, WazirXTicker> _tickers =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<string, int> _streamReferences =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BookState> _books =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, TrackedOrder> _trackedOrders = [];
	private readonly HashSet<string> _seenTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenTradeOrder = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private WazirXRestClient _restClient;
	private WazirXWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPrivatePoll;
	private DateTime _authRefreshAt;
	private long _lastPrivateTradeId;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="WazirXMessageAdapter"/>.
	/// </summary>
	public WazirXMessageAdapter(
		IdGenerator transactionIdGenerator)
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
	public override bool IsAllDownloadingSupported(
		DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.WazirX];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(
		SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.WazirX) ||
			securityId.IsAssociated(BoardCodes.WazirX);

	private WazirXRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private WazirXWsClient WsClient
		=> _wsClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _wsClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"WazirX API key and secret are required for " +
					"private operations.");
	}

	private WazirXMarket GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.WazirX) &&
			!securityId.IsAssociated(BoardCodes.WazirX))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not " +
					"WazirX.");
		return GetMarket(
			securityId.SecurityCode.ThrowIfEmpty(
				nameof(securityId.SecurityCode))) ??
			throw new InvalidOperationException(
				$"Unknown WazirX market " +
					$"'{securityId.SecurityCode}'.");
	}

	private WazirXMarket GetMarket(string symbol)
	{
		if (symbol.IsEmpty())
			return null;
		symbol = symbol.Trim()
			.Replace("/", string.Empty)
			.Replace("-", string.Empty)
			.Replace("_", string.Empty)
			.ToLowerInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: null;
	}

	private WazirXMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private void RegisterMarkets(
		IEnumerable<WazirXMarket> markets,
		IEnumerable<WazirXTicker> tickers)
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_tickers.Clear();
			foreach (var market in markets ?? [])
			{
				if (market?.Symbol.IsEmpty() == false)
					_markets[market.Symbol] = market;
			}
			foreach (var ticker in tickers ?? [])
			{
				if (ticker?.Symbol.IsEmpty() == false)
					_tickers[ticker.Symbol] = ticker;
			}
		}
	}

	private bool AddReference(string stream)
	{
		using (_sync.EnterScope())
		{
			if (_streamReferences.TryGetValue(
				stream, out var count))
			{
				_streamReferences[stream] = count + 1;
				return false;
			}
			_streamReferences.Add(stream, 1);
			return true;
		}
	}

	private bool ReleaseReference(string stream)
	{
		using (_sync.EnterScope())
		{
			if (!_streamReferences.TryGetValue(
				stream, out var count))
				return false;
			if (count > 1)
			{
				_streamReferences[stream] = count - 1;
				return false;
			}
			_streamReferences.Remove(stream);
			return true;
		}
	}

	private void TrackOrder(
		long orderId,
		TrackedOrder order)
	{
		if (orderId <= 0 || order is null)
			return;
		using (_sync.EnterScope())
			_trackedOrders[orderId] = order;
	}

	private TrackedOrder GetTrackedOrder(long orderId)
	{
		if (orderId <= 0)
			return null;
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(
				orderId, out var order)
					? order
					: null;
	}

	private bool AddTrade(string type, long id)
	{
		if (type.IsEmpty() || id <= 0)
			return false;
		using (_sync.EnterScope())
		{
			var key = type + ":" +
				id.ToString(CultureInfo.InvariantCulture);
			if (!_seenTradeIds.Add(key))
				return false;
			_seenTradeOrder.Enqueue(key);
			while (_seenTradeOrder.Count >
				_maximumRememberedTradeIds)
				_seenTradeIds.Remove(
					_seenTradeOrder.Dequeue());
			return true;
		}
	}

	private string GetPortfolioName()
		=> $"WAZIRX_{Key.ToId()}";

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_tickers.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_books.Clear();
			_trackedOrders.Clear();
			_seenTradeIds.Clear();
			_seenTradeOrder.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPrivatePoll = default;
		_authRefreshAt = default;
		_lastPrivateTradeId = 0;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_pollSync.Dispose();
		base.DisposeManaged();
	}
}
