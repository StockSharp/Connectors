namespace StockSharp.Glassnode;

/// <summary>The message adapter for Glassnode digital-asset data API.</summary>
[MediaIcon(Media.MediaNames.glassnode)]
[Doc("topics/api/connectors/crypto_exchanges/glassnode.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GlassnodeKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.History | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles)]
public partial class GlassnodeMessageAdapter : MessageAdapter, ITokenAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<string, GlassnodeAsset> _assets =
		new(StringComparer.OrdinalIgnoreCase);
	private GlassnodeRestClient _rest;

	/// <summary>Initializes a new instance.</summary>
	public GlassnodeMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(GlassnodeExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.Glassnode];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.IsAssociated(BoardCodes.Glassnode);

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClient();
		base.DisposeManaged();
	}
}
