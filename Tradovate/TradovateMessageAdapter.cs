namespace StockSharp.Tradovate;

[OrderCondition(typeof(TradovateOrderCondition))]
public partial class TradovateMessageAdapter
{
	private TradovateClient _httpClient;
	private TradovateSocketClient _marketSocket;
	private TradovateSocketClient _accountSocket;

	/// <summary>
	/// Initializes a new instance of the <see cref="TradovateMessageAdapter"/>.
	/// </summary>
	public TradovateMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
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
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient != null || _marketSocket != null || _accountSocket != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new(IsDemo, Login, Password, AppId, AppVersion, DeviceId, ClientId, Secret) { Parent = this };
		await _httpClient.Authenticate(cancellationToken);

		if (this.IsMarketData())
		{
			_marketSocket = new("wss://md.tradovateapi.com/v1/websocket", _httpClient.AccessToken, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_marketSocket.QuoteReceived += OnQuoteReceived;
			_marketSocket.DomReceived += OnDomReceived;
			_marketSocket.ChartReceived += OnChartReceived;
			_marketSocket.Error += OnSocketError;
			_marketSocket.StateChanged += SendOutConnectionStateAsync;
			await _marketSocket.ConnectAsync(cancellationToken);
		}

		if (this.IsTransactional())
		{
			var environment = IsDemo ? "demo" : "live";
			_accountSocket = new($"wss://{environment}.tradovateapi.com/v1/websocket", _httpClient.AccessToken, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_accountSocket.EntityReceived += OnEntityReceived;
			_accountSocket.Error += OnSocketError;
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

		_marketSocket?.Disconnect();
		_accountSocket?.Disconnect();
		return base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_marketSocket != null)
		{
			_marketSocket.QuoteReceived -= OnQuoteReceived;
			_marketSocket.DomReceived -= OnDomReceived;
			_marketSocket.ChartReceived -= OnChartReceived;
			_marketSocket.Error -= OnSocketError;
			_marketSocket.StateChanged -= SendOutConnectionStateAsync;
			_marketSocket.Dispose();
			_marketSocket = null;
		}

		if (_accountSocket != null)
		{
			_accountSocket.EntityReceived -= OnEntityReceived;
			_accountSocket.Error -= OnSocketError;
			_accountSocket.StateChanged -= SendOutConnectionStateAsync;
			_accountSocket.Dispose();
			_accountSocket = null;
		}

		_httpClient?.Dispose();
		_httpClient = null;
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private ValueTask OnSocketError(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);
}
