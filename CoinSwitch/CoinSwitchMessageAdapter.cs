namespace StockSharp.CoinSwitch;

public partial class CoinSwitchMessageAdapter
{
	private const string _tickerEvent =
		"FETCH_TICKER_INFO_CS_PRO";
	private const string _depthEvent =
		"FETCH_ORDER_BOOK_CS_PRO";
	private const string _tradesEvent =
		"FETCH_TRADES_CS_PRO";
	private const string _candlesEvent =
		"FETCH_CANDLESTICK_CS_PRO";
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

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public string Pair { get; init; }
		public bool UsesWebSocket { get; init; }
	}

	private readonly record struct StreamKey(
		string EventName,
		string Pair);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, CoinSwitchMarket>
		_marketsBySecurity =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, CoinSwitchMarket>
		_marketsByNative =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int>
		_streamReferences = [];
	private readonly Dictionary<string, long> _orderTransactions =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _orderSymbols =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenPublicTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenPublicTradeOrder = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private CoinSwitchRestClient _restClient;
	private CoinSwitchWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPoll;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="CoinSwitchMessageAdapter"/>.
	/// </summary>
	public CoinSwitchMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards
		=> [BoardCodes.CoinSwitch];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.CoinSwitch) ||
			securityId.IsAssociated(BoardCodes.CoinSwitch);

	private CoinSwitchRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private CoinSwitchWsClient WsClient
		=> _wsClient ?? throw new InvalidOperationException(
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
				"CoinSwitch API key and Ed25519 secret are required.");
	}

	private CoinSwitchMarket GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.CoinSwitch) &&
			!securityId.IsAssociated(BoardCodes.CoinSwitch))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not " +
					"CoinSwitch.");

		var requested = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
		{
			if (_marketsBySecurity.TryGetValue(
				requested, out var market) ||
				_marketsByNative.TryGetValue(requested, out market))
				return market;

			string normalized;
			try
			{
				normalized = requested.ToCoinSwitchNativeSymbol(
					ProductType);
			}
			catch (FormatException)
			{
				normalized = requested.ToUpperInvariant();
			}
			if (_marketsByNative.TryGetValue(normalized, out market))
				return market;
		}
		throw new InvalidOperationException(
			$"Unknown CoinSwitch market '{requested}'.");
	}

	private CoinSwitchMarket GetMarket(string nativeSymbol)
	{
		if (nativeSymbol.IsEmpty())
			return null;
		nativeSymbol = nativeSymbol.Trim()
			.Replace(',', ProductType == CoinSwitchProductTypes.Spot
				? '/'
				: ',')
			.ToUpperInvariant();
		using (_sync.EnterScope())
			return _marketsByNative.TryGetValue(
				nativeSymbol, out var market)
					? market
					: null;
	}

	private CoinSwitchMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _marketsBySecurity.Values];
	}

	private void RegisterMarkets(
		IEnumerable<CoinSwitchMarket> markets)
	{
		using (_sync.EnterScope())
		{
			_marketsBySecurity.Clear();
			_marketsByNative.Clear();
			foreach (var market in markets ?? [])
			{
				if (market?.NativeSymbol.IsEmpty() != false ||
					market.SecurityCode.IsEmpty())
					continue;
				_marketsBySecurity[market.SecurityCode] = market;
				_marketsByNative[market.NativeSymbol] = market;
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

	private bool AddPublicTrade(
		string symbol,
		string tradeId,
		long timestamp,
		decimal price,
		decimal volume)
	{
		if (symbol.IsEmpty())
			return false;
		var key = !tradeId.IsEmpty()
			? symbol + ":" + tradeId
			: string.Join(
				":",
				symbol,
				timestamp.ToString(CultureInfo.InvariantCulture),
				price.ToWire(),
				volume.ToWire());
		using (_sync.EnterScope())
		{
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

	private string GetSocketPair(
		CoinSwitchMarket market,
		TimeSpan? timeFrame = null)
	{
		var pair = market.SecurityCode.ToCoinSwitchSocketSymbol(
			ProductType);
		if (ProductType == CoinSwitchProductTypes.Futures &&
			timeFrame is not null)
			pair += "_" +
				timeFrame.Value.ToCoinSwitchInterval()
					.ToString(CultureInfo.InvariantCulture);
		return pair;
	}

	private bool CanStreamCandle(TimeSpan timeFrame)
		=> ProductType switch
		{
			CoinSwitchProductTypes.Spot =>
				timeFrame == TimeSpan.FromMinutes(1),
			CoinSwitchProductTypes.Futures => true,
			_ => false,
		};

	private string GetPortfolioName()
		=> $"CoinSwitch_{ProductType}_{Key.ToId()}";

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
			"CoinSwitch operation requires an exchange order ID.");
	}

	private void TrackOrder(
		string orderId,
		string nativeSymbol,
		long transactionId)
	{
		if (orderId.IsEmpty())
			return;
		using (_sync.EnterScope())
		{
			_orderTransactions[orderId] = transactionId;
			if (!nativeSymbol.IsEmpty())
				_orderSymbols[orderId] = nativeSymbol;
		}
	}

	private long GetOrderTransaction(string orderId)
	{
		using (_sync.EnterScope())
			return _orderTransactions.TryGetValue(
				orderId, out var transactionId)
					? transactionId
					: 0;
	}

	private string GetOrderSymbol(string orderId)
	{
		using (_sync.EnterScope())
			return _orderSymbols.TryGetValue(
				orderId, out var symbol)
					? symbol
					: null;
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
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_orderTransactions.Clear();
			_orderSymbols.Clear();
			_seenPublicTradeIds.Clear();
			_seenPublicTradeOrder.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPoll = default;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_pollSync.Dispose();
		base.DisposeManaged();
	}
}
