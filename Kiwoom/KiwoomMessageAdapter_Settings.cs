namespace StockSharp.Kiwoom;

/// <summary>The message adapter for the Kiwoom REST API.</summary>
[MediaIcon(Media.MediaNames.kiwoom)]
[Doc("topics/api/connectors/stock_market/kiwoom.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KiwoomKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.KoreaExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History | MessageAdapterCategories.Stock)]
[OrderCondition(typeof(KiwoomOrderCondition))]
public partial class KiwoomMessageAdapter : MessageAdapter, IDemoAdapter
{
	/// <summary>Application key issued by the Kiwoom REST API portal.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KiwoomAppKeyKey,
		Description = LocalizedStrings.KiwoomAppKeyDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString AppKey { get; set; }

	/// <summary>Application secret issued by the Kiwoom REST API portal.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KiwoomAppSecretKey,
		Description = LocalizedStrings.KiwoomAppSecretDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString AppSecret { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AppKey), AppKey)
			.Set(nameof(AppSecret), AppSecret)
			.Set(nameof(IsDemo), IsDemo);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AppKey = storage.GetValue<SecureString>(nameof(AppKey));
		AppSecret = storage.GetValue<SecureString>(nameof(AppSecret));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
	}
}
