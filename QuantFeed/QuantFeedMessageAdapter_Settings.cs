namespace StockSharp.QuantFeed;

/// <summary>The message adapter for QuantHouse Historical On-Demand CSV files.</summary>
[MediaIcon(Media.MediaNames.quanthouse)]
[Doc("topics/api/connectors/stock_market/quantfeed.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QuantFeedKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.Paid |
	MessageAdapterCategories.History | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX | MessageAdapterCategories.Commodities |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth)]
public partial class QuantFeedMessageAdapter : MessageAdapter
{
	/// <summary>Directory containing entitled QuantHouse CSV delivery files.</summary>
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
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DefaultTimeZoneKey,
		Description = LocalizedStrings.SystemTimeZoneIdentifierAppliedOnlyWhenACsvTimestampHasNoUtcOffsetDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 1)]
	public string DefaultTimeZoneId { get; set; } = "UTC";

	/// <summary>Whether nested delivery directories are scanned.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RecursiveScanKey,
		Description = LocalizedStrings.ScanNestedDeliveryDirectoriesWithoutFollowingReparsePointsDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 2)]
	public bool IsRecursive { get; set; } = true;

	/// <inheritdoc />
	public override async Task SaveAsync(SettingsStorage storage, CancellationToken cancellationToken)
	{
		await base.SaveAsync(storage, cancellationToken);
		storage
			.Set(nameof(DataDirectory), DataDirectory)
			.Set(nameof(DefaultTimeZoneId), DefaultTimeZoneId)
			.Set(nameof(IsRecursive), IsRecursive);
	}

	/// <inheritdoc />
	public override async Task LoadAsync(SettingsStorage storage, CancellationToken cancellationToken)
	{
		await base.LoadAsync(storage, cancellationToken);
		DataDirectory = storage.GetValue<string>(nameof(DataDirectory));
		DefaultTimeZoneId = storage.GetValue(nameof(DefaultTimeZoneId), DefaultTimeZoneId);
		IsRecursive = storage.GetValue(nameof(IsRecursive), IsRecursive);
	}
}
