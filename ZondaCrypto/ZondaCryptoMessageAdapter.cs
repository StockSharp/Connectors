namespace StockSharp.ZondaCrypto;

public partial class ZondaCryptoMessageAdapter
{
	private const int _maximumRememberedTradeIds = 10000;

	private class MarketSubscription
	{
		public string NativeSymbol { get; init; }

		public string SecurityCode { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }

		public string MarketCode { get; init; }

		public string SecurityCode { get; init; }

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

		public DateTime Time { get; set; }
	}

	private readonly record struct StreamKey(
		string Type,
		string NativeSymbol);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, ZondaCryptoMarket>
		_marketsBySecurity =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ZondaCryptoMarket>
		_marketsByNative =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<StreamKey, int>
		_streamReferences = [];
	private readonly Dictionary<string, BookState>
		_orderBooks =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TrackedOrder>
		_trackedOrders =
			new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenPublicTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenPublicTradeOrder = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private ZondaCryptoRestClient _restClient;
	private ZondaCryptoWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPrivatePoll;
	private DateTime _lastHeartbeat;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="ZondaCryptoMessageAdapter"/>.
	/// </summary>
	public ZondaCryptoMessageAdapter(
		IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(
		DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards =>
		[BoardCodes.ZondaCrypto];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(
		SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.ZondaCrypto) ||
			securityId.IsAssociated(BoardCodes.ZondaCrypto);

	private ZondaCryptoRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private ZondaCryptoWsClient WsClient
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
				"zondacrypto API key and secret are required for " +
					"private operations.");
	}

	private ZondaCryptoMarket GetMarket(
		SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.ZondaCrypto) &&
			!securityId.IsAssociated(BoardCodes.ZondaCrypto))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not " +
					"zondacrypto.");
		return GetMarket(
			securityId.SecurityCode.ThrowIfEmpty(
				nameof(securityId.SecurityCode))) ??
			throw new InvalidOperationException(
				$"Unknown zondacrypto market " +
					$"'{securityId.SecurityCode}'.");
	}

	private ZondaCryptoMarket GetMarket(string value)
	{
		if (value.IsEmpty())
			return null;
		value = value.Trim();
		using (_sync.EnterScope())
		{
			if (_marketsByNative.TryGetValue(value, out var market) ||
				_marketsBySecurity.TryGetValue(value, out market))
				return market;
			try
			{
				return _marketsByNative.TryGetValue(
					value.ToZondaMarketCode(), out market)
						? market
						: null;
			}
			catch (FormatException)
			{
				return null;
			}
		}
	}

	private ZondaCryptoMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _marketsBySecurity.Values];
	}

	private void RegisterMarkets(
		IEnumerable<ZondaCryptoTicker> tickers)
	{
		using (_sync.EnterScope())
		{
			_marketsBySecurity.Clear();
			_marketsByNative.Clear();

			foreach (var market in (tickers ?? [])
				.Select(static ticker => ticker?.Market)
				.Where(static market =>
					market?.Code.IsEmpty() == false &&
					!market.BaseCurrency.IsEmpty() &&
					!market.QuoteCurrency.IsEmpty())
				.GroupBy(
					static market => market.Code,
					StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.First()))
			{
				_marketsBySecurity[market.SecurityCode] = market;
				_marketsByNative[market.Code] = market;
			}
		}
	}

	private static bool AddReference(
		IDictionary<StreamKey, int> references,
		StreamKey key)
	{
		if (references.TryGetValue(key, out var count))
		{
			references[key] = count + 1;
			return false;
		}
		references.Add(key, 1);
		return true;
	}

	private static bool ReleaseReference(
		IDictionary<StreamKey, int> references,
		StreamKey key)
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

	private string GetPortfolioName()
		=> $"ZondaCrypto_{Key.ToId()}";

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

	private bool AddPublicTrade(
		string market,
		string tradeId)
	{
		if (market.IsEmpty() || tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			var key = market + ":" + tradeId;
			if (!_seenPublicTradeIds.Add(key))
				return false;
			_seenPublicTradeOrder.Enqueue(key);

			while (_seenPublicTradeOrder.Count >
				_maximumRememberedTradeIds)
				_seenPublicTradeIds.Remove(
					_seenPublicTradeOrder.Dequeue());

			return true;
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_marketsBySecurity.Clear();
			_marketsByNative.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_streamReferences.Clear();
			_orderBooks.Clear();
			_trackedOrders.Clear();
			_seenPublicTradeIds.Clear();
			_seenPublicTradeOrder.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPrivatePoll = default;
		_lastHeartbeat = default;
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
			"zondacrypto operation requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_pollSync.Dispose();
		base.DisposeManaged();
	}
}
