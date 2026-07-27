namespace StockSharp.Tinkoff;

using System.ComponentModel.DataAnnotations;
using System.Security;

using Ecng.ComponentModel;
using Ecng.Serialization;

/// <summary>
/// The message adapter for <see cref="Tinkoff"/>.
/// </summary>
[MediaIcon(Media.MediaNames.tinkoff)]
[Doc("topics/api/connectors/russia/tinkoff.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TinkoffKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Russia | MessageAdapterCategories.Transactions | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Free | MessageAdapterCategories.Ticks)]
[OrderCondition(typeof(TinkoffOrderCondition))]
public partial class TinkoffMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	private const string _defaultEndpoint = "https://invest-public-api.tbank.ru";
	private const string _defaultDemoEndpoint = "https://sandbox-invest-public-api.tbank.ru";
	private const string _defaultHistoryEndpoint = "https://invest-public-api.tbank.ru";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>Production API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ApiEndpointKey,
		Description = LocalizedStrings.ProductionGRPCApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string Endpoint { get; set; } = _defaultEndpoint;

	/// <summary>Demo API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoApiEndpointKey,
		Description = LocalizedStrings.DemoGRPCApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string DemoEndpoint { get; set; } = _defaultDemoEndpoint;

	/// <summary>Historical data endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.HistoryEndpointKey,
		Description = LocalizedStrings.HistoricalDataEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	public string HistoryEndpoint { get; set; } = _defaultHistoryEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Token), Token)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(Endpoint), Endpoint)
			.Set(nameof(DemoEndpoint), DemoEndpoint)
			.Set(nameof(HistoryEndpoint), HistoryEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Token = storage.GetValue<SecureString>(nameof(Token));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		Endpoint = storage.GetValue(nameof(Endpoint), Endpoint);
		DemoEndpoint = storage.GetValue(nameof(DemoEndpoint), DemoEndpoint);
		HistoryEndpoint = storage.GetValue(nameof(HistoryEndpoint), HistoryEndpoint);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Token.ToId();
	}
}
