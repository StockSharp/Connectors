namespace StockSharp.Finra;

/// <summary>
/// The message adapter for FINRA Query API equity datasets.
/// </summary>
[MediaIcon(Media.MediaNames.finra)]
[Doc("topics/api/connectors/stock_market/finra.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FinraKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Free |
	MessageAdapterCategories.US |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.Level1)]
public partial class FinraMessageAdapter :
	MessageAdapter,
	IKeySecretAdapter,
	ITokenAdapter,
	IDemoAdapter,
	IAddressAdapter<Uri>
{
	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = "FINRA API Client ID.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = "FINRA API Client Secret.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = "Optional existing FINRA OAuth access token. Client credentials are used when empty.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public SecureString Token { get; set; }

	/// <inheritdoc />
	[Display(
		Name = "Mock data",
		Description = "Append Mock to the dataset name for a FINRA mock credential.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>FINRA equity dataset exposed as Level 1 history.</summary>
	[Display(
		Name = "Dataset",
		Description = "Public FINRA equity dataset projected into StockSharp Level 1 fields.",
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 4)]
	[BasicSetting]
	public FinraDataSets DataSet { get; set; } =
		FinraDataSets.ConsolidatedShortInterest;

	/// <summary>
	/// Optional weekly-summary tier such as T1, T2, OTCE, or NA.
	/// </summary>
	[Display(
		Name = "Weekly tier",
		Description = "Optional weeklySummary tierIdentifier filter (T1, T2, OTCE, or NA).",
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 5)]
	public string WeeklyTierIdentifier { get; set; }

	/// <summary>
	/// Optional weekly summary type, for example ATS_W_SMBL or OTC_W_SMBL.
	/// </summary>
	[Display(
		Name = "Weekly summary type",
		Description = "Optional weeklySummary summaryTypeCode filter, such as ATS_W_SMBL or OTC_W_SMBL.",
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 6)]
	public string WeeklySummaryTypeCode { get; set; } = "ATS_W_SMBL";

	/// <summary>Maximum records requested in one synchronous page.</summary>
	[Display(
		Name = "Page size",
		Description = "FINRA synchronous page size from 1 to 5000.",
		GroupName = "Limits",
		Order = 7)]
	public int PageSize { get; set; } = 5000;

	/// <summary>Maximum raw records loaded by one subscription.</summary>
	[Display(
		Name = "Maximum records",
		Description = "Safety limit for raw records loaded by one lookup or history request.",
		GroupName = "Limits",
		Order = 8)]
	public int MaxRecords { get; set; } = 100000;

	/// <summary>Requested FINRA dataset schema version.</summary>
	[Display(
		Name = "Data version",
		Description = "Value sent in the FINRA data-version request header.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public int DataVersion { get; set; } = 1;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = "FINRA Query API base address.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 10)]
	public Uri Address { get; set; } =
		new("https://api.finra.org/");

	/// <summary>FINRA Identity Platform OAuth token address.</summary>
	[Display(
		Name = "OAuth address",
		Description = "FINRA Identity Platform client-credentials token endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 11)]
	public Uri AuthAddress { get; set; } = new(
		"https://ews.fip.finra.org/fip/rest/ews/oauth2/access_token?grant_type=client_credentials");

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Token), Token)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(DataSet), DataSet)
			.Set(nameof(WeeklyTierIdentifier), WeeklyTierIdentifier)
			.Set(nameof(WeeklySummaryTypeCode), WeeklySummaryTypeCode)
			.Set(nameof(PageSize), PageSize)
			.Set(nameof(MaxRecords), MaxRecords)
			.Set(nameof(DataVersion), DataVersion)
			.Set(nameof(Address), Address)
			.Set(nameof(AuthAddress), AuthAddress);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Token = storage.GetValue<SecureString>(nameof(Token));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		DataSet = storage.GetValue(nameof(DataSet), DataSet);
		WeeklyTierIdentifier = storage.GetValue(
			nameof(WeeklyTierIdentifier), WeeklyTierIdentifier);
		WeeklySummaryTypeCode = storage.GetValue(
			nameof(WeeklySummaryTypeCode), WeeklySummaryTypeCode);
		PageSize = storage.GetValue(nameof(PageSize), PageSize);
		MaxRecords = storage.GetValue(nameof(MaxRecords), MaxRecords);
		DataVersion = storage.GetValue(
			nameof(DataVersion), DataVersion);
		Address = storage.GetValue(nameof(Address), Address);
		AuthAddress = storage.GetValue(
			nameof(AuthAddress), AuthAddress);
	}
}
