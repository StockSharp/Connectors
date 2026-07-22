namespace StockSharp.CryptoQuant;

/// <summary>The message adapter for CryptoQuant digital-asset data API.</summary>
[MediaIcon(Media.MediaNames.cryptoquant)]
[Doc("topics/api/connectors/crypto_exchanges/cryptoquant.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CryptoQuantKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.History | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles)]
public partial class CryptoQuantMessageAdapter : MessageAdapter, ITokenAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<string, CryptoQuantInstrument> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private CryptoQuantRestClient _rest;

	/// <summary>Initializes a new instance.</summary>
	public CryptoQuantMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(CryptoQuantExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.CryptoQuant];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.IsAssociated(BoardCodes.CryptoQuant);

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClient();
		base.DisposeManaged();
	}
}
