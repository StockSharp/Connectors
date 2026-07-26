namespace StockSharp.MoexISS;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;
using Ecng.Serialization;

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
	private string _restEndpoint = "https://iss.moex.com/iss/";

	/// <summary>REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint
	{
		get => _restEndpoint;
		set
		{
			_restEndpoint = value.ThrowIfEmpty(nameof(value)).TrimEnd('/') + "/";
			_client.BaseAddress = new(_restEndpoint);
		}
	}

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(60),
	];

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage.Set(nameof(RestEndpoint), RestEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
	}
}
