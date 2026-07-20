namespace StockSharp.QFEX;

using Native;

public partial class QFEXMessageAdapter
{
	private class MarketSubscription
	{
		public long TransactionId { get; init; }
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public QFEXCandleIntervals Interval { get; init; }
		public TimeSpan TimeFrame { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, QFEXReferenceDataSymbol> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<QFEXMarketStreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, long> _transactionByClientOrderId =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private QFEXRestClient _restClient;
	private QFEXMarketDataWebSocketClient _marketSocket;
	private QFEXTradeWebSocketClient _tradeSocket;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _serverTime;

	/// <summary>Initializes a new instance.</summary>
	public QFEXMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(QFEXExtensions.TimeFrames);
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
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.QFEX];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.QFEX) ||
			securityId.IsAssociated(BoardCodes.QFEX);

	private QFEXRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private QFEXMarketDataWebSocketClient MarketSocket => _marketSocket ??
		throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private QFEXTradeWebSocketClient TradeSocket => _tradeSocket ??
		throw new InvalidOperationException(
			"QFEX private access is not configured or connected.");

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private void UpdateServerTime(DateTime time)
	{
		time = time.EnsureUtc();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private void EnsureConnected()
	{
		if (_restClient is null || _marketSocket is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (_tradeSocket?.IsAuthenticated != true || _portfolioName.IsEmpty())
			throw new InvalidOperationException(
				"QFEX API credentials are required for private operations.");
	}

	private QFEXReferenceDataSymbol GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not QFEX.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown QFEX symbol '{symbol}'.");
	}

	private bool TryGetMarket(string symbol,
		out QFEXReferenceDataSymbol market)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol ?? string.Empty, out market);
	}

	private QFEXReferenceDataSymbol[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private long GetTransactionId(string clientOrderId)
	{
		if (clientOrderId.IsEmpty())
			return 0;
		using (_sync.EnterScope())
			if (_transactionByClientOrderId.TryGetValue(clientOrderId,
				out var transactionId))
				return transactionId;
		const string prefix = "SS-";
		return clientOrderId.StartsWith(prefix, StringComparison.Ordinal) &&
			long.TryParse(clientOrderId.AsSpan(prefix.Length), NumberStyles.None,
				CultureInfo.InvariantCulture, out var parsed)
				? parsed
				: 0;
	}

	private static string CreateClientOrderId(long transactionId,
		string userOrderId)
	{
		var value = userOrderId.IsEmpty()
			? "SS-" + transactionId.ToString(CultureInfo.InvariantCulture)
			: userOrderId.Trim();
		if (value.Length is < 1 or > 64 || value.Any(char.IsControl))
			throw new ArgumentException(
				"QFEX client order ID must contain 1 to 64 printable characters.",
				nameof(userOrderId));
		return value;
	}

	private bool TryAcceptAccountTrade(string tradeId)
	{
		if (tradeId.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			var added = _seenAccountTrades.Add(tradeId);
			if (_seenAccountTrades.Count > 16384)
				foreach (var old in _seenAccountTrades.Take(
					_seenAccountTrades.Count - 8192).ToArray())
					_seenAccountTrades.Remove(old);
			return added;
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
			_transactionByClientOrderId.Clear();
			_seenAccountTrades.Clear();
			_serverTime = default;
		}
		_portfolioName = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_tradeSocket?.Dispose();
		_marketSocket?.Dispose();
		_restClient?.Dispose();
		base.DisposeManaged();
	}
}
