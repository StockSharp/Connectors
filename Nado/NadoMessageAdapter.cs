namespace StockSharp.Nado;

using Native;

public partial class NadoMessageAdapter
{
	private class MarketSubscription
	{
		public long TransactionId { get; init; }
		public int ProductId { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public int Granularity { get; init; }
	}

	private sealed class PriceState
	{
		public decimal? Bid { get; set; }
		public decimal? Ask { get; set; }
		public decimal? Last { get; set; }
		public decimal? Oracle { get; set; }
		public decimal? Index { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, NadoMarket> _markets =
		new(StringComparer.Ordinal);
	private readonly Dictionary<int, NadoMarket> _marketsByProduct = [];
	private readonly Dictionary<int, PriceState> _prices = [];
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<NadoSubscriptionKey, int> _streamReferences = [];
	private readonly Dictionary<string, long> _transactionByDigest =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, NadoOrder> _ordersByDigest =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderTypes> _orderTypesByDigest =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenFills =
		new(StringComparer.Ordinal);
	private readonly HashSet<int> _knownPositions = [];
	private NadoRestClient _restClient;
	private NadoWebSocketClient _socket;
	private NadoSigner _signer;
	private NadoContracts _contracts;
	private string _walletAddress;
	private string _subaccount;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _serverTime;

	/// <summary>Initializes a new instance.</summary>
	public NadoMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(NadoExtensions.TimeFrames);
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
	public override string[] AssociatedBoards => [BoardCodes.Nado];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Nado) ||
			securityId.IsAssociated(BoardCodes.Nado);

	private NadoRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private NadoWebSocketClient Socket => _socket ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private NadoSigner Signer => _signer ?? throw new
		InvalidOperationException("A Nado private key is required for trading.");

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
		time = time.EnsureNadoUtc();
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
		if (_subaccount.IsEmpty())
			throw new InvalidOperationException(
				"A Nado wallet address is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		_ = Signer;
		if (_contracts is null)
			throw new InvalidOperationException(
				"Nado signing domain is unavailable.");
	}

	private NadoMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode + "' is not Nado.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					"Unknown or incorrectly cased Nado symbol '" + symbol + "'.");
	}

	private NadoMarket GetMarket(int productId)
	{
		using (_sync.EnterScope())
			return _marketsByProduct.TryGetValue(productId, out var market)
				? market
				: null;
	}

	private NadoMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private PriceState GetPriceState(int productId)
	{
		using (_sync.EnterScope())
			return _prices.TryGetValue(productId, out var state) ? state : null;
	}

	private void TrackOrder(string digest, long transactionId, NadoOrder order,
		OrderTypes? orderType = null)
	{
		if (digest.IsEmpty())
			return;
		using (_sync.EnterScope())
		{
			if (transactionId != 0)
				_transactionByDigest[digest] = transactionId;
			if (order is not null)
				_ordersByDigest[digest] = order;
			if (orderType is not null)
				_orderTypesByDigest[digest] = orderType.Value;
		}
	}

	private OrderTypes GetOrderType(string digest)
	{
		if (digest.IsEmpty())
			return OrderTypes.Limit;
		using (_sync.EnterScope())
			return _orderTypesByDigest.TryGetValue(digest, out var orderType)
				? orderType
				: OrderTypes.Limit;
	}

	private long GetTransactionId(string digest)
	{
		if (digest.IsEmpty())
			return 0;
		using (_sync.EnterScope())
			return _transactionByDigest.TryGetValue(digest, out var id) ? id : 0;
	}

	private NadoOrder GetTrackedOrder(string digest)
	{
		if (digest.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _ordersByDigest.TryGetValue(digest, out var order) ? order : null;
	}

	private bool TryAcceptFill(string submissionIndex, string digest)
	{
		var key = submissionIndex + ":" + digest;
		using (_sync.EnterScope())
		{
			var added = _seenFills.Add(key);
			if (_seenFills.Count > 16384)
				_seenFills.Clear();
			return added;
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByProduct.Clear();
			_prices.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_transactionByDigest.Clear();
			_ordersByDigest.Clear();
			_orderTypesByDigest.Clear();
			_seenFills.Clear();
			_knownPositions.Clear();
			_serverTime = default;
		}
		_contracts = null;
		_walletAddress = null;
		_subaccount = null;
		_portfolioName = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
	}
}
