namespace StockSharp.DeltaExchangeIndia;

public partial class DeltaExchangeIndiaMessageAdapter
{
	private class MarketSubscription
	{
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }

		public int ProductId { get; init; }

		public string Symbol { get; init; }

		public Sides Side { get; init; }

		public OrderTypes OrderType { get; init; }

		public decimal Volume { get; init; }

		public decimal Price { get; init; }
	}

	private const int _maximumSeenTrades = 10000;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, DeltaProduct> _products =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<int, DeltaProduct> _productsById = [];
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
	private readonly Dictionary<long, TrackedOrder> _trackedOrders = [];
	private readonly HashSet<string> _seenTrades =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenTradeOrder = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private DeltaExchangeIndiaRestClient _restClient;
	private DeltaExchangeIndiaWsClient _publicWsClient;
	private DeltaExchangeIndiaWsClient _privateWsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPrivatePoll;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="DeltaExchangeIndiaMessageAdapter"/>.
	/// </summary>
	public DeltaExchangeIndiaMessageAdapter(
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
	public override bool IsAllDownloadingSupported(DataType dataType)
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
	public override string[] AssociatedBoards
		=> [BoardCodes.DeltaExchangeIndia];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.DeltaExchangeIndia) ||
			securityId.IsAssociated(BoardCodes.DeltaExchangeIndia);

	private DeltaExchangeIndiaRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private DeltaExchangeIndiaWsClient PublicWsClient
		=> _publicWsClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private DeltaExchangeIndiaWsClient PrivateWsClient
		=> _privateWsClient ?? throw new InvalidOperationException(
			"Delta Exchange India private WebSocket is unavailable.");

	private void EnsureConnected()
	{
		if (_restClient is null || _publicWsClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable ||
			_privateWsClient is null)
			throw new InvalidOperationException(
				"Delta Exchange India API key and secret are " +
					"required for private operations.");
	}

	private DeltaProduct GetProduct(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.DeltaExchangeIndia) &&
			!securityId.IsAssociated(BoardCodes.DeltaExchangeIndia))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not " +
					"Delta Exchange India.");
		return GetProduct(
			securityId.SecurityCode.ThrowIfEmpty(
				nameof(securityId.SecurityCode))) ??
			throw new InvalidOperationException(
				$"Unknown Delta Exchange India product " +
					$"'{securityId.SecurityCode}'.");
	}

	private DeltaProduct GetProduct(string symbol)
	{
		if (symbol.IsEmpty())
			return null;
		symbol = symbol.Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _products.TryGetValue(symbol, out var product)
				? product
				: null;
	}

	private DeltaProduct GetProduct(int id)
	{
		using (_sync.EnterScope())
			return _productsById.TryGetValue(id, out var product)
				? product
				: null;
	}

	private DeltaProduct[] GetProducts()
	{
		using (_sync.EnterScope())
			return [.. _products.Values];
	}

	private void RegisterProducts(
		IEnumerable<DeltaProduct> products)
	{
		using (_sync.EnterScope())
		{
			_products.Clear();
			_productsById.Clear();

			foreach (var product in products ?? [])
			{
				if (product?.Symbol.IsEmpty() != false ||
					product.Id <= 0)
					continue;
				_products[product.Symbol] = product;
				_productsById[product.Id] = product;
			}
		}
	}

	private bool AddReference(string channel, string symbol)
	{
		var key = channel + ":" + symbol;
		using (_sync.EnterScope())
		{
			if (_streamReferences.TryGetValue(key, out var count))
			{
				_streamReferences[key] = count + 1;
				return false;
			}
			_streamReferences[key] = 1;
			return true;
		}
	}

	private bool ReleaseReference(string channel, string symbol)
	{
		var key = channel + ":" + symbol;
		using (_sync.EnterScope())
		{
			if (!_streamReferences.TryGetValue(key, out var count))
				return false;
			if (count > 1)
			{
				_streamReferences[key] = count - 1;
				return false;
			}
			_streamReferences.Remove(key);
			return true;
		}
	}

	private bool AddTrade(string scope, string id)
	{
		if (scope.IsEmpty() || id.IsEmpty())
			return false;
		var key = scope + ":" + id;
		using (_sync.EnterScope())
		{
			if (!_seenTrades.Add(key))
				return false;
			_seenTradeOrder.Enqueue(key);

			while (_seenTradeOrder.Count > _maximumSeenTrades)
				_seenTrades.Remove(_seenTradeOrder.Dequeue());

			return true;
		}
	}

	private void TrackOrder(long id, TrackedOrder order)
	{
		if (id <= 0 || order is null)
			return;
		using (_sync.EnterScope())
			_trackedOrders[id] = order;
	}

	private TrackedOrder GetTrackedOrder(long id)
	{
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(id, out var order)
				? order
				: null;
	}

	private string GetPortfolioName()
		=> $"DELTAINDIA_{Key.ToId()}";

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_products.Clear();
			_productsById.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_trackedOrders.Clear();
			_seenTrades.Clear();
			_seenTradeOrder.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPrivatePoll = default;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_pollSync.Dispose();
		base.DisposeManaged();
	}
}
