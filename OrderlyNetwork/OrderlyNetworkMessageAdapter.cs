namespace StockSharp.OrderlyNetwork;

/// <summary>The message adapter for Orderly Network perpetual markets.</summary>
[MediaIcon(Media.MediaNames.orderly_network)]
[Doc("topics/api/connectors/crypto_exchanges/orderly_network.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OrderlyNetworkKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(OrderlyNetworkOrderCondition))]
public partial class OrderlyNetworkMessageAdapter : MessageAdapter
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
		public string Interval { get; init; }
	}

	private sealed class OrderStatusSubscription
	{
		public string[] Symbols { get; init; }
		public long? OrderId { get; init; }
		public string ClientOrderId { get; init; }
		public Sides? Side { get; init; }
		public decimal? Volume { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _depthSync = new(1, 1);
	private readonly Dictionary<string, OrderlyNetworkSymbolInfo> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderlyNetworkFuture> _futures =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<string, int> _publicStreamReferences =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, long> _depthTimestamps =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderStatusSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, long> _transactionIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenTradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _knownPositions =
		new(StringComparer.OrdinalIgnoreCase);
	private OrderlyNetworkSigner _signer;
	private OrderlyNetworkRestClient _restClient;
	private OrderlyNetworkSocketClient _publicSocket;
	private OrderlyNetworkSocketClient _privateSocket;
	private string _portfolioName;
	private DateTime _serverTime;

	/// <summary>Supported candle time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		OrderlyNetworkExtensions.TimeFrames;

	/// <summary>Initializes a new instance.</summary>
	public OrderlyNetworkMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
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
		=> true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.OrderlyNetwork];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.OrderlyNetwork) ||
			securityId.IsAssociated(BoardCodes.OrderlyNetwork);

	private OrderlyNetworkRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private OrderlyNetworkSocketClient PublicSocket => _publicSocket ?? throw new
		InvalidOperationException(
			"An Orderly account ID is required for realtime market data.");

	private OrderlyNetworkSocketClient PrivateSocket => _privateSocket ?? throw new
		InvalidOperationException(
			"An Orderly account ID and secret are required for private streams.");

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private void UpdateServerTime(long timestamp)
	{
		if (timestamp <= 0)
			return;
		UpdateServerTime(timestamp.FromOrderlyMilliseconds());
	}

	private void UpdateServerTime(DateTime time)
	{
		time = time.EnsureOrderlyUtc();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private void EnsureConnected()
	{
		if (_restClient is null || _signer is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureRealtimeReady()
	{
		EnsureConnected();
		_ = PublicSocket;
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (!_signer.IsSigningAvailable)
			throw new InvalidOperationException(
				"An Orderly account ID and ED25519 secret are required for private operations.");
	}

	private string GetSymbol(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Orderly Network.");
		var requested = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
		{
			if (_markets.TryGetValue(requested, out var direct))
				return direct.Symbol;
			var normalized = requested.Replace('-', '_').Replace('/', '_');
			if (_markets.TryGetValue(normalized, out var normalizedMarket))
				return normalizedMarket.Symbol;
		}
		throw new InvalidOperationException(
			$"Unknown Orderly Network market '{requested}'.");
	}

	private OrderlyNetworkSymbolInfo GetMarket(string symbol)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Orderly Network market '{symbol}'.");
	}

	private OrderlyNetworkSymbolInfo[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private OrderlyNetworkFuture GetFuture(string symbol)
	{
		using (_sync.EnterScope())
			return _futures.TryGetValue(symbol, out var future) ? future : null;
	}

	private string PortfolioName => _portfolioName ??=
		"Orderly_" + AccountId.ThrowIfEmpty(nameof(AccountId));

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.Equals(PortfolioName,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				$"Unknown Orderly Network portfolio '{portfolioName}'.");
	}

	private long GetTransactionId(string clientOrderId)
	{
		using (_sync.EnterScope())
			return _transactionIds.TryGetValue(clientOrderId ?? string.Empty,
				out var transactionId) ? transactionId : 0;
	}

	private bool TryAcceptTrade(string tradeId)
	{
		if (tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			var added = _seenTradeIds.Add(tradeId);
			if (_seenTradeIds.Count > 16384)
				foreach (var old in _seenTradeIds.Take(8192).ToArray())
					_seenTradeIds.Remove(old);
			return added;
		}
	}

	private static bool AddReference(IDictionary<string, int> references,
		string topic)
	{
		if (references.TryGetValue(topic, out var count))
		{
			references[topic] = checked(count + 1);
			return false;
		}
		references.Add(topic, 1);
		return true;
	}

	private static bool ReleaseReference(IDictionary<string, int> references,
		string topic)
	{
		if (!references.TryGetValue(topic, out var count))
			return false;
		if (count > 1)
		{
			references[topic] = count - 1;
			return false;
		}
		references.Remove(topic);
		return true;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_futures.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_publicStreamReferences.Clear();
			_depthTimestamps.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_transactionIds.Clear();
			_seenTradeIds.Clear();
			_knownPositions.Clear();
			_serverTime = default;
		}
		_portfolioName = null;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_privateSocket?.Dispose();
		_publicSocket?.Dispose();
		_restClient?.Dispose();
		_signer?.Dispose();
		_depthSync.Dispose();
		base.DisposeManaged();
	}
}
