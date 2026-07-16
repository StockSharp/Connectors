namespace StockSharp.KotakNeo;

public partial class KotakNeoMessageAdapter
{
	private KotakNeoRestClient _restClient;
	private KotakNeoMarketDataClient _marketClient;
	private KotakNeoOrderClient _orderClient;

	/// <summary>Initializes a new instance of the <see cref="KotakNeoMessageAdapter"/>.</summary>
	public KotakNeoMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(25);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["NSE", "NFO", "BSE", "BFO", "MCX", "CDS", "BCD"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_restClient = new(ConsumerKey.ThrowIfEmpty(nameof(ConsumerKey)).UnSecure()) { Parent = this };
		var session = await _restClient.Login(MobileNumber, UserCode,
			KotakNeoTotp.Generate(TotpSecret.ThrowIfEmpty(nameof(TotpSecret)).UnSecure(), DateTime.UtcNow),
			Mpin, cancellationToken);

		if (this.IsMarketData())
		{
			_marketClient = new(session, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_marketClient.UpdateReceived += OnMarketUpdate;
			_marketClient.StateChanged += SendOutConnectionStateAsync;
			_marketClient.Error += SendOutErrorAsync;
			await _marketClient.Connect(cancellationToken);
		}

		if (this.IsTransactional())
		{
			_orderClient = new(session, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_orderClient.OrderReceived += OnOrderReceived;
			_orderClient.StateChanged += SendOutConnectionStateAsync;
			_orderClient.Error += SendOutErrorAsync;
			await _orderClient.Connect(cancellationToken);
		}

		await base.ConnectAsync(connectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_restClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_marketClient?.Disconnect();
		_orderClient?.Disconnect();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_orderClient != null)
			await _orderClient.SendHeartbeat(cancellationToken);
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshot(_portfolioSubscriptionId, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_marketClient != null)
		{
			_marketClient.UpdateReceived -= OnMarketUpdate;
			_marketClient.StateChanged -= SendOutConnectionStateAsync;
			_marketClient.Error -= SendOutErrorAsync;
			_marketClient.Dispose();
			_marketClient = null;
		}

		if (_orderClient != null)
		{
			_orderClient.OrderReceived -= OnOrderReceived;
			_orderClient.StateChanged -= SendOutConnectionStateAsync;
			_orderClient.Error -= SendOutErrorAsync;
			_orderClient.Dispose();
			_orderClient = null;
		}

		_restClient?.Dispose();
		_restClient = null;
		_marketSubscriptions.Clear();
		_securityIds.Clear();
		_indexKeys.Clear();
		_lastTicks.Clear();
		_orderTransactions.Clear();
		_orderFills.Clear();
		_orderDetails.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;

		await base.ResetAsync(resetMsg, cancellationToken);
	}
}
