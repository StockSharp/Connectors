namespace StockSharp.Dhan;

public partial class DhanMessageAdapter
{
	private DhanRestClient _restClient;
	private DhanMarketDataClient _marketClient;
	private DhanOrderClient _orderClient;
	private DhanDepthClient _depth20Client;
	private DhanDepthClient _depth200Client;

	/// <summary>Initializes a new instance of the <see cref="DhanMessageAdapter"/>.</summary>
	public DhanMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5, 20, 200];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["IDX_I", "NSE_EQ", "NSE_FNO", "NSE_CURRENCY", "BSE_EQ", "BSE_FNO", "BSE_CURRENCY", "MCX_COMM"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_restClient = new(ClientId, Token) { Parent = this };

		if (this.IsTransactional())
			await _restClient.GetFunds(cancellationToken);

		if (this.IsMarketData())
		{
			_marketClient = new(ClientId, Token.UnSecure(), ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_marketClient.TickReceived += OnTickReceived;
			_marketClient.StateChanged += SendOutConnectionStateAsync;
			_marketClient.Error += SendOutErrorAsync;
			await _marketClient.Connect(cancellationToken);
		}

		if (this.IsTransactional())
		{
			_orderClient = new(ClientId, Token.UnSecure(), ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
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
		_depth20Client?.Disconnect();
		_depth200Client?.Disconnect();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshot(cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_marketClient != null)
		{
			_marketClient.TickReceived -= OnTickReceived;
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

		DisposeDepthClient(ref _depth20Client);
		DisposeDepthClient(ref _depth200Client);

		_restClient?.Dispose();
		_restClient = null;

		_marketSubscriptions.Clear();
		_depthSubscriptions.Clear();
		_securityIds.Clear();
		_instrumentTypes.Clear();
		_lastTicks.Clear();
		_orderTransactions.Clear();
		_orderFills.Clear();
		_tradeIds.Clear();
		_foreverOrders.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;

		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask<DhanDepthClient> GetDepthClient(int depth, CancellationToken cancellationToken)
	{
		var client = depth == 20 ? _depth20Client : _depth200Client;
		if (client != null)
			return client;

		client = new DhanDepthClient(ClientId, Token.UnSecure(), depth, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
		client.DepthReceived += OnDepthReceived;
		client.Error += SendOutErrorAsync;
		await client.Connect(cancellationToken);

		if (depth == 20)
			_depth20Client = client;
		else
			_depth200Client = client;

		return client;
	}

	private void DisposeDepthClient(ref DhanDepthClient client)
	{
		if (client == null)
			return;
		client.DepthReceived -= OnDepthReceived;
		client.Error -= SendOutErrorAsync;
		client.Dispose();
		client = null;
	}
}
