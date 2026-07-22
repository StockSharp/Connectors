namespace StockSharp.LsSecurities;

public partial class LsSecuritiesMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public DataType DataType { get; init; }
	}

	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string PortfolioName { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; set; }
		public decimal Price { get; set; }
		public decimal Volume { get; set; }
		public TimeInForce TimeInForce { get; set; }
		public LsSecuritiesOrderCondition Condition { get; init; }
	}

	private LsSecuritiesRestClient _rest;
	private LsSecuritiesWebSocketClient _stream;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<string, LsInstrument> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, OrderTracker> _orders = [];
	private readonly SynchronizedSet<string> _reportedTrades = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Initializes a new instance of the <see cref="LsSecuritiesMessageAdapter"/> class.</summary>
	public LsSecuritiesMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(LsSecuritiesExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles ||
			dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override bool IsSupportExecutionsPnL => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["KRX", "NXT"];

	private string PortfolioName => Account.IsEmpty("LS");

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest != null || _stream != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_rest = new(Key?.UnSecure(), Secret?.UnSecure(),
			Math.Max(1, ReConnectionSettings.ReAttemptCount)) { Parent = this };
		try
		{
			await _rest.Connect(cancellationToken);
			_stream = new(_rest.GetAccessToken, IsDemo, Math.Max(1, ReConnectionSettings.ReAttemptCount))
			{
				Parent = this,
			};
			_stream.TradeReceived += ProcessTrade;
			_stream.DepthReceived += ProcessDepth;
			_stream.OrderReceived += ProcessRealtimeOrder;
			_stream.Error += SendOutErrorAsync;
			_stream.StateChanged += SendOutConnectionStateAsync;
			foreach (var code in new[] { "SC0", "SC1", "SC2", "SC3", "SC4" })
				await _stream.Subscribe(code, string.Empty, true, cancellationToken);
			await _stream.Connect(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest == null || _stream == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await _stream.Disconnect();
		DisposeClients();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClients();
		_marketSubscriptions.Clear();
		_instruments.Clear();
		_orders.Clear();
		_reportedTrades.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastPortfolioRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId != 0 &&
			DateTime.UtcNow - _lastPortfolioRefresh >= TimeSpan.FromSeconds(15))
		{
			await SendPortfolioSnapshot(_portfolioSubscriptionId, cancellationToken);
			_lastPortfolioRefresh = DateTime.UtcNow;
		}
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private LsSecuritiesRestClient GetRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private static SecurityId ToSecurityId(string code)
		=> new() { SecurityCode = code.NormalizeCode(), BoardCode = "KRX" };

	private void CacheInstrument(LsInstrument instrument)
	{
		if (instrument?.Code.IsEmpty() == false)
			_instruments[instrument.Code.NormalizeCode()] = instrument;
	}

	private async Task EnsureInstruments(CancellationToken cancellationToken)
	{
		if (_instruments.Count > 0)
			return;
		foreach (var instrument in await GetRest().GetInstruments(cancellationToken))
			CacheInstrument(instrument);
	}

	private void DisposeClients()
	{
		if (_stream != null)
		{
			_stream.TradeReceived -= ProcessTrade;
			_stream.DepthReceived -= ProcessDepth;
			_stream.OrderReceived -= ProcessRealtimeOrder;
			_stream.Error -= SendOutErrorAsync;
			_stream.StateChanged -= SendOutConnectionStateAsync;
			_stream.Dispose();
			_stream = null;
		}

		_rest?.Dispose();
		_rest = null;
	}
}
