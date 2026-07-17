namespace StockSharp.Daishin;

public partial class DaishinMessageAdapter
{
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
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	private readonly SynchronizedDictionary<string, DaishinSecurityInfo> _securities = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, DaishinTrackedOrder> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, LiveCandleState> _liveCandles = [];
	private readonly SemaphoreSlim _orderSync = new(1, 1);
	private DaishinComClient _client;
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioFilter;
	private DateTime _lastConnectionCheck;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;
	private long _tickSequence;

	/// <summary>Supported candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Initializes a new instance of the <see cref="DaishinMessageAdapter"/>.</summary>
	public DaishinMessageAdapter(IdGenerator transactionIdGenerator)
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
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5, 10];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["KRX", "NXT"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var client = new DaishinComClient(Account, Market, IsTradingEnabled)
		{
			Parent = this,
		};
		client.Level1Received += OnLevel1;
		client.BookReceived += OnBook;
		client.OrderReceived += OnOrder;
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
		_lastConnectionCheck = default;
		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
		_tickSequence = 0;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var now = CurrentTime;
		if (_client != null && now - _lastConnectionCheck >= TimeSpan.FromSeconds(15))
		{
			if (!await _client.IsConnectedAsync(cancellationToken))
				await SendOutErrorAsync(new InvalidOperationException(
					"Daishin CYBOS Plus connection is not active."), cancellationToken);
			_lastConnectionCheck = now;
		}
		if (_orderStatusSubscriptionId != 0 &&
			now - _lastOrderRefresh >= TimeSpan.FromSeconds(15))
			await SendOrderSnapshot(_orderStatusSubscriptionId, null, cancellationToken);
		if (_portfolioSubscriptionId != 0 &&
			now - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
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
		client.BookReceived -= OnBook;
		client.OrderReceived -= OnOrder;
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

	private void CacheSecurity(DaishinSecurityInfo security)
	{
		if (security?.Code.IsEmpty() != false)
			return;
		_securities[GetSecurityKey(security.SecurityType, security.Code)] = security;
	}

	private async Task<DaishinSecurityInfo> ResolveSecurityAsync(SecurityId securityId,
		SecurityTypes? securityType, CancellationToken cancellationToken)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (securityType is SecurityTypes type &&
			_securities.TryGetValue(GetSecurityKey(type, code), out var cached))
			return cached;

		var types = securityType is SecurityTypes requested ? new HashSet<SecurityTypes> { requested } : [];
		var matches = await _client.GetSecuritiesAsync(code, types, cancellationToken);
		foreach (var match in matches)
			CacheSecurity(match);
		return matches.FirstOrDefault(item => securityType == null || item.SecurityType == securityType)
			?? throw new InvalidOperationException($"Daishin CYBOS Plus security '{code}' was not found.");
	}

	private static string GetSecurityKey(SecurityTypes securityType, string code)
		=> $"{securityType}|{code?.ToUpperInvariant()}";

	private static DateTime NormalizeUtc(DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
}
