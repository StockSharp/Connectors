namespace StockSharp.Pacifica;

using Native;

public partial class PacificaMessageAdapter
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
		public PacificaCandleIntervals Interval { get; init; }
		public TimeSpan TimeFrame { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, PacificaMarket> _markets =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, PacificaPrice> _prices =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<PacificaSubscriptionKey, int>
		_streamReferences = [];
	private readonly Dictionary<string, long> _transactionByClientOrderId =
		new(StringComparer.Ordinal);
	private readonly HashSet<long> _seenAccountTradeIds = [];
	private readonly HashSet<string> _knownPositionSymbols =
		new(StringComparer.Ordinal);
	private PacificaRestClient _restClient;
	private PacificaWebSocketClient _socket;
	private PacificaSigner _signer;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _serverTime;

	/// <summary>Initializes a new instance.</summary>
	public PacificaMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(PacificaExtensions.TimeFrames);
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
	public override string[] AssociatedBoards => [BoardCodes.Pacifica];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Pacifica) ||
			securityId.IsAssociated(BoardCodes.Pacifica);

	private PacificaRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private PacificaWebSocketClient Socket => _socket ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private PacificaSigner Signer => _signer ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

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
		if (_restClient is null || _socket is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (!Signer.IsAccountAvailable)
			throw new InvalidOperationException(
				"A Pacifica wallet address is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (!Signer.IsSigningAvailable)
			throw new InvalidOperationException(
				"A Pacifica private key is required for trading.");
	}

	private PacificaMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode +
				"' is not Pacifica.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					"Unknown or incorrectly cased Pacifica symbol '" +
					symbol + "'.");
	}

	private bool TryGetMarket(string symbol, out PacificaMarket market)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol ?? string.Empty, out market);
	}

	private PacificaMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private PacificaPrice GetPrice(string symbol)
	{
		using (_sync.EnterScope())
			return _prices.TryGetValue(symbol ?? string.Empty, out var price)
				? price
				: null;
	}

	private long GetTransactionId(string clientOrderId)
	{
		if (clientOrderId.IsEmpty())
			return 0;
		using (_sync.EnterScope())
			return _transactionByClientOrderId.TryGetValue(clientOrderId,
				out var transactionId)
				? transactionId
				: 0;
	}

	private bool TryAcceptAccountTrade(long tradeId)
	{
		if (tradeId <= 0)
			return false;
		using (_sync.EnterScope())
		{
			var added = _seenAccountTradeIds.Add(tradeId);
			if (_seenAccountTradeIds.Count > 16384)
				foreach (var old in _seenAccountTradeIds.OrderBy(static id => id)
					.Take(_seenAccountTradeIds.Count - 8192).ToArray())
					_seenAccountTradeIds.Remove(old);
			return added;
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_prices.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_transactionByClientOrderId.Clear();
			_seenAccountTradeIds.Clear();
			_knownPositionSymbols.Clear();
			_serverTime = default;
		}
		_portfolioName = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_socket?.Dispose();
		_restClient?.Dispose();
		_signer?.Dispose();
		base.DisposeManaged();
	}
}
