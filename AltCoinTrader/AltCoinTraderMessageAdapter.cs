namespace StockSharp.AltCoinTrader;

public partial class AltCoinTraderMessageAdapter
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

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string SecurityCode { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public TimeInForce TimeInForce { get; init; }
	}

	private readonly record struct StreamKey(
		string Channel,
		string NativeSymbol,
		int Depth);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, AltCoinTraderMarket>
		_marketsBySecurity =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, AltCoinTraderMarket>
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
	private readonly Dictionary<string, TrackedOrder>
		_trackedOrders = new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenPublicTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenPublicTradeOrder = [];
	private readonly HashSet<string> _seenPrivateTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenPrivateTradeOrder = [];
	private readonly SemaphoreSlim _privatePollSync = new(1, 1);
	private AltCoinTraderRestClient _restClient;
	private AltCoinTraderWsClient _publicWsClient;
	private AltCoinTraderWsClient _privateWsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPrivatePoll;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="AltCoinTraderMessageAdapter"/>.
	/// </summary>
	public AltCoinTraderMessageAdapter(
		IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards
		=> [BoardCodes.AltCoinTrader];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.AltCoinTrader) ||
			securityId.IsAssociated(BoardCodes.AltCoinTrader);

	private AltCoinTraderRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private AltCoinTraderWsClient PublicWsClient
		=> _publicWsClient ??
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _publicWsClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"AltCoinTrader API key and secret are required " +
					"for private operations.");
	}

	private AltCoinTraderMarket GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.AltCoinTrader) &&
			!securityId.IsAssociated(BoardCodes.AltCoinTrader))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' " +
					"is not AltCoinTrader.");

		var requested = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
		{
			if (_marketsByNative.TryGetValue(
				requested, out var market))
				return market;
			var normalized =
				requested.ToAltCoinTraderSecurityCode();
			if (_marketsBySecurity.TryGetValue(
				normalized, out market))
				return market;
		}
		throw new InvalidOperationException(
			$"Unknown AltCoinTrader market '{requested}'.");
	}

	private AltCoinTraderMarket GetMarket(string nativeSymbol)
	{
		if (nativeSymbol.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _marketsByNative.TryGetValue(
				nativeSymbol, out var market)
					? market
					: null;
	}

	private void RegisterMarkets(
		IEnumerable<AltCoinTraderMarket> markets)
	{
		using (_sync.EnterScope())
		{
			_marketsBySecurity.Clear();
			_marketsByNative.Clear();

			foreach (var market in markets ?? [])
			{
				if (market?.Symbol.IsEmpty() != false ||
					market.Base.IsEmpty() ||
					market.Quote.IsEmpty())
					continue;
				market.Symbol = market.Symbol
					.Trim().ToUpperInvariant();
				_marketsBySecurity[market.SecurityCode] = market;
				_marketsByNative[market.Symbol] = market;
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
		=> $"AltCoinTrader_{Key.ToId()}";

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

	private bool AddTrade(
		string market,
		string tradeId,
		bool isPrivate)
	{
		if (market.IsEmpty() || tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			var key = market + ":" + tradeId;
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
			_seenPublicTradeIds.Clear();
			_seenPublicTradeOrder.Clear();
			_seenPrivateTradeIds.Clear();
			_seenPrivateTradeOrder.Clear();
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
			"AltCoinTrader operation requires " +
				"an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_privatePollSync.Dispose();
		base.DisposeManaged();
	}
}
