namespace StockSharp.Bit2Me;

public partial class Bit2MeMessageAdapter
{
	private class MarketSubscription
	{
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string Symbol { get; init; }
		public string ClientOrderId { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public decimal? TriggerPrice { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public bool IsPostOnly { get; init; }
	}

	private readonly record struct StreamKey(string Channel, string Symbol);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, Bit2MeMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _knownActiveOrderIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenTradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPublicTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _privatePollSync = new(1, 1);
	private Bit2MeRestClient _restClient;
	private Bit2MeWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPrivatePoll;

	/// <summary>
	/// Initializes a new instance of the <see cref="Bit2MeMessageAdapter"/>.
	/// </summary>
	public Bit2MeMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.Bit2Me];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bit2Me) ||
			securityId.IsAssociated(BoardCodes.Bit2Me);

	private Bit2MeRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private Bit2MeWsClient WsClient
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
				"Bit2Me API key and secret are required for private operations.");
	}

	private Bit2MeMarket GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bit2Me) &&
			!securityId.IsAssociated(BoardCodes.Bit2Me))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Bit2Me.");

		var requested = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		string normalized = null;
		try
		{
			normalized = requested.NormalizeSymbol();
		}
		catch (FormatException)
		{
		}
		using (_sync.EnterScope())
		{
			if (!normalized.IsEmpty() &&
				_markets.TryGetValue(normalized, out var market))
				return market;
			var compact = requested.Replace("/", string.Empty)
				.Replace("-", string.Empty).Replace("_", string.Empty);
			var candidates = _markets.Values.Where(value =>
				value.Symbol.Replace("/", string.Empty)
					.EqualsIgnoreCase(compact)).Take(2).ToArray();
			if (candidates.Length == 1)
				return candidates[0];
		}
		throw new InvalidOperationException(
			$"Unknown Bit2Me market '{requested}'.");
	}

	private void RegisterMarkets(IEnumerable<Bit2MeMarket> markets)
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets ?? [])
			{
				if (market?.Symbol.IsEmpty() != false)
					continue;
				var symbol = market.Symbol.NormalizeSymbol();
				market.Symbol = symbol;
				_markets[symbol] = market;
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
		=> $"Bit2Me_{Key.ToId()}";

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

	private bool AddTrade(string tradeId)
	{
		if (tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
			return _seenTradeIds.Add(tradeId);
	}

	private bool AddPublicTrade(string symbol, Bit2MeWsTrade trade)
	{
		if (trade is null)
			return false;
		var key = $"{symbol}:{trade.Timestamp}:{trade.Side}:{trade.Price}:" +
			trade.Amount.ToString(CultureInfo.InvariantCulture);
		using (_sync.EnterScope())
			return _seenPublicTrades.Add(key);
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_streamReferences.Clear();
			_trackedOrders.Clear();
			_knownActiveOrderIds.Clear();
			_seenTradeIds.Clear();
			_seenPublicTrades.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPrivatePoll = default;
	}

	private static string ResolveOrderId(long? numericOrderId,
		string stringOrderId, string operation)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId;
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			$"Bit2Me {operation} requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_privatePollSync.Dispose();
		base.DisposeManaged();
	}
}
