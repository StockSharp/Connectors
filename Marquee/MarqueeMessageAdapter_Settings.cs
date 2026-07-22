namespace StockSharp.Marquee;

/// <summary>The message adapter for Goldman Sachs Marquee APIs.</summary>
[MediaIcon(Media.MediaNames.goldmansachs)]
[Doc("topics/api/connectors/stock_market/marquee.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MarqueeKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Paid)]
public partial class MarqueeMessageAdapter : MessageAdapter, IDemoAdapter, IKeySecretAdapter
{
	/// <summary>OAuth application client identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.ClientIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>OAuth application client secret.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(IsDemo), IsDemo);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
	}
}
