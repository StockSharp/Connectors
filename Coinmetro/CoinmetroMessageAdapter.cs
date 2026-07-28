namespace StockSharp.Coinmetro;

public partial class CoinmetroMessageAdapter
{
	private const int _maximumRememberedTradeIds = 10000;

	private class MarketSubscription
	{
		public string Pair { get; init; }

		public string SecurityCode { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }

		public string Pair { get; init; }

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

		public long Sequence { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, CoinmetroMarket>
		_marketsBySecurity =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, CoinmetroMarket>
		_marketsByPair =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, CoinmetroTicker>
		_tickers =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<string, int>
		_bookReferences =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BookState>
		_orderBooks =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TrackedOrder>
		_trackedOrders =
			new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenTradeOrder = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private CoinmetroRestClient _restClient;
	private CoinmetroWsClient _wsClient;
	private int _tickReferences;
	private int _privateReferences;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPrivatePoll;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="CoinmetroMessageAdapter"/>.
	/// </summary>
	public CoinmetroMessageAdapter(
		IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
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
		=> false;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards =>
		[BoardCodes.Coinmetro];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(
		SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.Coinmetro) ||
			securityId.IsAssociated(BoardCodes.Coinmetro);

	private CoinmetroRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private CoinmetroWsClient WsClient
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
				"Coinmetro bearer token is required for private " +
					"operations.");
	}

	private CoinmetroMarket GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.Coinmetro) &&
			!securityId.IsAssociated(BoardCodes.Coinmetro))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not " +
					"Coinmetro.");
		return GetMarket(
			securityId.SecurityCode.ThrowIfEmpty(
				nameof(securityId.SecurityCode))) ??
			throw new InvalidOperationException(
				$"Unknown Coinmetro market " +
					$"'{securityId.SecurityCode}'.");
	}

	private CoinmetroMarket GetMarket(string value)
	{
		if (value.IsEmpty())
			return null;
		value = value.Trim();
		using (_sync.EnterScope())
		{
			if (_marketsByPair.TryGetValue(value, out var market) ||
				_marketsBySecurity.TryGetValue(value, out market))
				return market;
			var compact = value
				.Replace("/", string.Empty)
				.Replace("-", string.Empty)
				.Replace("_", string.Empty);
			return _marketsByPair.TryGetValue(
				compact, out market)
					? market
					: null;
		}
	}

	private CoinmetroMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _marketsBySecurity.Values];
	}

	private void RegisterMarkets(
		IEnumerable<CoinmetroMarket> markets,
		IEnumerable<CoinmetroTicker> tickers)
	{
		using (_sync.EnterScope())
		{
			_marketsBySecurity.Clear();
			_marketsByPair.Clear();
			_tickers.Clear();
			foreach (var market in markets ?? [])
			{
				if (market?.Pair.IsEmpty() != false ||
					market.BaseCurrency.IsEmpty() ||
					market.QuoteCurrency.IsEmpty())
					continue;
				_marketsBySecurity[market.SecurityCode] = market;
				_marketsByPair[market.Pair] = market;
			}
			foreach (var ticker in tickers ?? [])
			{
				if (ticker?.Pair.IsEmpty() == false)
					_tickers[ticker.Pair] = ticker;
			}
		}
	}

	private string GetPortfolioName()
		=> $"Coinmetro_{RestClient.AccessToken.Secure().ToId()}";

	private void TrackOrder(
		string orderId,
		TrackedOrder order)
	{
		if (orderId.IsEmpty() || order is null)
			return;
		using (_sync.EnterScope())
			_trackedOrders[orderId] = order;
	}

	private TrackedOrder GetTrackedOrder(string orderId)
	{
		if (orderId.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(
				orderId, out var order)
					? order
					: null;
	}

	private bool AddTrade(string pair, string tradeId)
	{
		if (pair.IsEmpty() || tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			var key = pair + ":" + tradeId;
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

	private bool AddBookReference(string pair)
	{
		using (_sync.EnterScope())
		{
			if (_bookReferences.TryGetValue(pair, out var count))
			{
				_bookReferences[pair] = count + 1;
				return false;
			}
			_bookReferences.Add(pair, 1);
			return true;
		}
	}

	private bool ReleaseBookReference(string pair)
	{
		using (_sync.EnterScope())
		{
			if (!_bookReferences.TryGetValue(pair, out var count))
				return false;
			if (count > 1)
			{
				_bookReferences[pair] = count - 1;
				return false;
			}
			_bookReferences.Remove(pair);
			return true;
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_marketsBySecurity.Clear();
			_marketsByPair.Clear();
			_tickers.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_bookReferences.Clear();
			_orderBooks.Clear();
			_trackedOrders.Clear();
			_seenTradeIds.Clear();
			_seenTradeOrder.Clear();
			_tickReferences = 0;
			_privateReferences = 0;
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPrivatePoll = default;
	}

	private static string ResolveOrderId(
		long? numericOrderId,
		string stringOrderId)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId;
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(
				CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			"Coinmetro operation requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_pollSync.Dispose();
		base.DisposeManaged();
	}
}
