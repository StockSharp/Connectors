namespace StockSharp.Bitkub;

public partial class BitkubMessageAdapter
{
	private sealed class MarketDefinition
	{
		public string Symbol { get; init; }
		public string BaseAsset { get; init; }
		public string QuoteAsset { get; init; }
		public int PairingId { get; init; }
		public decimal PriceStep { get; init; }
		public decimal VolumeStep { get; init; }
		public decimal MinimumQuoteSize { get; init; }
		public bool IsTrading { get; init; }
		public bool IsBuyFrozen { get; init; }
		public bool IsSellFrozen { get; init; }
		public bool IsCancelFrozen { get; init; }
	}

	private class MarketSubscription
	{
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class OrderSubscription
	{
		public string Symbol { get; init; }
		public string OrderId { get; init; }
		public Sides? Side { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string Symbol { get; init; }
		public string ClientId { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public bool IsPostOnly { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, MarketDefinition> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<string, int> _streamReferences =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _activeOrderIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPublicTradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPrivateTradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _balanceRefreshSync = new(1, 1);
	private BitkubRestClient _restClient;
	private BitkubPublicWebSocketClient _publicWebSocketClient;
	private BitkubPrivateWebSocketClient _privateWebSocketClient;
	private DateTime _lastBalanceRefresh;

	/// <summary>
	/// Initializes a new instance of the <see cref="BitkubMessageAdapter"/> class.
	/// </summary>
	public BitkubMessageAdapter(IdGenerator transactionIdGenerator)
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
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Bitkub];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bitkub) ||
			securityId.IsAssociated(BoardCodes.Bitkub);

	private BitkubRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private BitkubPublicWebSocketClient PublicWebSocketClient
		=> _publicWebSocketClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _publicWebSocketClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable || _privateWebSocketClient is null)
			throw new InvalidOperationException(
				"Bitkub API key and secret are required for private operations.");
	}

	private MarketDefinition GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bitkub) &&
			!securityId.IsAssociated(BoardCodes.Bitkub))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Bitkub.");
		return GetMarket(securityId.SecurityCode);
	}

	private MarketDefinition GetMarket(string symbol)
	{
		symbol = symbol.NormalizeSymbol();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Bitkub symbol '{symbol}'.");
	}

	private void RegisterMarkets(IEnumerable<BitkubSymbol> symbols)
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var item in symbols ?? [])
			{
				if (item?.Symbol.IsEmpty() != false || item.PairingId <= 0 ||
					!item.MarketSegment.IsEmpty() &&
					!item.MarketSegment.EqualsIgnoreCase("SPOT"))
					continue;
				var symbol = item.Symbol.NormalizeSymbol();
				var (baseAsset, quoteAsset) = symbol.SplitSymbol();
				_markets[symbol] = new()
				{
					Symbol = symbol,
					BaseAsset = item.BaseAsset.IsEmpty(baseAsset).ToUpperInvariant(),
					QuoteAsset = item.QuoteAsset.IsEmpty(quoteAsset).ToUpperInvariant(),
					PairingId = item.PairingId,
					PriceStep = item.PriceStep > 0
						? item.PriceStep
						: BitkubExtensions.GetStep(item.PriceScale),
					VolumeStep = BitkubExtensions.GetStep(item.BaseAssetScale),
					MinimumQuoteSize = item.MinimumQuoteSize,
					IsTrading = item.Status.EqualsIgnoreCase("active"),
					IsBuyFrozen = item.IsBuyFrozen,
					IsSellFrozen = item.IsSellFrozen,
					IsCancelFrozen = item.IsCancelFrozen,
				};
			}
		}
	}

	private bool AddStreamReference(string symbol)
	{
		if (_streamReferences.TryGetValue(symbol, out var count))
		{
			_streamReferences[symbol] = count + 1;
			return false;
		}
		_streamReferences.Add(symbol, 1);
		return true;
	}

	private bool ReleaseStreamReference(string symbol)
	{
		if (!_streamReferences.TryGetValue(symbol, out var count))
			return false;
		if (count > 1)
		{
			_streamReferences[symbol] = count - 1;
			return false;
		}
		_streamReferences.Remove(symbol);
		return true;
	}

	private string GetPortfolioName()
		=> $"Bitkub_{Key.ToId()}";

	private void TrackOrder(string orderId, TrackedOrder order)
	{
		if (orderId.IsEmpty() || order is null)
			return;
		using (_sync.EnterScope())
		{
			_trackedOrders[orderId] = order;
			_activeOrderIds.Add(orderId);
		}
	}

	private TrackedOrder GetTrackedOrder(string orderId)
	{
		if (orderId.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(orderId, out var order) ? order : null;
	}

	private bool AddPublicTrade(string symbol, string tradeId)
	{
		using (_sync.EnterScope())
			return _seenPublicTradeIds.Add($"{symbol}:{tradeId}");
	}

	private bool AddPrivateTrade(string tradeId)
	{
		if (tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
			return _seenPrivateTradeIds.Add(tradeId);
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
			_orderSubscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_trackedOrders.Clear();
			_activeOrderIds.Clear();
			_seenPublicTradeIds.Clear();
			_seenPrivateTradeIds.Clear();
		}
		_lastBalanceRefresh = default;
	}

	private static string ResolveOrderId(long? numericOrderId, string stringOrderId,
		string operation)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId;
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			$"Bitkub {operation} requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_balanceRefreshSync.Dispose();
		base.DisposeManaged();
	}
}
