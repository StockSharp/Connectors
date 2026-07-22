namespace StockSharp.CoinMetrics;

/// <summary>The message adapter for Coin Metrics API v4.</summary>
[MediaIcon(Media.MediaNames.coinmetrics)]
[Doc("topics/api/connectors/crypto_exchanges/coin_metrics.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinMetricsKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles)]
public partial class CoinMetricsMessageAdapter : MessageAdapter, ITokenAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public CoinMetricsStreamKey Key { get; init; }
		public int Depth { get; init; }
		public long? Remaining { get; set; }
		public DateTime? LastCountedCandle { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _streamGate = new(1, 1);
	private readonly Dictionary<string, CoinMetricsMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly Dictionary<CoinMetricsStreamKey, CoinMetricsStreamClient>
		_streams = [];
	private CoinMetricsRestClient _rest;

	/// <summary>Initializes a new instance.</summary>
	public CoinMetricsMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(CoinMetricsExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.CoinMetrics];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.IsAssociated(BoardCodes.CoinMetrics);

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		_streamGate.Dispose();
		base.DisposeManaged();
	}
}
