namespace StockSharp.FivePaisa;

public partial class FivePaisaMessageAdapter
{
	private FivePaisaRestClient _restClient;
	private FivePaisaFeedClient _feedClient;
	private FivePaisaDepthClient _depthClient;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Initializes a new instance of the <see cref="FivePaisaMessageAdapter"/>.</summary>
	public FivePaisaMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [20];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["NSE", "NFO", "CDS", "BSE", "BFO", "BCD", "MCX"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_restClient = new(AppKey, ClientCode, Token) { Parent = this };
		if (this.IsTransactional())
			await _restClient.GetMargin(cancellationToken);

		_feedClient = new(ClientCode, Token.UnSecure(), ReconnectAttempts, ReConnectionSettings.WorkingTime) { Parent = this };
		_feedClient.MarketDataReceived += OnMarketDataReceived;
		_feedClient.OrderReceived += OnOrderReceived;
		_feedClient.StateChanged += SendOutConnectionStateAsync;
		_feedClient.Error += SendOutErrorAsync;
		await _feedClient.Connect(cancellationToken);

		await base.ConnectAsync(connectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_restClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_feedClient != null)
			await _feedClient.Disconnect(cancellationToken);
		if (_depthClient != null)
			await _depthClient.Disconnect(cancellationToken);
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId != 0 && CurrentTime - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
		{
			await SendPortfolioSnapshot(_portfolioSubscriptionId, cancellationToken);
			_lastPortfolioRefresh = CurrentTime;
		}
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_feedClient != null)
		{
			_feedClient.MarketDataReceived -= OnMarketDataReceived;
			_feedClient.OrderReceived -= OnOrderReceived;
			_feedClient.StateChanged -= SendOutConnectionStateAsync;
			_feedClient.Error -= SendOutErrorAsync;
			_feedClient.Dispose();
			_feedClient = null;
		}

		if (_depthClient != null)
		{
			_depthClient.DepthReceived -= OnDepthReceived;
			_depthClient.Error -= SendOutErrorAsync;
			_depthClient.Dispose();
			_depthClient = null;
		}

		_restClient?.Dispose();
		_restClient = null;

		_marketSubscriptions.Clear();
		_depthSubscriptions.Clear();
		_securityIds.Clear();
		_instruments.Clear();
		_lastTicks.Clear();
		_orderTransactions.Clear();
		_transactionOrders.Clear();
		_exchangeOrderIds.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastPortfolioRefresh = default;

		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask<FivePaisaDepthClient> GetDepthClient(CancellationToken cancellationToken)
	{
		if (_depthClient != null)
			return _depthClient;

		var client = new FivePaisaDepthClient(Token.UnSecure(), ReconnectAttempts, ReConnectionSettings.WorkingTime) { Parent = this };
		client.DepthReceived += OnDepthReceived;
		client.Error += SendOutErrorAsync;
		await client.Connect(cancellationToken);
		_depthClient = client;
		return client;
	}
}
