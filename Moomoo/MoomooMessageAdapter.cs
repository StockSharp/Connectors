namespace StockSharp.Moomoo;

public partial class MoomooMessageAdapter
{
	private readonly record struct CandleSubscription(SecurityId SecurityId, TimeSpan TimeFrame);

	private MoomooClient _client;
	private TrdCommon.TrdAcc[] _accounts = [];
	private readonly SynchronizedDictionary<long, SecurityId> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<long, SecurityId> _depthSubscriptions = [];
	private readonly SynchronizedDictionary<long, SecurityId> _tickSubscriptions = [];
	private readonly SynchronizedDictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _processedFills = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Initializes a new instance of the adapter.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction identifier generator.</param>
	public MoomooMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <summary>
	/// Supported candle time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(3), TimeSpan.FromHours(4),
		TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30),
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles || dataType == DataType.Transactions || dataType == DataType.PositionChanges;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		var (host, port) = GetAddress(Address);
		_client = new()
		{
			BasicQuoteHandler = ProcessBasicQuote,
			OrderBookHandler = ProcessOrderBook,
			TickerHandler = ProcessTicker,
			CandleHandler = ProcessCandle,
			OrderHandler = ProcessOrderPush,
			FillHandler = ProcessFillPush,
			ErrorHandler = (error, token) => SendOutErrorAsync(error, token),
		};
		try
		{
			await _client.Connect(host, port, cancellationToken);
			await _client.UnlockTrade(Password, cancellationToken);
			var environment = IsDemo ? TrdCommon.TrdEnv.TrdEnv_Simulate : TrdCommon.TrdEnv.TrdEnv_Real;
			_accounts = (await _client.GetAccounts(cancellationToken))
				.Where(a => a.TrdEnv == (int)environment && a.TrdMarketAuthListList.Contains((int)TrdCommon.TrdMarket.TrdMarket_US))
				.ToArray();
			if (_accounts.Length > 0)
				await _client.SubscribeAccounts(_accounts.Select(a => a.AccID), cancellationToken);
			await base.ConnectAsync(message, cancellationToken);
		}
		catch
		{
			_client.Dispose();
			_client = null;
			ClearState();
			throw;
		}
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage message, CancellationToken cancellationToken)
	{
		_client?.Dispose();
		_client = null;
		ClearState();
		return base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message, CancellationToken cancellationToken)
	{
		_client?.Dispose();
		_client = null;
		ClearState();
		await base.ResetAsync(message, cancellationToken);
	}

	private void ClearState()
	{
		_accounts = [];
		_level1Subscriptions.Clear();
		_depthSubscriptions.Clear();
		_tickSubscriptions.Clear();
		_candleSubscriptions.Clear();
		_orderTransactions.Clear();
		_processedFills.Clear();
	}

	private static (string host, int port) GetAddress(EndPoint address)
		=> address switch
		{
			IPEndPoint ip => (ip.Address.ToString(), ip.Port),
			DnsEndPoint dns => (dns.Host, dns.Port),
			_ => throw new InvalidOperationException(LocalizedStrings.InvalidValue.Put(address)),
		};
}
