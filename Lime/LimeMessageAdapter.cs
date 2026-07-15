namespace StockSharp.Lime;

public partial class LimeMessageAdapter
{
	private LimeClient _httpClient;
	private LimeSocketClient _accountSocket;

	/// <summary>
	/// Initializes a new instance of the <see cref="LimeMessageAdapter"/>.
	/// </summary>
	public LimeMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
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
		TimeSpan.FromDays(7),
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => false;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient != null || _accountSocket != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new(Login, Password, ClientId, ClientSecret) { Parent = this };
		await _httpClient.Authenticate(cancellationToken);
		await EnsureAccounts(cancellationToken);

		if (this.IsTransactional())
		{
			_accountSocket = new(_httpClient.AccessToken, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_accountSocket.BalanceReceived += OnBalanceReceived;
			_accountSocket.PositionsReceived += OnPositionsReceived;
			_accountSocket.OrderReceived += OnOrderReceived;
			_accountSocket.TradeReceived += OnTradeReceived;
			_accountSocket.Error += SendOutErrorAsync;
			_accountSocket.StateChanged += SendOutConnectionStateAsync;
			await _accountSocket.ConnectAsync(cancellationToken);
		}

		await base.ConnectAsync(connectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_accountSocket?.Disconnect();
		return base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_accountSocket != null)
		{
			_accountSocket.BalanceReceived -= OnBalanceReceived;
			_accountSocket.PositionsReceived -= OnPositionsReceived;
			_accountSocket.OrderReceived -= OnOrderReceived;
			_accountSocket.TradeReceived -= OnTradeReceived;
			_accountSocket.Error -= SendOutErrorAsync;
			_accountSocket.StateChanged -= SendOutConnectionStateAsync;
			_accountSocket.Dispose();
			_accountSocket = null;
		}

		_httpClient?.Dispose();
		_httpClient = null;
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_accountSocket != null)
			await _accountSocket.SendHeartbeat(cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}
}
