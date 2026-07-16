namespace StockSharp.SierraChartDtc;

using StockSharp.SierraChartDtc.Native;

public partial class SierraChartDtcMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public DataType DataType { get; init; }
		public DtcSymbolState Symbol { get; init; }
	}

	private sealed class DtcSymbolState
	{
		public object SyncRoot { get; } = new();
		public uint SymbolId { get; init; }
		public string Symbol { get; init; }
		public string Exchange { get; init; }
		public SecurityId SecurityId { get; init; }
		public HashSet<long> MarketSubscriptions { get; } = [];
		public HashSet<long> DepthSubscriptions { get; } = [];
		public Dictionary<decimal, QuoteChange> Bids { get; } = [];
		public Dictionary<decimal, QuoteChange> Asks { get; } = [];
	}

	private sealed class SecurityLookupContext
	{
		public SecurityLookupMessage Message { get; init; }
		public HashSet<SecurityTypes> SecurityTypes { get; init; }
		public long Skip { get; set; }
		public long Remaining { get; set; }
	}

	private sealed class HistoricalItem
	{
		public DateTime Time { get; init; }
		public decimal Price { get; init; }
		public decimal Volume { get; init; }
		public DtcAtBidOrAsks AtBidOrAsk { get; init; }
		public DtcHistoricalPriceRecord Candle { get; init; }
	}

	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public string ClientOrderId { get; init; }
		public string ServerOrderId { get; set; }
		public SecurityId SecurityId { get; set; }
		public string PortfolioName { get; set; }
		public Sides Side { get; set; }
		public OrderTypes OrderType { get; set; }
		public decimal Price { get; set; }
		public decimal Volume { get; set; }
		public SierraChartDtcOrderCondition Condition { get; set; }
	}

	private sealed class OrderStatusContext
	{
		public OrderStatusMessage Message { get; init; }
		public bool IsOrdersComplete { get; set; }
		public bool IsFillsComplete { get; set; }
	}

	private sealed class PortfolioContext
	{
		public PortfolioLookupMessage Message { get; init; }
		public bool IsAccountsComplete { get; set; }
		public bool IsPositionsComplete { get; set; }
		public bool IsBalancesComplete { get; set; }
	}

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4),
		TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
		TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
		TimeSpan.FromHours(2), TimeSpan.FromDays(1), TimeSpan.FromDays(7),
	];

	private DtcClient _client;
	private DtcLogonResponse _capabilities;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<string, DtcSymbolState> _symbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<uint, DtcSymbolState> _symbolsById = [];
	private readonly SynchronizedDictionary<int, SecurityLookupContext> _securityLookups = [];
	private readonly SynchronizedDictionary<string, OrderTracker> _ordersByClient = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _ordersByServer = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, OrderTracker> _ordersByTransaction = [];
	private readonly SynchronizedSet<string> _reportedFills = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<int, OrderStatusContext> _orderStatusRequests = [];
	private readonly SynchronizedDictionary<int, PortfolioContext> _portfolioRequests = [];
	private readonly SynchronizedSet<DtcClient> _historyClients = [];
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private int _nextRequestId;
	private int _nextSymbolId;

	/// <summary>Initializes a new instance of the <see cref="SierraChartDtcMessageAdapter"/> class.</summary>
	public SierraChartDtcMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(_timeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Ticks || dataType.IsTFCandles ||
			dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override bool IsSupportExecutionsPnL => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["DTC"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (MarketDepthLevels <= 0)
			throw new ArgumentOutOfRangeException(nameof(MarketDepthLevels));

		_client = CreateClient(Address, SslProtocol);
		try
		{
			_capabilities = await _client.Connect(Login, Password?.UnSecure(), TradeAccount,
				HeartbeatInterval, MarketDataTransmissionInterval, cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeMainClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await _client.Disconnect(cancellationToken);
		DisposeMainClient();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeMainClient();
		foreach (var client in _historyClients.SyncGet(set => set.ToArray()))
			client.Dispose();
		_historyClients.Clear();
		_marketSubscriptions.Clear();
		_symbols.Clear();
		_symbolsById.Clear();
		_securityLookups.Clear();
		_ordersByClient.Clear();
		_ordersByServer.Clear();
		_ordersByTransaction.Clear();
		_reportedFills.Clear();
		_orderStatusRequests.Clear();
		_portfolioRequests.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_capabilities = null;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private DtcClient CreateClient(EndPoint address, SslProtocols sslProtocol)
	{
		var client = new DtcClient(address, sslProtocol, IsCertificateValidation, TargetHost)
		{
			Parent = this,
		};
		client.MessageReceived += ProcessMessage;
		client.Error += SendOutErrorAsync;
		return client;
	}

	private DtcClient CreateHistoryClient()
	{
		var client = new DtcClient(HistoryAddress ?? Address, HistorySslProtocol,
			IsCertificateValidation, TargetHost)
		{
			Parent = this,
		};
		_historyClients.Add(client);
		return client;
	}

	private DtcClient GetClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private async ValueTask ProcessMessage(DtcMessage message, CancellationToken cancellationToken)
	{
		switch (message)
		{
			case DtcSecurityDefinition security:
				await ProcessSecurityDefinition(security, cancellationToken);
				break;
			case DtcMarketDataFeedStatus feedStatus:
				await ProcessFeedStatus(feedStatus, cancellationToken);
				break;
			case DtcMarketDataFeedSymbolStatus symbolFeedStatus:
				await ProcessSymbolFeedStatus(symbolFeedStatus, cancellationToken);
				break;
			case DtcTradingSymbolStatus tradingStatus:
				await ProcessTradingStatus(tradingStatus, cancellationToken);
				break;
			case DtcMarketDataSnapshot snapshot:
				await ProcessSnapshot(snapshot, cancellationToken);
				break;
			case DtcTradeUpdate trade:
				await ProcessTrade(trade, cancellationToken);
				break;
			case DtcBidAskUpdate bidAsk:
				await ProcessBidAsk(bidAsk, cancellationToken);
				break;
			case DtcSessionUpdate session:
				await ProcessSession(session, cancellationToken);
				break;
			case DtcDepthUpdate depth:
				await ProcessDepth(depth, cancellationToken);
				break;
			case DtcOrderUpdate order:
				await ProcessOrder(order, cancellationToken);
				break;
			case DtcHistoricalFill fill:
				await ProcessHistoricalFill(fill, cancellationToken);
				break;
			case DtcPositionUpdate position:
				await ProcessPosition(position, cancellationToken);
				break;
			case DtcTradeAccount account:
				await ProcessTradeAccount(account, cancellationToken);
				break;
			case DtcAccountBalance balance:
				await ProcessBalance(balance, cancellationToken);
				break;
			case DtcReject reject:
				await ProcessReject(reject, cancellationToken);
				break;
		}
	}

	private DtcSymbolState GetOrCreateSymbol(SecurityId securityId)
	{
		var symbol = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		var exchange = securityId.BoardCode.EqualsIgnoreCase("DTC") ? string.Empty : securityId.BoardCode;
		var key = $"{symbol}\u001f{exchange}";
		return _symbols.SafeAdd(key, _ =>
		{
			var id = unchecked((uint)Interlocked.Increment(ref _nextSymbolId));
			if (id == 0)
				id = unchecked((uint)Interlocked.Increment(ref _nextSymbolId));
			var state = new DtcSymbolState
			{
				SymbolId = id,
				Symbol = symbol,
				Exchange = exchange,
				SecurityId = new SecurityId { SecurityCode = symbol, BoardCode = exchange.IsEmpty("DTC") },
			};
			_symbolsById[id] = state;
			return state;
		});
	}

	private int GetRequestId()
	{
		var id = Interlocked.Increment(ref _nextRequestId) & int.MaxValue;
		if (id == 0)
			id = Interlocked.Increment(ref _nextRequestId) & int.MaxValue;
		return id;
	}

	private static SecurityId ToSecurityId(string symbol, string exchange)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = exchange.IsEmpty("DTC"),
		};

	private void DisposeMainClient()
	{
		if (_client == null)
			return;
		_client.MessageReceived -= ProcessMessage;
		_client.Error -= SendOutErrorAsync;
		_client.Dispose();
		_client = null;
	}
}
