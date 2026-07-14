namespace StockSharp.Schwab;

public partial class SchwabMessageAdapter
{
	private SchwabClient _client;
	private SchwabStreamerClient _streamer;
	private readonly SynchronizedDictionary<long, SecurityId> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<long, (SecurityId securityId, string service)> _depthSubscriptions = [];

	/// <summary>
	/// Initializes a new instance of the adapter.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction identifier generator.</param>
	public SchwabMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <summary>
	/// Supported candle time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromDays(1),
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles || dataType == DataType.Transactions || dataType == DataType.PositionChanges;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (_client is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_client = new(Address, Token);
		_streamer = new(await _client.GetUserPreferences(cancellationToken), Token, ReConnectionSettings.AttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
		_streamer.LevelOneReceived += OnLevelOneReceived;
		_streamer.BookReceived += OnBookReceived;
		_streamer.AccountActivityReceived += OnAccountActivityReceived;
		_streamer.Error += OnStreamerError;
		await _streamer.ConnectAsync(cancellationToken);
		await _streamer.SubscribeAccountActivity(cancellationToken);
		await base.ConnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message, CancellationToken cancellationToken)
	{
		_level1Subscriptions.Clear();
		_depthSubscriptions.Clear();
		if (_streamer is not null)
		{
			_streamer.LevelOneReceived -= OnLevelOneReceived;
			_streamer.BookReceived -= OnBookReceived;
			_streamer.AccountActivityReceived -= OnAccountActivityReceived;
			_streamer.Error -= OnStreamerError;
			_streamer.Disconnect();
			_streamer.Dispose();
			_streamer = null;
		}
		_client?.Dispose();
		_client = null;
		await base.ResetAsync(message, cancellationToken);
	}
}
