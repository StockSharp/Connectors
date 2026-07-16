namespace StockSharp.Saxo;

public partial class SaxoMessageAdapter
{
	private SaxoNativeClient _client;
	private readonly SynchronizedDictionary<long, SaxoInstrument> _instruments = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _activityTrades = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <summary>Initializes a new instance of the <see cref="SaxoMessageAdapter"/> class.</summary>
	public SaxoMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(SaxoExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["SAXO"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		var accessToken = AccessToken?.UnSecure();
		var refreshToken = RefreshToken?.UnSecure();
		if (accessToken.IsEmpty() && refreshToken.IsEmpty())
			throw new InvalidOperationException("Saxo AccessToken or RefreshToken is required.");

		_client = new(Environment, accessToken, refreshToken, ClientId, ClientSecret?.UnSecure(), RedirectUri,
			Math.Max(1, ReConnectionSettings.ReAttemptCount)) { Parent = this };
		_client.PriceReceived += OnPriceReceived;
		_client.CandleReceived += OnCandleReceived;
		_client.BalanceReceived += OnBalanceReceived;
		_client.PositionReceived += OnPositionReceived;
		_client.ActivityReceived += OnActivityReceived;
		_client.Error += SendOutErrorAsync;
		_client.StateChanged += SendOutConnectionStateAsync;

		try
		{
			await _client.Connect(AccountKey, cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await _client.Disconnect();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClient();
		_instruments.Clear();
		_orderTransactions.Clear();
		_activityTrades.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void DisposeClient()
	{
		if (_client == null)
			return;
		_client.PriceReceived -= OnPriceReceived;
		_client.CandleReceived -= OnCandleReceived;
		_client.BalanceReceived -= OnBalanceReceived;
		_client.PositionReceived -= OnPositionReceived;
		_client.ActivityReceived -= OnActivityReceived;
		_client.Error -= SendOutErrorAsync;
		_client.StateChanged -= SendOutConnectionStateAsync;
		_client.Dispose();
		_client = null;
	}
}
