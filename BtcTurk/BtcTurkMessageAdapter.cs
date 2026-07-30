namespace StockSharp.BtcTurk;

public partial class BtcTurkMessageAdapter
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
		public string SecurityCode { get; init; }
		public string ClientOrderId { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public decimal? TriggerPrice { get; init; }
	}

	private readonly record struct StreamKey(string Channel,
		string NativeSymbol);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, BtcTurkMarket> _marketsBySecurity =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BtcTurkMarket> _marketsByNative =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<long, TrackedOrder> _trackedOrders = [];
	private readonly HashSet<long> _knownActiveOrderIds = [];
	private readonly HashSet<long> _seenTradeIds = [];
	private readonly Queue<long> _seenTradeOrder = [];
	private readonly HashSet<string> _seenPublicTradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Queue<string> _seenPublicTradeOrder = [];
	private readonly SemaphoreSlim _privatePollSync = new(1, 1);
	private BtcTurkRestClient _restClient;
	private BtcTurkWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPrivatePoll;

	/// <summary>
	/// Initializes a new instance of the <see cref="BtcTurkMessageAdapter"/>.
	/// </summary>
	public BtcTurkMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
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
		=> false;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.BtcTurk];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BtcTurk) ||
			securityId.IsAssociated(BoardCodes.BtcTurk);

	private BtcTurkRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private BtcTurkWsClient WsClient
		=> _wsClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _wsClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"BtcTurk API key and secret are required for private operations.");
	}

	private BtcTurkMarket GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BtcTurk) &&
			!securityId.IsAssociated(BoardCodes.BtcTurk))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not BtcTurk.");

		var requested = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
		{
			if (_marketsByNative.TryGetValue(requested, out var market))
				return market;
			try
			{
				var normalized = requested.NormalizeSymbol();
				if (_marketsBySecurity.TryGetValue(normalized, out market))
					return market;
			}
			catch (FormatException)
			{
			}
			var compact = requested.Replace("/", string.Empty)
				.Replace("-", string.Empty).Replace("_", string.Empty);
			if (_marketsByNative.TryGetValue(compact, out market))
				return market;
		}
		throw new InvalidOperationException(
			$"Unknown BtcTurk market '{requested}'.");
	}

	private void RegisterMarkets(IEnumerable<BtcTurkMarket> markets)
	{
		using (_sync.EnterScope())
		{
			_marketsBySecurity.Clear();
			_marketsByNative.Clear();

			foreach (var market in markets ?? [])
			{
				if (market?.NativeSymbol.IsEmpty() != false ||
					market.Numerator.IsEmpty() ||
					market.Denominator.IsEmpty())
					continue;
				market.NativeSymbol = market.NativeSymbol.ToUpperInvariant();
				_marketsBySecurity[market.SecurityCode] = market;
				_marketsByNative[market.NativeSymbol] = market;
			}
		}
	}

	private static bool AddReference(
		IDictionary<StreamKey, int> references, StreamKey key)
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
		IDictionary<StreamKey, int> references, StreamKey key)
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
		=> $"BtcTurk_{Key.ToId()}";

	private void TrackOrder(long orderId, TrackedOrder order)
	{
		if (orderId <= 0 || order is null)
			return;
		using (_sync.EnterScope())
		{
			_trackedOrders[orderId] = order;
			_knownActiveOrderIds.Add(orderId);
		}
	}

	private TrackedOrder GetTrackedOrder(long orderId)
	{
		if (orderId <= 0)
			return null;
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(orderId, out var order)
				? order
				: null;
	}

	private bool AddTrade(long tradeId)
	{
		if (tradeId <= 0)
			return false;
		using (_sync.EnterScope())
		{
			if (!_seenTradeIds.Add(tradeId))
				return false;
			_seenTradeOrder.Enqueue(tradeId);

			while (_seenTradeOrder.Count > _maximumRememberedTradeIds)
				_seenTradeIds.Remove(_seenTradeOrder.Dequeue());

			return true;
		}
	}

	private bool AddPublicTrade(string pairSymbol, string tradeId)
	{
		if (pairSymbol.IsEmpty() || tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			var key = $"{pairSymbol}:{tradeId}";
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
			_trackedOrders.Clear();
			_knownActiveOrderIds.Clear();
			_seenTradeIds.Clear();
			_seenTradeOrder.Clear();
			_seenPublicTradeIds.Clear();
			_seenPublicTradeOrder.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPrivatePoll = default;
	}

	private static long ResolveOrderId(long? numericOrderId,
		string stringOrderId, string operation)
	{
		if (numericOrderId is > 0)
			return numericOrderId.Value;
		if (long.TryParse(stringOrderId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var orderId) && orderId > 0)
			return orderId;
		throw new InvalidOperationException(
			$"BtcTurk {operation} requires a numeric exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_privatePollSync.Dispose();
		base.DisposeManaged();
	}
}
