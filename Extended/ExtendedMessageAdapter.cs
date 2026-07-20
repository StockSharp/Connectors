namespace StockSharp.Extended;

using Native;

public partial class ExtendedMessageAdapter
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
		public TimeSpan TimeFrame { get; init; }
		public string Interval { get; init; }
	}

	private sealed class PriceState
	{
		public decimal? MarkPrice { get; set; }
		public decimal? IndexPrice { get; set; }
		public decimal? LastPrice { get; set; }
		public decimal? BestBidPrice { get; set; }
		public decimal? BestAskPrice { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, ExtendedMarket> _markets =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, PriceState> _prices =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, decimal> _takerFees =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<ExtendedSubscriptionKey, int> _streamReferences = [];
	private readonly Dictionary<string, long> _transactionByExternalId =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, long> _transactionByOrderId = [];
	private readonly Dictionary<long, string> _externalByOrderId = [];
	private readonly HashSet<long> _seenAccountTradeIds = [];
	private readonly HashSet<string> _knownPositionSymbols =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _knownSpotBalanceSymbols =
		new(StringComparer.Ordinal);
	private ExtendedRestClient _restClient;
	private ExtendedWebSocketClient _socket;
	private ExtendedSigner _signer;
	private ExtendedAccount _account;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _serverTime;

	/// <summary>Initializes a new instance.</summary>
	public ExtendedMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(ExtendedExtensions.TimeFrames);
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
	public override string[] AssociatedBoards => [BoardCodes.Extended];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Extended) ||
			securityId.IsAssociated(BoardCodes.Extended);

	private ExtendedRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private ExtendedWebSocketClient Socket => _socket ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private ExtendedSigner Signer => _signer ?? throw new
		InvalidOperationException("Extended Stark signing is not configured.");

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
		time = time.EnsureExtendedUtc();
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
		if (_account is null)
			throw new InvalidOperationException(
				"An Extended API key is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (_signer is null)
			throw new InvalidOperationException(
				"An Extended Stark private key is required for trading.");
	}

	private ExtendedMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode + "' is not Extended.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					"Unknown or incorrectly cased Extended symbol '" + symbol + "'.");
	}

	private ExtendedMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private bool TryGetMarket(string symbol, out ExtendedMarket market)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol ?? string.Empty, out market);
	}

	private PriceState GetPriceState(string symbol)
	{
		using (_sync.EnterScope())
			return _prices.TryGetValue(symbol ?? string.Empty, out var state)
				? state
				: null;
	}

	private long GetTransactionId(string externalId)
	{
		if (externalId.IsEmpty())
			return 0;
		using (_sync.EnterScope())
			return _transactionByExternalId.TryGetValue(externalId,
				out var transactionId) ? transactionId : 0;
	}

	private long GetTransactionId(long orderId)
	{
		if (orderId <= 0)
			return 0;
		using (_sync.EnterScope())
			return _transactionByOrderId.TryGetValue(orderId,
				out var transactionId) ? transactionId : 0;
	}

	private string GetExternalId(long orderId)
	{
		if (orderId <= 0)
			return null;
		using (_sync.EnterScope())
			return _externalByOrderId.TryGetValue(orderId, out var externalId)
				? externalId
				: null;
	}

	private void TrackOrder(long orderId, string externalId, long transactionId)
	{
		using (_sync.EnterScope())
		{
			if (!externalId.IsEmpty() && transactionId != 0)
				_transactionByExternalId[externalId] = transactionId;
			if (orderId > 0)
			{
				if (transactionId != 0)
					_transactionByOrderId[orderId] = transactionId;
				if (!externalId.IsEmpty())
					_externalByOrderId[orderId] = externalId;
			}
		}
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
			_takerFees.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_transactionByExternalId.Clear();
			_transactionByOrderId.Clear();
			_externalByOrderId.Clear();
			_seenAccountTradeIds.Clear();
			_knownPositionSymbols.Clear();
			_knownSpotBalanceSymbols.Clear();
			_serverTime = default;
		}
		_account = null;
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
