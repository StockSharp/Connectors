namespace StockSharp.KoreaInvestment;

public partial class KoreaInvestmentMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public KisSecurityInfo Security { get; init; }
		public DataType DataType { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public int MaxDepth { get; init; }
		public ActiveCandle Candle { get; set; }
	}

	private sealed class ActiveCandle
	{
		public DateTime OpenTime { get; init; }
		public decimal Open { get; init; }
		public decimal High { get; set; }
		public decimal Low { get; set; }
		public decimal Close { get; set; }
		public decimal Volume { get; set; }
	}

	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public KisSecurityInfo Security { get; init; }
		public string OrderNumber { get; set; }
		public string OrganizationNumber { get; set; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Price { get; init; }
		public decimal Volume { get; init; }
		public KoreaInvestmentOrderCondition Condition { get; init; }
	}

	private KoreaInvestmentRestClient _rest;
	private KoreaInvestmentWebSocketClient _stream;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<string, KisSecurityInfo> _securityInfos = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _orderFills = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastAccountRefresh;

	/// <summary>Initializes a new instance of the <see cref="KoreaInvestmentMessageAdapter"/> class.</summary>
	public KoreaInvestmentMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(KoreaInvestmentExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [1, 5, 10];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
	[
		"KRX", "NXT", "SOR", "KRX-FUT", "NASDAQ", "NYSE", "AMEX", "HKEX", "SSE", "SZSE", "TSE", "HNX", "HOSE",
	];

	private string PortfolioName => $"KIS-{AccountNumber}-{ProductCode}";

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_rest != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		var appKey = AppKey?.UnSecure().ThrowIfEmpty(nameof(AppKey));
		var appSecret = AppSecret?.UnSecure().ThrowIfEmpty(nameof(AppSecret));
		if (this.IsTransactional())
		{
			AccountNumber.ThrowIfEmpty(nameof(AccountNumber));
			ProductCode.ThrowIfEmpty(nameof(ProductCode));
		}

		var attempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);
		_rest = new(appKey, appSecret, AccountNumber, ProductCode, IsDemo, attempts) { Parent = this };
		try
		{
			await _rest.Connect(cancellationToken);
			_stream = new(_rest.ApprovalKey, IsDemo, attempts) { Parent = this };
			_stream.EventReceived += OnRealtimeEvent;
			_stream.Error += SendOutErrorAsync;
			_stream.StateChanged += SendOutConnectionStateAsync;
			await _stream.Connect(cancellationToken);

			if (this.IsTransactional() && !HtsId.IsEmpty())
			{
				await _stream.Subscribe(KisRealtimeChannels.DomesticOrderNotice, HtsId,
					KisSecurityInfo.Create("_", KoreaInvestmentMarkets.Krx, SecurityTypes.Stock), cancellationToken);
				await _stream.Subscribe(KisRealtimeChannels.DerivativeOrderNotice, HtsId,
					KisSecurityInfo.Create("_", KoreaInvestmentMarkets.KrxDerivatives, SecurityTypes.Future), cancellationToken);
				await _stream.Subscribe(KisRealtimeChannels.OverseasOrderNotice, HtsId,
					KisSecurityInfo.Create("_", KoreaInvestmentMarkets.Nasdaq, SecurityTypes.Stock), cancellationToken);
			}

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
		await _stream.Disconnect();
		DisposeClients();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (this.IsTransactional() && CurrentTime - _lastAccountRefresh >= TimeSpan.FromSeconds(30))
		{
			if (_portfolioSubscriptionId != 0)
				await SendPortfolioSnapshot(_portfolioSubscriptionId, cancellationToken);
			if (_orderStatusSubscriptionId != 0)
				await SendOrderSnapshot(_orderStatusSubscriptionId, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, cancellationToken);
			_lastAccountRefresh = CurrentTime;
		}
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClients();
		_marketSubscriptions.Clear();
		_securityInfos.Clear();
		_orders.Clear();
		_orderFills.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastAccountRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void DisposeClients()
	{
		if (_stream != null)
		{
			_stream.EventReceived -= OnRealtimeEvent;
			_stream.Error -= SendOutErrorAsync;
			_stream.StateChanged -= SendOutConnectionStateAsync;
			_stream.Dispose();
			_stream = null;
		}
		_rest?.Dispose();
		_rest = null;
	}

	private ValueTask OnRealtimeEvent(KisRealtimeEvent message, CancellationToken cancellationToken)
		=> message switch
		{
			KisRealtimeTrade trade => ProcessRealtimeTrade(trade, cancellationToken),
			KisRealtimeDepth depth => ProcessRealtimeDepth(depth, cancellationToken),
			KisRealtimeOrderNotice notice => ProcessOrderNotice(notice, cancellationToken),
			_ => default,
		};

	private KisSecurityInfo ResolveSecurity(SecurityId securityId, SecurityTypes? type = null,
		KoreaInvestmentMarkets? market = null)
	{
		var key = GetSecurityKey(securityId);
		if (market is null && _securityInfos.TryGetValue(key, out var security))
			return security;
		security = securityId.ToKis(type, market);
		_securityInfos[key] = security;
		return security;
	}

	private static string GetSecurityKey(SecurityId securityId)
		=> $"{securityId.BoardCode}:{securityId.SecurityCode}";

	private static bool IsSame(KisSecurityInfo left, KisSecurityInfo right)
		=> left.Market == right.Market && left.Code.EqualsIgnoreCase(right.Code);
}
