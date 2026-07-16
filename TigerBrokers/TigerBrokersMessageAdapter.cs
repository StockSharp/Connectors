namespace StockSharp.TigerBrokers;

public partial class TigerBrokersMessageAdapter
{
	private TigerNativeClient _client;
	private readonly SynchronizedDictionary<string, TigerInstrument> _instruments = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, TigerMarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<long, long> _orderTransactions = [];
	private readonly SynchronizedSet<long> _trades = [];
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <summary>Initializes a new instance of the <see cref="TigerBrokersMessageAdapter"/> class.</summary>
	public TigerBrokersMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(TigerExtensions.TimeFrames);
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
	public override string[] AssociatedBoards { get; } =
		["TIGER_US", "TIGER_HK", "TIGER_CN", "TIGER_SG", "TIGER_AU", "TIGER_NZ", "TIGER_UK"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var config = new TigerConfig
		{
			TigerId = TigerId.ThrowIfEmpty(nameof(TigerId)),
			DefaultAccount = Account.ThrowIfEmpty(nameof(Account)),
			License = License.ToNative(),
			PrivateKey = PrivateKey.ThrowIfEmpty(nameof(PrivateKey)).UnSecure(),
			Token = Token?.UnSecure(),
			AutoGrabPermission = AutoGrabPermission,
			AutoRefreshToken = false,
			FailRetryCounts = Math.Clamp(ReConnectionSettings.ReAttemptCount, 0, 5),
			Language = Language.en_US,
			TimeZone = TimeZoneInfo.Utc,
			UseFullTick = true,
		};

		_client = new(config) { Parent = this };
		_client.QuoteReceived += OnQuoteReceived;
		_client.BboReceived += OnBboReceived;
		_client.DepthReceived += OnDepthReceived;
		_client.TradeTickReceived += OnTradeTickReceived;
		_client.FullTickReceived += OnFullTickReceived;
		_client.KlineReceived += OnKlineReceived;
		_client.OrderReceived += OnOrderReceived;
		_client.OrderTransactionReceived += OnOrderTransactionReceived;
		_client.PositionReceived += OnPositionReceived;
		_client.AssetReceived += OnAssetReceived;
		_client.Error += SendOutErrorAsync;
		_client.StateChanged += SendOutConnectionStateAsync;

		try
		{
			await _client.Connect(cancellationToken);
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
		_client.Disconnect();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClient();
		_instruments.Clear();
		_marketSubscriptions.Clear();
		_orderTransactions.Clear();
		_trades.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void DisposeClient()
	{
		if (_client == null)
			return;
		_client.QuoteReceived -= OnQuoteReceived;
		_client.BboReceived -= OnBboReceived;
		_client.DepthReceived -= OnDepthReceived;
		_client.TradeTickReceived -= OnTradeTickReceived;
		_client.FullTickReceived -= OnFullTickReceived;
		_client.KlineReceived -= OnKlineReceived;
		_client.OrderReceived -= OnOrderReceived;
		_client.OrderTransactionReceived -= OnOrderTransactionReceived;
		_client.PositionReceived -= OnPositionReceived;
		_client.AssetReceived -= OnAssetReceived;
		_client.Error -= SendOutErrorAsync;
		_client.StateChanged -= SendOutConnectionStateAsync;
		_client.Dispose();
		_client = null;
	}
}
