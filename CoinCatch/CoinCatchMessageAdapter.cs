namespace StockSharp.CoinCatch;

public partial class CoinCatchMessageAdapter
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
		public string Channel { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public string Interval { get; init; }
	}

	private readonly record struct StreamKey(
		string Channel,
		string NativeSymbol);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, CoinCatchSymbol> _marketsBySecurity =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, CoinCatchSymbol> _marketsByNative =
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
	private readonly Dictionary<string, long> _orderTransactions =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _orderSymbols =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenPublicTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenPublicTradeOrder = [];
	private readonly HashSet<string> _seenPrivateTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenPrivateTradeOrder = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private CoinCatchRestClient _restClient;
	private CoinCatchWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPrivatePoll;
	private DateTime _lastWebSocketHeartbeat;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="CoinCatchMessageAdapter"/>.
	/// </summary>
	public CoinCatchMessageAdapter(IdGenerator transactionIdGenerator)
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
		=> true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards =>
	[
		BoardCodes.CoinCatch,
		BoardCodes.CoinCatchFutUsdt,
		BoardCodes.CoinCatchFutCoin,
	];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(
				ProductType.ToBoardCode()) ||
			securityId.IsAssociated(ProductType.ToBoardCode());

	private CoinCatchRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private CoinCatchWsClient WsClient
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
				"CoinCatch API key, secret and passphrase are required " +
					"for private operations.");
	}

	private CoinCatchSymbol GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(
				ProductType.ToBoardCode()) &&
			!securityId.IsAssociated(ProductType.ToBoardCode()))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not CoinCatch.");

		var requested = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim().ToUpperInvariant();
		using (_sync.EnterScope())
		{
			if (_marketsByNative.TryGetValue(requested, out var market) ||
				_marketsBySecurity.TryGetValue(
					requested.Replace('-', '/').Replace('_', '/'),
					out market))
				return market;
			var compact = requested.Replace("/", string.Empty)
				.Replace("-", string.Empty).Replace("_", string.Empty);
			if (_marketsByNative.TryGetValue(compact, out market))
				return market;
		}
		throw new InvalidOperationException(
			$"Unknown CoinCatch market '{requested}'.");
	}

	private CoinCatchSymbol GetMarket(string nativeSymbol)
	{
		if (nativeSymbol.IsEmpty())
			return null;
		nativeSymbol = nativeSymbol.Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _marketsByNative.TryGetValue(nativeSymbol, out var market)
				? market
				: null;
	}

	private void RegisterMarkets(IEnumerable<CoinCatchSymbol> markets)
	{
		using (_sync.EnterScope())
		{
			_marketsBySecurity.Clear();
			_marketsByNative.Clear();
			foreach (var market in markets ?? [])
			{
				if (market?.Symbol.IsEmpty() != false ||
					market.BaseCoin.IsEmpty() ||
					market.QuoteCoin.IsEmpty())
					continue;
				market.Symbol = market.Symbol.Trim().ToUpperInvariant();
				_marketsBySecurity[market.SecurityCode] = market;
				_marketsByNative[market.Symbol] = market;
				_marketsByNative[
					market.Symbol.ToCoinCatchWebSocketSymbol()] = market;
				if (!market.SymbolName.IsEmpty())
					_marketsByNative[
						market.SymbolName.Trim().ToUpperInvariant()] =
						market;
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

	private string GetPortfolioName()
		=> $"CoinCatch_{ProductType}_{Key.ToId()}";

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
			_orderTransactions.Clear();
			_orderSymbols.Clear();
			_seenPublicTradeIds.Clear();
			_seenPublicTradeOrder.Clear();
			_seenPrivateTradeIds.Clear();
			_seenPrivateTradeOrder.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPrivatePoll = default;
		_lastWebSocketHeartbeat = default;
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
			"CoinCatch operation requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_pollSync.Dispose();
		base.DisposeManaged();
	}
}
