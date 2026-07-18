namespace StockSharp.AlgoSeek;

/// <summary>The message adapter for licensed AlgoSeek historical delivery files.</summary>
[MediaIcon(Media.MediaNames.algoseek)]
[Doc("topics/api/connectors/stock_market/algoseek.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AlgoSeekKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Paid |
	MessageAdapterCategories.History | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Options | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles)]
public partial class AlgoSeekMessageAdapter : MessageAdapter
{
	/// <summary>Directory containing licensed AlgoSeek CSV delivery files.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DataDirectoryKey,
		Description = LocalizedStrings.DataDirectoryKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string DataDirectory { get; set; }

	/// <summary>Time zone used by US equities and OPRA options files.</summary>
	[Display(Name = "Market time zone",
		Description = "System time-zone identifier used for US equity and option timestamps.",
		GroupName = "Market data", Order = 1)]
	public string MarketTimeZoneId { get; set; } = "America/New_York";

	/// <summary>Whether nested delivery directories are scanned.</summary>
	[Display(Name = "Recursive scan",
		Description = "Scan nested delivery directories without following reparse points.",
		GroupName = "Market data", Order = 2)]
	public bool IsRecursive { get; set; } = true;

	/// <summary>Whether only consolidated NBBO equity quotes are published.</summary>
	[Display(Name = "NBBO quotes only",
		Description = "Ignore venue BBO rows and publish consolidated equity NBBO rows only.",
		GroupName = "Market data", Order = 3)]
	public bool IsNationalBestQuotesOnly { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(DataDirectory), DataDirectory)
			.Set(nameof(MarketTimeZoneId), MarketTimeZoneId)
			.Set(nameof(IsRecursive), IsRecursive)
			.Set(nameof(IsNationalBestQuotesOnly), IsNationalBestQuotesOnly);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		DataDirectory = storage.GetValue<string>(nameof(DataDirectory));
		MarketTimeZoneId = storage.GetValue(nameof(MarketTimeZoneId), MarketTimeZoneId);
		IsRecursive = storage.GetValue(nameof(IsRecursive), IsRecursive);
		IsNationalBestQuotesOnly = storage.GetValue(nameof(IsNationalBestQuotesOnly),
			IsNationalBestQuotesOnly);
	}
}
