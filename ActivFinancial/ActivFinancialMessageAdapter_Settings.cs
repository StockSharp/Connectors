namespace StockSharp.ActivFinancial;

/// <summary>The message adapter for ACTIV Financial One API market data.</summary>
[MediaIcon(Media.MediaNames.activfinancial)]
[Doc("topics/api/connectors/stock_market/activ_financial.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ActivFinancialKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Paid |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Options |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
public partial class ActivFinancialMessageAdapter : MessageAdapter, ILoginPasswordAdapter
{
	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>One API gateway host assigned by ACTIV Financial.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OneApiHostKey,
		Description = LocalizedStrings.ActivOneApiGatewayHostAssignedToTheAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string Host { get; set; } = "aop-ny4-replay.activfinancial.com";

	/// <summary>ACTIV data source.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DataSourceKey,
		Description = LocalizedStrings.EntitledActivOneApiDataSourceDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 3)]
	[BasicSetting]
	public ActivDataSources DataSource { get; set; } = ActivDataSources.Activ;

	/// <summary>Symbol namespace used for canonical requests.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SymbologyKey,
		Description = LocalizedStrings.SymbolNamespaceUsedForCanonicalOneApiRequestsDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 4)]
	public ActivSymbologies Symbology { get; set; } = ActivSymbologies.Native;

	/// <summary>Node.js executable path.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NodeJsPathKey,
		Description = LocalizedStrings.PathOrCommandNameOfTheNodeJsExecutableDescKey,
		GroupName = LocalizedStrings.GatewayKey,
		Order = 5)]
	[BasicSetting]
	public string NodePath { get; set; } = "node";

	/// <summary>Directory containing the typed gateway and its installed npm dependencies.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.GatewayDirectoryKey,
		Description = LocalizedStrings.DirectoryContainingActivGatewayCjsPackageJsonAndNodeModulesDescKey,
		GroupName = LocalizedStrings.GatewayKey,
		Order = 6)]
	[BasicSetting]
	public string GatewayDirectory { get; set; } =
		Path.Combine(AppContext.BaseDirectory, "ActivFinancialGateway");

	/// <summary>Fallback time zone for records whose topic has no Olson time-zone field.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FallbackTimeZoneKey,
		Description = LocalizedStrings.IanaOrSystemTimeZoneIdentifierUsedOnlyWhenActivOmitsTopicTimeZoneMetadataDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 7)]
	public string FallbackTimeZoneId { get; set; } = "UTC";

	/// <summary>Maximum records returned by a lookup with no smaller requested count.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LookupLimitKey,
		Description = LocalizedStrings.MaximumNumberOfQuerySnapshotRecordsReturnedByOneSecurityLookupDescKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 8)]
	public int MaxLookupResults { get; set; } = 1000;

	/// <summary>Maximum records returned by one TSS history request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.HistoryLimitKey,
		Description = LocalizedStrings.MaximumNumberOfTickOrCandleRecordsReturnedByOneTssRequestDescKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 9)]
	public int MaxHistoryResults { get; set; } = 10000;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(Host), Host)
			.Set(nameof(DataSource), DataSource)
			.Set(nameof(Symbology), Symbology)
			.Set(nameof(NodePath), NodePath)
			.Set(nameof(GatewayDirectory), GatewayDirectory)
			.Set(nameof(FallbackTimeZoneId), FallbackTimeZoneId)
			.Set(nameof(MaxLookupResults), MaxLookupResults)
			.Set(nameof(MaxHistoryResults), MaxHistoryResults);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Host = storage.GetValue(nameof(Host), Host);
		DataSource = storage.GetValue(nameof(DataSource), DataSource);
		Symbology = storage.GetValue(nameof(Symbology), Symbology);
		NodePath = storage.GetValue(nameof(NodePath), NodePath);
		GatewayDirectory = storage.GetValue(nameof(GatewayDirectory), GatewayDirectory);
		FallbackTimeZoneId = storage.GetValue(nameof(FallbackTimeZoneId), FallbackTimeZoneId);
		MaxLookupResults = storage.GetValue(nameof(MaxLookupResults), MaxLookupResults);
		MaxHistoryResults = storage.GetValue(nameof(MaxHistoryResults), MaxHistoryResults);
	}
}
