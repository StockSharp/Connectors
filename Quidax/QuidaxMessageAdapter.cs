namespace StockSharp.Quidax;

public partial class QuidaxMessageAdapter
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
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, QuidaxMarket>
		_marketsBySecurity =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, QuidaxMarket>
		_marketsByNative =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<string, TrackedOrder>
		_trackedOrders = new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenPublicTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenPublicTradeOrder = [];
	private readonly HashSet<string> _seenPrivateTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenPrivateTradeOrder = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private QuidaxRestClient _restClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPoll;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="QuidaxMessageAdapter"/>.
	/// </summary>
	public QuidaxMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.Quidax];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Quidax) ||
			securityId.IsAssociated(BoardCodes.Quidax);

	private QuidaxRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Quidax secret key is required for private operations.");
	}

	private QuidaxMarket GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Quidax) &&
			!securityId.IsAssociated(BoardCodes.Quidax))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Quidax.");

		var requested = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
		{
			if (_marketsByNative.TryGetValue(requested, out var market))
				return market;
			var normalized = requested.Contains('/') ||
				requested.Contains('_') ||
				requested.Contains('-')
					? requested.ToQuidaxSymbol()
					: requested.ToLowerInvariant();
			if (_marketsByNative.TryGetValue(normalized, out market))
				return market;
			if (_marketsBySecurity.TryGetValue(
				requested, out market))
				return market;
		}
		throw new InvalidOperationException(
			$"Unknown Quidax market '{requested}'.");
	}

	private QuidaxMarket GetMarket(string nativeSymbol)
	{
		if (nativeSymbol.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _marketsByNative.TryGetValue(
				nativeSymbol.Trim(), out var market)
					? market
					: null;
	}

	private QuidaxMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _marketsBySecurity.Values];
	}

	private void RegisterMarkets(IEnumerable<QuidaxMarket> markets)
	{
		using (_sync.EnterScope())
		{
			_marketsBySecurity.Clear();
			_marketsByNative.Clear();

			foreach (var market in markets ?? [])
			{
				if (market?.Id.IsEmpty() != false ||
					market.BaseUnit.IsEmpty() ||
					market.QuoteUnit.IsEmpty())
					continue;
				market.Id = market.Id.Trim().ToLowerInvariant();
				_marketsBySecurity[market.SecurityCode] = market;
				_marketsByNative[market.Id] = market;
			}
		}
	}

	private string GetPortfolioName()
		=> $"Quidax_{Token.ToId()}";

	private void TrackOrder(string orderId, TrackedOrder order)
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
			_trackedOrders.Clear();
			_seenPublicTradeIds.Clear();
			_seenPublicTradeOrder.Clear();
			_seenPrivateTradeIds.Clear();
			_seenPrivateTradeOrder.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPoll = default;
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
			"Quidax operation requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClient();
		_pollSync.Dispose();
		base.DisposeManaged();
	}
}
