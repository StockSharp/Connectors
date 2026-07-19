namespace StockSharp.CoinsPh;

public partial class CoinsPhMessageAdapter
{
	private enum StreamTypes
	{
		Trades,
		Ticker,
		Depth,
		Klines,
	}

	private sealed class MarketDefinition
	{
		public string Symbol { get; init; }
		public string BaseAsset { get; init; }
		public string QuoteAsset { get; init; }
		public decimal PriceStep { get; init; }
		public decimal QuantityStep { get; init; }
		public decimal MinimumQuantity { get; init; }
		public decimal MaximumQuantity { get; init; }
		public decimal MinimumPrice { get; init; }
		public decimal MaximumPrice { get; init; }
		public decimal MinimumNotional { get; init; }
		public decimal MaximumNotional { get; init; }
		public bool IsTrading { get; init; }
		public HashSet<CoinsPhOrderTypes> OrderTypes { get; init; }
	}

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
		public string ClientOrderId { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public CoinsPhOrderCondition Condition { get; init; }
	}

	private readonly record struct StreamKey(StreamTypes Type, string Symbol,
		TimeSpan TimeFrame);

	private readonly record struct OrderIdentity(long? NumericId, string ClientId)
	{
		public string Key => NumericId?.ToString(CultureInfo.InvariantCulture) ??
			ClientId;
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, MarketDefinition> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _activeOrderIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPublicTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private CoinsPhRestClient _restClient;
	private CoinsPhPublicSocketClient _publicSocketClient;
	private CoinsPhPrivateSocketClient _privateSocketClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="CoinsPhMessageAdapter"/> class.
	/// </summary>
	public CoinsPhMessageAdapter(IdGenerator transactionIdGenerator)
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
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.CoinsPh];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinsPh) ||
			securityId.IsAssociated(BoardCodes.CoinsPh);

	private CoinsPhRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private CoinsPhPublicSocketClient PublicSocketClient
		=> _publicSocketClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _publicSocketClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable || _privateSocketClient is null)
			throw new InvalidOperationException(
				"Coins.ph API key and secret are required for private operations.");
	}

	private MarketDefinition GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinsPh) &&
			!securityId.IsAssociated(BoardCodes.CoinsPh))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Coins.ph.");
		return GetMarket(securityId.SecurityCode);
	}

	private MarketDefinition GetMarket(string symbol)
	{
		symbol = symbol.NormalizeSymbol();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var definition)
				? definition
				: throw new InvalidOperationException(
					$"Unknown Coins.ph symbol '{symbol}'.");
	}

	private void RegisterMarkets(IEnumerable<CoinsPhSymbol> markets)
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets ?? [])
			{
				if (market?.Symbol.IsEmpty() != false || market.BaseAsset.IsEmpty() ||
					market.QuoteAsset.IsEmpty())
					continue;
				var price = market.Filters?.FirstOrDefault(static filter =>
					filter.Type == CoinsPhFilterTypes.Price);
				var lot = market.Filters?.FirstOrDefault(static filter =>
					filter.Type == CoinsPhFilterTypes.LotSize);
				var notional = market.Filters?.FirstOrDefault(static filter =>
					filter.Type is CoinsPhFilterTypes.Notional or
						CoinsPhFilterTypes.MinimumNotional);
				var definition = new MarketDefinition
				{
					Symbol = market.Symbol.NormalizeSymbol(),
					BaseAsset = market.BaseAsset.ToUpperInvariant(),
					QuoteAsset = market.QuoteAsset.ToUpperInvariant(),
					PriceStep = price?.TickSize ?? GetStep(
						market.QuoteAssetPrecision),
					QuantityStep = lot?.StepSize ?? GetStep(
						market.BaseAssetPrecision),
					MinimumQuantity = lot?.MinimumQuantity ?? 0m,
					MaximumQuantity = lot?.MaximumQuantity ?? 0m,
					MinimumPrice = price?.MinimumPrice ?? 0m,
					MaximumPrice = price?.MaximumPrice ?? 0m,
					MinimumNotional = notional?.MinimumNotional ?? 0m,
					MaximumNotional = notional?.MaximumNotional ?? 0m,
					IsTrading = market.Status == CoinsPhSymbolStatuses.Trading,
					OrderTypes = [.. market.OrderTypes ?? []],
				};
				_markets[definition.Symbol] = definition;
			}
		}
	}

	private static decimal GetStep(int precision)
	{
		if (precision <= 0)
			return 1m;
		var value = 1m;
		for (var i = 0; i < precision.Min(28); i++)
			value /= 10m;
		return value;
	}

	private static bool AddReference(IDictionary<StreamKey, int> references,
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

	private static bool ReleaseReference(IDictionary<StreamKey, int> references,
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
		=> $"CoinsPh_{Key.ToId()}";

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

	private bool AddPublicTrade(string symbol, long tradeId)
	{
		if (tradeId <= 0)
			return false;
		using (_sync.EnterScope())
		{
			if (_seenPublicTrades.Count > 100000)
				_seenPublicTrades.Clear();
			return _seenPublicTrades.Add(
				$"{symbol}:{tradeId.ToString(CultureInfo.InvariantCulture)}");
		}
	}

	private bool AddAccountTrade(string tradeId)
	{
		if (tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			if (_seenAccountTrades.Count > 100000)
				_seenAccountTrades.Clear();
			return _seenAccountTrades.Add(tradeId);
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_trackedOrders.Clear();
			_activeOrderIds.Clear();
			_seenPublicTrades.Clear();
			_seenAccountTrades.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
		}
	}

	private static OrderIdentity ResolveOrderId(long? numericOrderId,
		string stringOrderId, string operation)
	{
		if (numericOrderId is > 0)
			return new(numericOrderId, null);
		if (!stringOrderId.IsEmpty())
		{
			if (long.TryParse(stringOrderId, NumberStyles.None,
				CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
				return new(parsed, null);
			return new(null, stringOrderId);
		}
		throw new InvalidOperationException(
			$"Coins.ph {operation} requires an exchange order ID.");
	}

	private static CoinsPhOrderCondition CreateCondition(CoinsPhOrderTypes type,
		decimal stopPrice, decimal quoteAmount = 0m)
	{
		if (type.ToStockSharp() != OrderTypes.Conditional && quoteAmount <= 0)
			return null;
		return new()
		{
			Type = type is CoinsPhOrderTypes.TakeProfit or
				CoinsPhOrderTypes.TakeProfitLimit
					? CoinsPhConditionalOrderTypes.TakeProfit
					: CoinsPhConditionalOrderTypes.StopLoss,
			StopPrice = stopPrice > 0 ? stopPrice : null,
			QuoteAmount = quoteAmount > 0 ? quoteAmount : null,
		};
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
