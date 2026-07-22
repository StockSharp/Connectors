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
		Name = "One API host",
		Description = "ACTIV One API gateway host assigned to the account.",
		GroupName = "Connection",
		Order = 2)]
	[BasicSetting]
	public string Host { get; set; } = "aop-ny4-replay.activfinancial.com";

	/// <summary>ACTIV data source.</summary>
	[Display(
		Name = "Data source",
		Description = "Entitled ACTIV One API data source.",
		GroupName = "Market data",
		Order = 3)]
	[BasicSetting]
	public ActivDataSources DataSource { get; set; } = ActivDataSources.Activ;

	/// <summary>Symbol namespace used for canonical requests.</summary>
	[Display(
		Name = "Symbology",
		Description = "Symbol namespace used for canonical One API requests.",
		GroupName = "Market data",
		Order = 4)]
	public ActivSymbologies Symbology { get; set; } = ActivSymbologies.Native;

	/// <summary>Node.js executable path.</summary>
	[Display(
		Name = "Node.js path",
		Description = "Path or command name of the Node.js executable.",
		GroupName = "Gateway",
		Order = 5)]
	[BasicSetting]
	public string NodePath { get; set; } = "node";

	/// <summary>Directory containing the typed gateway and its installed npm dependencies.</summary>
	[Display(
		Name = "Gateway directory",
		Description = "Directory containing activ_gateway.cjs, package.json, and node_modules.",
		GroupName = "Gateway",
		Order = 6)]
	[BasicSetting]
	public string GatewayDirectory { get; set; } =
		Path.Combine(AppContext.BaseDirectory, "ActivFinancialGateway");

	/// <summary>Fallback time zone for records whose topic has no Olson time-zone field.</summary>
	[Display(
		Name = "Fallback time zone",
		Description = "IANA or system time-zone identifier used only when ACTIV omits topic time-zone metadata.",
		GroupName = "Market data",
		Order = 7)]
	public string FallbackTimeZoneId { get; set; } = "UTC";

	/// <summary>Maximum records returned by a lookup with no smaller requested count.</summary>
	[Display(
		Name = "Lookup limit",
		Description = "Maximum number of query-snapshot records returned by one security lookup.",
		GroupName = "Limits",
		Order = 8)]
	public int MaxLookupResults { get; set; } = 1000;

	/// <summary>Maximum records returned by one TSS history request.</summary>
	[Display(
		Name = "History limit",
		Description = "Maximum number of tick or candle records returned by one TSS request.",
		GroupName = "Limits",
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
