namespace StockSharp.Yuanta;

public partial class YuantaMessageAdapter
{
	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string PortfolioName { get; init; }
		public SecurityTypes SecurityType { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public TimeInForce TimeInForce { get; init; }
		public YuantaOrderCondition Condition { get; init; }
	}

	private sealed class LiveCandleState
	{
		public DateTime OpenTime { get; set; }
		public decimal Open { get; set; }
		public decimal High { get; set; }
		public decimal Low { get; set; }
		public decimal Close { get; set; }
		public decimal Volume { get; set; }
	}

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromMinutes(60),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	private readonly SynchronizedDictionary<string, YuantaSecurityInfo> _securities = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, LiveCandleState> _liveCandles = [];
	private YuantaSdkClient _client;
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioFilter;
	private DateTime _lastHeartbeat;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Supported candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Initializes a new instance of the <see cref="YuantaMessageAdapter"/>.</summary>
	public YuantaMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
	[
		"TWSE", "TPEX", "TWEMERGING", "TWSEODD", "TPEXODD", "TAIFEX",
		"SGX", "CME", "CBOT", "TOCOM", "JPX", "HKFE", "ICEUS", "ICEUK",
		"EUREX", "ASX", "CBOE",
	];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var client = new YuantaSdkClient(SdkPath, Account, Password, CertificatePath,
			CertificatePassword, Environment, LogPath, ReconnectAttempts)
		{
			Parent = this,
		};
		client.Level1Received += OnLevel1;
		client.TradeReceived += OnTrade;
		client.BookReceived += OnBook;
		client.OrderReceived += OnOrder;
		client.FillReceived += OnFill;
		client.Error += SendOutErrorAsync;
		client.ConnectionLost += OnConnectionLost;
		_client = client;
		try
		{
			await client.ConnectAsync(cancellationToken);
			connectMsg.SessionId = client.Version;
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClientAsync();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClientAsync();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientAsync();
		_securities.Clear();
		_orders.Clear();
		_transactionOrders.Clear();
		_tradeIds.Clear();
		_liveCandles.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioFilter = null;
		_lastHeartbeat = default;
		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		var now = CurrentTime;
		if (_client != null && now - _lastHeartbeat >= TimeSpan.FromSeconds(30))
		{
			await _client.SendHeartbeatAsync(cancellationToken);
			_lastHeartbeat = now;
		}
		if (_orderStatusSubscriptionId != 0 && now - _lastOrderRefresh >= TimeSpan.FromSeconds(15))
			await SendOrderSnapshot(_orderStatusSubscriptionId, cancellationToken);
		if (_portfolioSubscriptionId != 0 && now - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
			await SendPortfolioSnapshot(_portfolioSubscriptionId, _portfolioFilter, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private ValueTask OnConnectionLost(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async Task DisposeClientAsync()
	{
		var client = _client;
		if (client == null)
			return;
		_client = null;
		client.Level1Received -= OnLevel1;
		client.TradeReceived -= OnTrade;
		client.BookReceived -= OnBook;
		client.OrderReceived -= OnOrder;
		client.FillReceived -= OnFill;
		client.Error -= SendOutErrorAsync;
		client.ConnectionLost -= OnConnectionLost;
		try
		{
			await client.DisconnectAsync();
		}
		finally
		{
			client.Dispose();
		}
	}

	private void CacheSecurity(YuantaSecurityInfo security)
	{
		if (security?.Symbol.IsEmpty() != false)
			return;
		_securities[GetSecurityKey(security.Market, security.Symbol)] = security;
	}

	private YuantaSecurityInfo ResolveSecurity(int market, string symbol, SecurityTypes? securityType = null)
	{
		if (_securities.TryGetValue(GetSecurityKey(market, symbol), out var security))
			return security;
		security = new()
		{
			Market = market,
			Symbol = symbol,
			SecurityType = securityType ?? market.ToSecurityType(symbol),
		};
		CacheSecurity(security);
		return security;
	}

	private static string GetSecurityKey(int market, string symbol)
		=> $"{market}|{symbol?.ToUpperInvariant()}";

	private static DateTime NormalizeUtc(DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
}
