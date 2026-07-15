namespace StockSharp.Fyers;

public partial class FyersMessageAdapter
{
	private FyersRestClient _restClient;
	private FyersMarketDataClient _marketClient;
	private FyersOrderClient _orderClient;
	private FyersTbtClient _tbtClient;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Initializes a new instance of the <see cref="FyersMessageAdapter"/>.</summary>
	public FyersMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5, 50];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["NSE", "NFO", "CDS", "NCO", "BSE", "BFO", "MCX"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_restClient = new(ClientId, Token) { Parent = this };
		if (this.IsTransactional())
			await _restClient.GetProfile(cancellationToken);

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
			_orderClient.TradeReceived += OnTradeReceived;
			_orderClient.PositionReceived += OnPositionReceived;
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
		_tbtClient?.Disconnect();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_marketClient != null)
			await _marketClient.SendHeartbeat(cancellationToken);
		if (_orderClient != null)
			await _orderClient.SendHeartbeat(cancellationToken);
		if (_tbtClient != null)
			await _tbtClient.SendHeartbeat(cancellationToken);

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
			_orderClient.TradeReceived -= OnTradeReceived;
			_orderClient.PositionReceived -= OnPositionReceived;
			_orderClient.StateChanged -= SendOutConnectionStateAsync;
			_orderClient.Error -= SendOutErrorAsync;
			_orderClient.Dispose();
			_orderClient = null;
		}

		DisposeTbtClient();
		_restClient?.Dispose();
		_restClient = null;

		_marketSubscriptions.Clear();
		_depth50Subscriptions.Clear();
		_securityIds.Clear();
		_instruments.Clear();
		_lastTicks.Clear();
		_orderTransactions.Clear();
		_orderFills.Clear();
		_tradeIds.Clear();
		_gttOrders.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastPortfolioRefresh = default;

		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask<FyersTbtClient> GetTbtClient(CancellationToken cancellationToken)
	{
		if (_tbtClient != null)
			return _tbtClient;

		var client = new FyersTbtClient(await _restClient.GetTbtUrl(cancellationToken), ClientId, Token.UnSecure(),
			ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
		client.DepthReceived += OnDepthReceived;
		client.Error += SendOutErrorAsync;
		try
		{
			await client.Connect(cancellationToken);
			_tbtClient = client;
			return client;
		}
		catch
		{
			client.DepthReceived -= OnDepthReceived;
			client.Error -= SendOutErrorAsync;
			client.Dispose();
			throw;
		}
	}

	private void DisposeTbtClient()
	{
		if (_tbtClient == null)
			return;
		_tbtClient.DepthReceived -= OnDepthReceived;
		_tbtClient.Error -= SendOutErrorAsync;
		_tbtClient.Dispose();
		_tbtClient = null;
	}
}
