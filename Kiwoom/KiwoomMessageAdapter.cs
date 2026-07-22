namespace StockSharp.Kiwoom;

public partial class KiwoomMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public KiwoomSecurityInfo Security { get; init; }
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
		public KiwoomSecurityInfo Security { get; init; }
		public string OrderNumber { get; set; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Price { get; init; }
		public decimal Volume { get; init; }
		public KiwoomOrderCondition Condition { get; init; }
	}

	private KiwoomRestClient _rest;
	private KiwoomWebSocketClient _domesticStream;
	private KiwoomWebSocketClient _usStream;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<string, KiwoomSecurityInfo> _securityInfos = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _orderFills = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastAccountRefresh;

	/// <summary>Initializes a new instance of the <see cref="KiwoomMessageAdapter"/> class.</summary>
	public KiwoomMessageAdapter(IdGenerator transactionIdGenerator)
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
		this.AddSupportedCandleTimeFrames(KiwoomExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [1, 5, 10];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["KRX", "NXT", "SOR", "NASDAQ", "NYSE", "AMEX"];

	private string PortfolioName => nameof(Kiwoom);

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_rest != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		var appKey = Key?.UnSecure().ThrowIfEmpty(nameof(Key));
		var appSecret = Secret?.UnSecure().ThrowIfEmpty(nameof(Secret));
		var attempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);
		_rest = new(appKey, appSecret, IsDemo, attempts) { Parent = this };

		try
		{
			await _rest.Connect(cancellationToken);
			_domesticStream = CreateStream(KiwoomAssetClasses.DomesticStock, attempts);
			await _domesticStream.Connect(cancellationToken);
			if (!IsDemo)
			{
				_usStream = CreateStream(KiwoomAssetClasses.UsStock, attempts);
				await _usStream.Connect(cancellationToken);
			}

			if (this.IsTransactional())
			{
				await _domesticStream.SubscribePrivate("00", cancellationToken);
				await _domesticStream.SubscribePrivate("04", cancellationToken);
				if (_usStream != null)
				{
					await _usStream.SubscribePrivate("F4", cancellationToken);
					await _usStream.SubscribePrivate("F5", cancellationToken);
				}
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
		if (_usStream != null)
			await _usStream.Disconnect();
		if (_domesticStream != null)
			await _domesticStream.Disconnect();
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

	private KiwoomWebSocketClient CreateStream(KiwoomAssetClasses assetClass, int attempts)
	{
		var stream = new KiwoomWebSocketClient(_rest.GetAccessToken, assetClass, IsDemo, attempts) { Parent = this };
		stream.EventReceived += OnRealtimeEvent;
		stream.Error += SendOutErrorAsync;
		stream.StateChanged += SendOutConnectionStateAsync;
		return stream;
	}

	private void DisposeClients()
	{
		DisposeStream(ref _usStream);
		DisposeStream(ref _domesticStream);
		_rest?.Dispose();
		_rest = null;
	}

	private void DisposeStream(ref KiwoomWebSocketClient stream)
	{
		if (stream == null)
			return;
		stream.EventReceived -= OnRealtimeEvent;
		stream.Error -= SendOutErrorAsync;
		stream.StateChanged -= SendOutConnectionStateAsync;
		stream.Dispose();
		stream = null;
	}

	private ValueTask OnRealtimeEvent(KiwoomRealtimeMessage message, CancellationToken cancellationToken)
		=> message.Data.Type switch
		{
			"0B" or "FE" => ProcessRealtimeTrade(message, cancellationToken),
			"0D" or "FT" => ProcessRealtimeDepth(message, cancellationToken),
			"00" or "F4" or "F5" => ProcessOrderNotice(message, cancellationToken),
			"04" => ProcessBalanceNotice(message, cancellationToken),
			_ => default,
		};

	private KiwoomSecurityInfo ResolveSecurity(SecurityId securityId, KiwoomMarkets? market = null)
	{
		var key = GetSecurityKey(securityId);
		if (market is null && _securityInfos.TryGetValue(key, out var security))
			return security;
		security = securityId.ToKiwoom(market);
		_securityInfos[key] = security;
		return security;
	}

	private KiwoomSecurityInfo ResolveRealtimeSecurity(KiwoomRealtimeMessage message)
	{
		var code = message.Data.Item.IsEmpty(message.Data.Values?.SecurityCode);
		if (!code.IsEmpty())
		{
			code = code.TrimStart('A');
			var match = _securityInfos.Values.FirstOrDefault(item => item.Code.EqualsIgnoreCase(code) && item.AssetClass == message.AssetClass);
			if (match != null)
				return match;
		}
		if (message.AssetClass == KiwoomAssetClasses.DomesticStock)
			return KiwoomSecurityInfo.Create(code.ThrowIfEmpty(nameof(code)), KiwoomMarkets.Krx);
		var exchange = message.Data.Values?.ExchangeCode;
		var market = exchange?.ToUpperInvariant() switch
		{
			"NY" or "NYSE" => KiwoomMarkets.Nyse,
			"NA" or "AMEX" => KiwoomMarkets.Amex,
			_ => KiwoomMarkets.Nasdaq,
		};
		return KiwoomSecurityInfo.Create(code.ThrowIfEmpty(nameof(code)), market);
	}

	private static string GetSecurityKey(SecurityId securityId)
		=> $"{securityId.BoardCode}:{securityId.SecurityCode}";

	private static bool IsSame(KiwoomSecurityInfo left, KiwoomSecurityInfo right)
		=> left.Market == right.Market && left.Code.EqualsIgnoreCase(right.Code);
}
