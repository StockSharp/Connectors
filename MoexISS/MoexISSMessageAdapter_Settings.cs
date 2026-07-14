namespace StockSharp.MoexISS;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;

using StockSharp.Localization;

/// <summary>
/// The message adapter for <see cref="MoexISS"/>.
/// </summary>
[MediaIcon(Media.MediaNames.moex)]
[Doc("topics/api/connectors/russia/moexiss.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MoexISSKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.OrderLog)]
public partial class MoexISSMessageAdapter : HistoricalMessageAdapter
{
	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(60),
	];
}