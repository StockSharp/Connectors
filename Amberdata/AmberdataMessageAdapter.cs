namespace StockSharp.Amberdata;

/// <summary>The message adapter for Amberdata spot market data APIs.</summary>
[MediaIcon(Media.MediaNames.amberdata)]
[Doc("topics/api/connectors/crypto_exchanges/amberdata.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AmberdataKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Candles)]
public partial class AmberdataMessageAdapter : MessageAdapter, ITokenAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public AmberdataStreamKey Key { get; init; }
		public int Depth { get; init; }
		public long? Remaining { get; set; }
		public DateTime? LastCountedCandle { get; set; }
	}

	private sealed class BookState
	{
		public AmberdataSocketBookLevel[] Bids { get; set; }
		public AmberdataSocketBookLevel[] Asks { get; set; }
		public DateTime BidTime { get; set; }
		public DateTime AskTime { get; set; }
		public DateTime ServerTime { get; set; }
		public long Sequence { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, AmberdataReference> _references =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly Dictionary<AmberdataStreamKey, BookState> _books = [];
	private AmberdataRestClient _rest;
	private AmberdataSocketClient _socket;

	/// <summary>Initializes a new instance.</summary>
	public AmberdataMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AmberdataExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.Amberdata];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.IsAssociated(BoardCodes.Amberdata);

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
