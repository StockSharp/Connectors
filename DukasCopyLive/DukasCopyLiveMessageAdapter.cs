namespace StockSharp.DukasCopyLive;

public partial class DukasCopyLiveMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public DataType DataType { get; init; }
		public TimeSpan? TimeFrame { get; init; }
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
		public DukasCopyLiveOrderCondition Condition { get; set; }
	}

	private DukasCopyLiveBridgeClient _client;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<string, OrderTracker> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _filledAmounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, DukasCopyLiveOrder> _positionOrders = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioName;

	/// <summary>Initializes a new instance of the <see cref="DukasCopyLiveMessageAdapter"/> class.</summary>
	public DukasCopyLiveMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(15);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(DukasCopyLiveExtensions.TimeFrames.Keys);
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
	public override string[] AssociatedBoards { get; } = [DukasCopyLiveExtensions.BoardCode];

	/// <inheritdoc />
	public override IEnumerable<Level1Fields> CandlesBuildFrom { get; } =
		[Level1Fields.BestBidPrice, Level1Fields.BestAskPrice];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var userName = Login.ThrowIfEmpty(nameof(Login));
		var password = Password?.UnSecure().ThrowIfEmpty(nameof(Password));
		_client = new(BridgePort, BridgeJarPath);
		_client.TickReceived += ProcessTick;
		_client.BarReceived += ProcessBar;
		_client.OrderReceived += ProcessOrderUpdate;
		_client.AccountReceived += ProcessAccountUpdate;
		_client.Error += SendOutErrorAsync;

		try
		{
			await _client.Connect(userName, password, IsDemo, cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClient();
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
		DisposeClient();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		_marketSubscriptions.Clear();
		_orders.Clear();
		_filledAmounts.Clear();
		_positionOrders.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioName = null;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private DukasCopyLiveBridgeClient GetClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private async ValueTask AddMarketSubscription(MarketDataMessage mdMsg, DataType dataType,
		TimeSpan? timeFrame, CancellationToken cancellationToken)
	{
		var symbol = mdMsg.SecurityId.SecurityCode.NormalizeDukasSymbol();
		var isFirst = !_marketSubscriptions.CachedValues.Any(s =>
			s.SecurityId.SecurityCode.NormalizeDukasSymbol().EqualsIgnoreCase(symbol));

		_marketSubscriptions[mdMsg.TransactionId] = new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			DataType = dataType,
			TimeFrame = timeFrame,
		};

		try
		{
			if (isFirst)
				await GetClient().Subscribe([symbol], cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask RemoveMarketSubscription(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var removed))
			return;

		var symbol = removed.SecurityId.SecurityCode.NormalizeDukasSymbol();
		if (!_marketSubscriptions.CachedValues.Any(s =>
			s.SecurityId.SecurityCode.NormalizeDukasSymbol().EqualsIgnoreCase(symbol)))
			await GetClient().Unsubscribe([symbol], cancellationToken);
	}

	private void DisposeClient()
	{
		if (_client == null)
			return;

		_client.TickReceived -= ProcessTick;
		_client.BarReceived -= ProcessBar;
		_client.OrderReceived -= ProcessOrderUpdate;
		_client.AccountReceived -= ProcessAccountUpdate;
		_client.Error -= SendOutErrorAsync;
		_client.Dispose();
		_client = null;
	}
}
