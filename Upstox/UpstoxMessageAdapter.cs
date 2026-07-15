namespace StockSharp.Upstox;

public partial class UpstoxMessageAdapter
{
	private UpstoxRestClient _restClient;
	private UpstoxMarketDataClient _marketClient;
	private UpstoxPortfolioClient _portfolioClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="UpstoxMessageAdapter"/>.
	/// </summary>
	public UpstoxMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
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
	public override bool IsSupportExecutionsPnL => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["NSE_EQ", "NSE_FO", "NSE_INDEX", "BSE_EQ", "BSE_FO", "BSE_INDEX", "MCX_FO", "NCD_FO", "BCD_FO"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_restClient = new(IsDemo, Token) { Parent = this };

		if (!IsDemo && this.IsMarketData())
		{
			_marketClient = new(await _restClient.GetMarketDataStreamUrl(cancellationToken), ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_marketClient.FeedReceived += OnFeedReceived;
			_marketClient.MarketInfoReceived += OnMarketInfoReceived;
			_marketClient.StateChanged += SendOutConnectionStateAsync;
			_marketClient.Error += SendOutErrorAsync;
			await _marketClient.Connect(cancellationToken);
		}

		if (!IsDemo && this.IsTransactional())
		{
			_portfolioClient = new(await _restClient.GetPortfolioStreamUrl(cancellationToken), ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_portfolioClient.UpdateReceived += OnPortfolioUpdate;
			_portfolioClient.StateChanged += SendOutConnectionStateAsync;
			_portfolioClient.Error += SendOutErrorAsync;
			await _portfolioClient.Connect(cancellationToken);
		}

		await base.ConnectAsync(connectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_restClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_marketClient?.Disconnect();
		_portfolioClient?.Disconnect();
		return base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_marketClient != null)
		{
			_marketClient.FeedReceived -= OnFeedReceived;
			_marketClient.MarketInfoReceived -= OnMarketInfoReceived;
			_marketClient.StateChanged -= SendOutConnectionStateAsync;
			_marketClient.Error -= SendOutErrorAsync;
			_marketClient.Dispose();
			_marketClient = null;
		}

		if (_portfolioClient != null)
		{
			_portfolioClient.UpdateReceived -= OnPortfolioUpdate;
			_portfolioClient.StateChanged -= SendOutConnectionStateAsync;
			_portfolioClient.Error -= SendOutErrorAsync;
			_portfolioClient.Dispose();
			_portfolioClient = null;
		}

		_restClient?.Dispose();
		_restClient = null;

		_marketSubscriptions.Clear();
		_securityIds.Clear();
		_lastTicks.Clear();
		_orderTransactions.Clear();
		_orderFills.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioName = null;

		await base.ResetAsync(resetMsg, cancellationToken);
	}
}
