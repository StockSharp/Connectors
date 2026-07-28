namespace StockSharp.BigOne;

public partial class BigOneMessageAdapter
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
		public int StreamDepth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public string Resolution { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string SecurityCode { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public decimal? TriggerPrice { get; init; }
	}

	private readonly record struct StreamKey(
		string Channel,
		string NativeSymbol,
		int Depth);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, BigOneSymbol> _marketsBySecurity =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BigOneSymbol> _marketsByNative =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _knownActiveOrderIds =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenPublicTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenPublicTradeOrder = [];
	private readonly HashSet<string> _seenPrivateTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenPrivateTradeOrder = [];
	private readonly SemaphoreSlim _privatePollSync = new(1, 1);
	private BigOneRestClient _restClient;
	private BigOneWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPrivatePoll;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="BigOneMessageAdapter"/>.
	/// </summary>
	public BigOneMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.BigOne];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BigOne) ||
			securityId.IsAssociated(BoardCodes.BigOne);

	private BigOneRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private BigOneWsClient WsClient
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
				"BigONE API key and secret are required " +
					"for private operations.");
	}

	private BigOneSymbol GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BigOne) &&
			!securityId.IsAssociated(BoardCodes.BigOne))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not BigONE.");

		var requested = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
		{
			if (_marketsByNative.TryGetValue(requested, out var market))
				return market;
			try
			{
				var normalized = requested.ToBigOneSecurityCode();
				if (_marketsBySecurity.TryGetValue(normalized, out market))
					return market;
			}
			catch (FormatException)
			{
			}
		}
		throw new InvalidOperationException(
			$"Unknown BigONE market '{requested}'.");
	}

	private BigOneSymbol GetMarket(string nativeSymbol)
	{
		using (_sync.EnterScope())
			return _marketsByNative.TryGetValue(nativeSymbol, out var market)
				? market
				: null;
	}

	private void RegisterMarkets(IEnumerable<BigOneSymbol> markets)
	{
		using (_sync.EnterScope())
		{
			_marketsBySecurity.Clear();
			_marketsByNative.Clear();
			foreach (var market in markets ?? [])
			{
				if (market?.Pair.IsEmpty() != false ||
					market.Base.IsEmpty() || market.Quote.IsEmpty())
					continue;
				_marketsBySecurity[market.SecurityCode] = market;
				_marketsByNative[market.Pair] = market;
				_marketsByNative[market.SecurityCode] = market;
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
		=> $"BigONE_{Key.ToId()}";

	private void TrackOrder(string orderId, TrackedOrder order)
	{
		if (orderId.IsEmpty() || order is null)
			return;
		using (_sync.EnterScope())
		{
			_trackedOrders[orderId] = order;
			_knownActiveOrderIds.Add(orderId);
		}
	}

	private TrackedOrder GetTrackedOrder(string orderId)
	{
		if (orderId.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(orderId, out var order)
				? order
				: null;
	}

	private bool AddTrade(string symbol, string tradeId, bool isPrivate)
	{
		if (symbol.IsEmpty() || tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			var key = symbol + ":" + tradeId;
			var ids = isPrivate
				? _seenPrivateTradeIds
				: _seenPublicTradeIds;
			var queue = isPrivate
				? _seenPrivateTradeOrder
				: _seenPublicTradeOrder;
			if (!ids.Add(key))
				return false;
			queue.Enqueue(key);
			while (queue.Count > _maximumRememberedTradeIds)
				ids.Remove(queue.Dequeue());
			return true;
		}
	}

	private static string CreatePublicTradeId(BigOneTrade trade)
		=> !trade.Id.IsEmpty()
			? trade.Id
			: string.Join("-",
				trade.Timestamp.ToString(CultureInfo.InvariantCulture),
				trade.Price.ToWire(),
				trade.Amount.ToWire());

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_marketsBySecurity.Clear();
			_marketsByNative.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_trackedOrders.Clear();
			_knownActiveOrderIds.Clear();
			_seenPublicTradeIds.Clear();
			_seenPublicTradeOrder.Clear();
			_seenPrivateTradeIds.Clear();
			_seenPrivateTradeOrder.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPrivatePoll = default;
	}

	private static string ResolveOrderId(long? numericOrderId,
		string stringOrderId)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId;
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(
				CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			"BigONE operation requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_privatePollSync.Dispose();
		base.DisposeManaged();
	}
}
