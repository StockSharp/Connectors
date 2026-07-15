namespace StockSharp.TradeZero;

[OrderCondition(typeof(TradeZeroOrderCondition))]
public partial class TradeZeroMessageAdapter
{
	private TradeZeroClient _httpClient;
	private TradeZeroSocketClient _portfolioSocket;
	private TradeZeroSocketClient _pnlSocket;

	/// <summary>
	/// Initializes a new instance of the <see cref="TradeZeroMessageAdapter"/>.
	/// </summary>
	public TradeZeroMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <summary>
	/// Supported time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => false;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient != null || _portfolioSocket != null || _pnlSocket != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new(Key.UnSecure(), Secret) { Parent = this };
		await _httpClient.GetAccounts(cancellationToken);

		if (this.IsTransactional())
		{
			_portfolioSocket = new(TradeZeroStreamKinds.Portfolio, Key.UnSecure(), Secret, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_portfolioSocket.PortfolioReceived += OnPortfolioReceived;
			_portfolioSocket.Error += OnSocketError;
			_portfolioSocket.StateChanged += SendOutConnectionStateAsync;
			await _portfolioSocket.ConnectAsync(cancellationToken);

			_pnlSocket = new(TradeZeroStreamKinds.Pnl, Key.UnSecure(), Secret, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_pnlSocket.PnlReceived += OnPnlReceived;
			_pnlSocket.Error += OnSocketError;
			_pnlSocket.StateChanged += SendOutConnectionStateAsync;
			await _pnlSocket.ConnectAsync(cancellationToken);
		}

		await base.ConnectAsync(connectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_portfolioSocket?.Disconnect();
		_pnlSocket?.Disconnect();
		return base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_portfolioSocket != null)
		{
			_portfolioSocket.PortfolioReceived -= OnPortfolioReceived;
			_portfolioSocket.Error -= OnSocketError;
			_portfolioSocket.StateChanged -= SendOutConnectionStateAsync;
			_portfolioSocket.Dispose();
			_portfolioSocket = null;
		}

		if (_pnlSocket != null)
		{
			_pnlSocket.PnlReceived -= OnPnlReceived;
			_pnlSocket.Error -= OnSocketError;
			_pnlSocket.StateChanged -= SendOutConnectionStateAsync;
			_pnlSocket.Dispose();
			_pnlSocket = null;
		}

		_httpClient?.Dispose();
		_httpClient = null;
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private ValueTask OnSocketError(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);
}
