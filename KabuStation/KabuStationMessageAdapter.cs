namespace StockSharp.KabuStation;

public partial class KabuStationMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public KabuStationSecurityInfo Security { get; init; }
		public DataType DataType { get; init; }
		public int MaxDepth { get; init; }
		public DateTime? LastTickTime { get; set; }
		public decimal? LastTickPrice { get; set; }
		public decimal? LastCumulativeVolume { get; set; }
	}

	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public KabuStationSecurityInfo Security { get; init; }
		public string PortfolioName { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Price { get; init; }
		public decimal Volume { get; init; }
	}

	private KabuStationRestClient _rest;
	private KabuStationWebSocketClient _socket;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<string, KabuStationSecurityInfo> _securityInfos = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, KabuStationSecurityInfo> _registeredSecurities = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioName;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Initializes a new instance of the <see cref="KabuStationMessageAdapter"/> class.</summary>
	public KabuStationMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(5);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = Enumerable.Range(1, 10).ToArray();

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.Tse, "NAGOYA", "FUKUOKA", "SAPPORO", "OSE", "OSE-DAY", "OSE-NIGHT", "KABU-SOR", "TSEPLUS"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_rest != null || _socket != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var attempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);
		_rest = new(ApiPassword, IsDemo, attempts) { Parent = this };
		try
		{
			await _rest.Connect(cancellationToken);
			_socket = new(IsDemo, attempts, ReConnectionSettings.WorkingTime) { Parent = this };
			_socket.BoardReceived += OnBoardReceived;
			_socket.Connected += OnSocketConnected;
			_socket.Error += SendOutErrorAsync;
			_socket.StateChanged += SendOutConnectionStateAsync;
			await _socket.ConnectAsync(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DisposeClients();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClients();
		_marketSubscriptions.Clear();
		_securityInfos.Clear();
		_registeredSecurities.Clear();
		_orders.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioName = null;
		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_socket != null)
			await _socket.SendHeartbeat(cancellationToken);

		var now = DateTime.UtcNow;
		if (_orderStatusSubscriptionId != 0 && now - _lastOrderRefresh >= TimeSpan.FromSeconds(3))
			await SendOrderSnapshot(_orderStatusSubscriptionId, _lastOrderRefresh == default ? null : _lastOrderRefresh.AddSeconds(-1), cancellationToken);
		if (_portfolioSubscriptionId != 0 && now - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
			await SendPortfolioSnapshot(_portfolioSubscriptionId, cancellationToken);

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask OnSocketConnected(bool reconnect, CancellationToken cancellationToken)
	{
		if (!reconnect || _rest == null)
			return;
		foreach (var security in _registeredSecurities.Values.ToArray())
			await _rest.Register(security, cancellationToken);
	}

	private void DisposeClients()
	{
		if (_socket != null)
		{
			_socket.BoardReceived -= OnBoardReceived;
			_socket.Connected -= OnSocketConnected;
			_socket.Error -= SendOutErrorAsync;
			_socket.StateChanged -= SendOutConnectionStateAsync;
			_socket.Disconnect();
			_socket.Dispose();
			_socket = null;
		}
		_rest?.Dispose();
		_rest = null;
	}

	private KabuStationSecurityInfo ResolveSecurity(SecurityId securityId, SecurityTypes? securityType = null)
	{
		if (KabuStationSecurityInfo.TryParse(securityId.Native, out var native))
			return native;
		var key = GetSecurityKey(securityId);
		if (_securityInfos.TryGetValue(key, out var cached))
			return cached;
		var security = securityId.ToKabuSecurity(securityType);
		CacheSecurity(security);
		return security;
	}

	private void CacheSecurity(KabuStationSecurityInfo security)
	{
		_securityInfos[GetSecurityKey(security.Symbol, security.BoardCode)] = security;
		_securityInfos[GetSecurityKey(security.Symbol, null)] = security;
	}

	private static string GetSecurityKey(SecurityId securityId)
		=> GetSecurityKey(securityId.SecurityCode, securityId.BoardCode);

	private static string GetSecurityKey(string symbol, string boardCode)
		=> $"{symbol?.Trim()}@{boardCode?.Trim()}";

	private static string GetNativeKey(KabuStationSecurityInfo security)
		=> $"{security.Symbol}@{security.Exchange}";
}
