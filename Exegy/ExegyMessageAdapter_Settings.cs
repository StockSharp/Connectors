namespace StockSharp.Exegy;

/// <summary>The message adapter for Exegy normalized historical CSV files.</summary>
[MediaIcon(Media.MediaNames.exegy)]
[Doc("topics/api/connectors/stock_market/exegy.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ExegyKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.Paid |
	MessageAdapterCategories.History | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX | MessageAdapterCategories.Commodities |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.OrderLog)]
public partial class ExegyMessageAdapter : MessageAdapter
{
	/// <summary>Directory containing entitled Exegy normalized CSV files.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DataDirectoryKey,
		Description = LocalizedStrings.DataDirectoryKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string DataDirectory { get; set; }

	/// <summary>Time zone used only for source timestamps without an offset.</summary>
	[Display(
		Name = "Default time zone",
		Description = "System time-zone identifier applied only when a CSV timestamp has no UTC offset.",
		GroupName = "Market data",
		Order = 1)]
	public string DefaultTimeZoneId { get; set; } = "UTC";

	/// <summary>Whether nested delivery directories are scanned.</summary>
	[Display(
		Name = "Recursive scan",
		Description = "Scan nested delivery directories without following reparse points.",
		GroupName = "Market data",
		Order = 2)]
	public bool IsRecursive { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(DataDirectory), DataDirectory)
			.Set(nameof(DefaultTimeZoneId), DefaultTimeZoneId)
			.Set(nameof(IsRecursive), IsRecursive);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		DataDirectory = storage.GetValue<string>(nameof(DataDirectory));
		DefaultTimeZoneId = storage.GetValue(nameof(DefaultTimeZoneId), DefaultTimeZoneId);
		IsRecursive = storage.GetValue(nameof(IsRecursive), IsRecursive);
	}
}
