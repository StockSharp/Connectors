namespace StockSharp.TradeStation;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for TradeStation API v3.
/// </summary>
[MediaIcon(Media.MediaNames.tradestation)]
[Doc("topics/api/connectors/stock_market/tradestation.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TradeStationKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Candles | MessageAdapterCategories.Options |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Level1)]
[OrderCondition(typeof(TradeStationOrderCondition))]
public partial class TradeStationMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey, Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey, Description = LocalizedStrings.DemoModeKey, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>
	/// Default smart-order route.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DefaultRouteKey, Description = LocalizedStrings.DefaultRouteKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public string DefaultRoute { get; set; } = "Intelligent";

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(DefaultRoute), DefaultRoute);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		DefaultRoute = storage.GetValue(nameof(DefaultRoute), DefaultRoute);
	}
}
