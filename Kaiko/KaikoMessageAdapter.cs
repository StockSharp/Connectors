namespace StockSharp.Kaiko;

/// <summary>The message adapter for Kaiko REST and Stream APIs.</summary>
[MediaIcon(Media.MediaNames.kaiko)]
[Doc("topics/api/connectors/crypto_exchanges/kaiko.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KaikoKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles)]
public partial class KaikoMessageAdapter : MessageAdapter, ITokenAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public KaikoSecurityKey Key { get; init; }
		public KaikoSubscriptionKinds Kind { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public long? Remaining { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, KaikoSecurityKey> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private KaikoRestClient _rest;
	private KaikoStreamClient _stream;

	/// <summary>Initializes a new instance.</summary>
	public KaikoMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(KaikoExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.Kaiko];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.IsAssociated(BoardCodes.Kaiko);
}
