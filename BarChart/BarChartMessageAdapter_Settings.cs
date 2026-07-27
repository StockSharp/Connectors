namespace StockSharp.BarChart;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security;

using Ecng.Common;
using Ecng.ComponentModel;
using Ecng.Serialization;

using StockSharp.Localization;
using StockSharp.Messages;

/// <summary>
/// The message adapter for <see cref="BarChart"/>.
/// </summary>
[MediaIcon(Media.MediaNames.barchart)]
[Doc("topics/api/connectors/stock_market/barchart.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BarChartKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX | MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles)]
public partial class BarChartMessageAdapter : HistoricalMessageAdapter, ITokenAdapter
{
	private const string _defaultRestEndpoint = "https://ondemand.websol.barchart.com";

	private static readonly HashSet<TimeSpan> _timeFrames = new(
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	]);

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Token), Token);
		storage.SetValue(nameof(RestEndpoint), RestEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Token = storage.GetValue<SecureString>(nameof(Token));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Token + " = " + Token.ToId();
	}
}
