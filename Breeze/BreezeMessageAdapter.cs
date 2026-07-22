namespace StockSharp.Breeze;

public partial class BreezeMessageAdapter
{
	private BreezeRestClient _restClient;
	private BreezeMarketDataClient _marketClient;
	private BreezeOrderClient _orderClient;
	private BreezeOhlcClient _ohlcClient;
	private string _portfolioName;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Initializes a new instance of the <see cref="BreezeMessageAdapter"/>.</summary>
	public BreezeMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["NSE", "NFO"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_restClient = new(Key?.UnSecure(), Secret, ApiSession) { Parent = this };
		var customer = await _restClient.Authenticate(cancellationToken);
		_portfolioName = customer.UserId.IsEmpty() ? _restClient.SocketUser : customer.UserId;

		if (this.IsMarketData())
		{
			_marketClient = new(_restClient.SocketUser, _restClient.SocketToken, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_marketClient.TickReceived += OnTickReceived;
			_marketClient.DepthReceived += OnDepthReceived;
			_marketClient.StateChanged += SendOutConnectionStateAsync;
			_marketClient.Error += SendOutErrorAsync;
			await _marketClient.Connect(cancellationToken);
		}

		if (this.IsTransactional())
		{
			_orderClient = new(_restClient.SocketUser, _restClient.SocketToken, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
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
		_ohlcClient?.Disconnect();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_marketClient != null) await _marketClient.SendHeartbeat(cancellationToken);
		if (_orderClient != null) await _orderClient.SendHeartbeat(cancellationToken);
		if (_ohlcClient != null) await _ohlcClient.SendHeartbeat(cancellationToken);

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
			_marketClient.DepthReceived -= OnDepthReceived;
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
		DisposeOhlcClient();
		_restClient?.Dispose();
		_restClient = null;
		_portfolioName = null;
		_marketSubscriptions.Clear();
		_candleSubscriptions.Clear();
		_securityIds.Clear();
		_instruments.Clear();
		_lastTicks.Clear();
		_orderTransactions.Clear();
		_orderFills.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastPortfolioRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask<BreezeOhlcClient> GetOhlcClient(CancellationToken cancellationToken)
	{
		if (_ohlcClient != null)
			return _ohlcClient;
		var client = new BreezeOhlcClient(_restClient.SocketUser, _restClient.SocketToken,
			ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
		client.CandleReceived += OnCandleReceived;
		client.Error += SendOutErrorAsync;
		try
		{
			await client.Connect(cancellationToken);
			_ohlcClient = client;
			return client;
		}
		catch
		{
			client.CandleReceived -= OnCandleReceived;
			client.Error -= SendOutErrorAsync;
			client.Dispose();
			throw;
		}
	}

	private void DisposeOhlcClient()
	{
		if (_ohlcClient == null)
			return;
		_ohlcClient.CandleReceived -= OnCandleReceived;
		_ohlcClient.Error -= SendOutErrorAsync;
		_ohlcClient.Dispose();
		_ohlcClient = null;
	}
}
