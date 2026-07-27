namespace StockSharp.OptionMetrics;

/// <summary>The message adapter for OptionMetrics IvyDB US files.</summary>
[MediaIcon(Media.MediaNames.optionmetrics)]
[Doc("topics/api/connectors/stock_market/optionmetrics.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OptionMetricsKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Paid |
	MessageAdapterCategories.History | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Options | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles)]
public partial class OptionMetricsMessageAdapter : MessageAdapter
{
	/// <summary>Directory containing licensed IvyDB US text files or ZIP archives.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DataDirectoryKey,
		Description = LocalizedStrings.DataDirectoryKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string DataDirectory { get; set; }

	/// <summary>Price adjustment used for underlying-security history.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceAdjustmentKey,
		Description = LocalizedStrings.SelectRawSplitAdjustedOrTotalReturnAdjustedUnderlyingPricesDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 1)]
	public IvyDbPriceAdjustments PriceAdjustment { get; set; } =
		IvyDbPriceAdjustments.SplitAdjusted;

	/// <summary>Time zone used to interpret IvyDB US trade dates.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketTimeZoneKey,
		Description = LocalizedStrings.SystemTimeZoneIdentifierForUsEasternMarketDatesDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 2)]
	public string MarketTimeZoneId { get; set; } = "America/New_York";

	/// <summary>Start of the daily underlying-security candle session.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SessionStartKey,
		Description = LocalizedStrings.OpenTimeAssignedToDailyUnderlyingSecurityCandlesDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 3)]
	public TimeSpan SessionStart { get; set; } = new(9, 30, 0);

	/// <summary>Time assigned to daily option observations.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OptionSnapshotTimeKey,
		Description = LocalizedStrings.UsEasternTimeAssignedToIvyDBEndOfDayOptionObservationsDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 4)]
	public TimeSpan OptionSnapshotTime { get; set; } = new(15, 59, 0);

	/// <summary>End of the daily underlying-security candle session.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SessionEndKey,
		Description = LocalizedStrings.CloseTimeAssignedToDailyUnderlyingSecurityObservationsDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 5)]
	public TimeSpan SessionEnd { get; set; } = new(16, 0, 0);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(DataDirectory), DataDirectory)
			.Set(nameof(PriceAdjustment), PriceAdjustment)
			.Set(nameof(MarketTimeZoneId), MarketTimeZoneId)
			.Set(nameof(SessionStart), SessionStart)
			.Set(nameof(OptionSnapshotTime), OptionSnapshotTime)
			.Set(nameof(SessionEnd), SessionEnd);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		DataDirectory = storage.GetValue<string>(nameof(DataDirectory));
		PriceAdjustment = storage.GetValue(nameof(PriceAdjustment), PriceAdjustment);
		MarketTimeZoneId = storage.GetValue(nameof(MarketTimeZoneId), MarketTimeZoneId);
		SessionStart = storage.GetValue(nameof(SessionStart), SessionStart);
		OptionSnapshotTime = storage.GetValue(nameof(OptionSnapshotTime), OptionSnapshotTime);
		SessionEnd = storage.GetValue(nameof(SessionEnd), SessionEnd);
	}
}
