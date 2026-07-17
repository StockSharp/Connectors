namespace StockSharp.Marquee;

/// <summary>The message adapter for Goldman Sachs Marquee APIs.</summary>
[MediaIcon(Media.MediaNames.goldmansachs)]
[Doc("topics/api/connectors/stock_market/marquee.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarqueeKey,
	Description = LocalizedStrings.MarketDataConnectorKey, GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Paid)]
public partial class MarqueeMessageAdapter : MessageAdapter, IDemoAdapter
{
	/// <summary>OAuth application client identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ClientIdKey,
		Description = LocalizedStrings.ClientIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string ClientId { get; set; }

	/// <summary>OAuth application client secret.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString ClientSecret { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoModeKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(ClientSecret), ClientSecret)
			.Set(nameof(IsDemo), IsDemo);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ClientId = storage.GetValue<string>(nameof(ClientId));
		ClientSecret = storage.GetValue<SecureString>(nameof(ClientSecret));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
	}
}
