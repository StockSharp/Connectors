namespace StockSharp.AlphaVantage;

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
/// The message adapter for <see cref="AlphaVantage"/>.
/// </summary>
[MediaIcon(Media.MediaNames.alphavantage)]
[Doc("topics/api/connectors/stock_market/alphavantage.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AlphaVantageKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX | MessageAdapterCategories.Candles | MessageAdapterCategories.Free)]
public partial class AlphaVantageMessageAdapter : HistoricalMessageAdapter, ITokenAdapter
{
	private const string _defaultQueryEndpoint = "https://www.alphavantage.co/query";
	private const string _defaultCurrencyListEndpoint = "https://www.alphavantage.co";

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromTicks(TimeHelper.TicksPerMonth),
	];

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Query API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.QueryEndpointKey,
		Description = LocalizedStrings.QueryApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string QueryEndpoint { get; set; } = _defaultQueryEndpoint;

	/// <summary>Currency list endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CurrencyListEndpointKey,
		Description = LocalizedStrings.BaseEndpointForCurrencyListFilesDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string CurrencyListEndpoint { get; set; } = _defaultCurrencyListEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Token), Token);
		storage.SetValue(nameof(QueryEndpoint), QueryEndpoint);
		storage.SetValue(nameof(CurrencyListEndpoint), CurrencyListEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Token = storage.GetValue<SecureString>(nameof(Token));
		QueryEndpoint = storage.GetValue(nameof(QueryEndpoint), QueryEndpoint);
		CurrencyListEndpoint = storage.GetValue(nameof(CurrencyListEndpoint), CurrencyListEndpoint);
	}
}
