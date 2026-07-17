namespace StockSharp.FubonNeo;

public partial class FubonNeoMessageAdapter
{
	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string PortfolioName { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public TimeInForce TimeInForce { get; init; }
		public FubonNeoOrderCondition Condition { get; init; }
	}

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromMinutes(60),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	private readonly SynchronizedDictionary<string, FubonNeoSecurityInfo> _securities = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, LiveCandleState> _liveCandleStates = new(StringComparer.OrdinalIgnoreCase);
	private FubonNeoSdkClient _client;
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioFilter;
	private DateTime _lastHeartbeat;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Supported candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Initializes a new instance of the <see cref="FubonNeoMessageAdapter"/>.</summary>
	public FubonNeoMessageAdapter(IdGenerator transactionIdGenerator)
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
		=> dataType == DataType.Securities || dataType.IsTFCandles || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["TWSE", "TPEX", "TAIFEX", "TAIFEX-AH"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var client = new FubonNeoSdkClient(SdkPath, PersonalId, Password, ApiKey, IsApiKeyLogin,
			CertificatePath, CertificatePassword, EnvironmentUrl, RealtimeMode, ReconnectAttempts)
		{
			Parent = this,
		};
		client.MarketDataReceived += OnMarketData;
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
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		await DisposeClientAsync();
		_securities.Clear();
		_orders.Clear();
		_transactionOrders.Clear();
		_tradeIds.Clear();
		_liveCandleStates.Clear();
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
		if (_client != null && now - _lastHeartbeat >= TimeSpan.FromSeconds(20))
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
		client.MarketDataReceived -= OnMarketData;
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

	private void CacheSecurity(FubonNeoSecurityInfo security)
	{
		if (security?.Symbol.IsEmpty() != false)
			return;
		_securities[security.ToNativeKey()] = security;
		_securities[GetSecurityKey(security.Kind, security.Symbol)] = security;
	}

	private FubonNeoSecurityInfo ResolveSecurity(string symbol, bool isFutures, int? assetType = null)
	{
		var kind = isFutures ? FubonNeoAssetKinds.FuturesOptions : FubonNeoAssetKinds.Stock;
		if (_securities.TryGetValue(GetSecurityKey(kind, symbol), out var security))
			return security;
		security = new()
		{
			Kind = kind,
			TickerType = assetType switch
			{
				2 => "OPTION",
				1 => "FUTURE",
				_ => isFutures ? "FUTURE" : "EQUITY",
			},
			Exchange = isFutures ? "TAIFEX" : "TWSE",
			Session = "REGULAR",
			Symbol = symbol,
		};
		CacheSecurity(security);
		return security;
	}

	private static string GetSecurityKey(FubonNeoAssetKinds kind, string symbol)
		=> $"{kind}|{symbol?.ToUpperInvariant()}";
}
