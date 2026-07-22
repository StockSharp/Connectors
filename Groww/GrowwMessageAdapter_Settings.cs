namespace StockSharp.Groww;

/// <summary>The message adapter for Groww Trading API.</summary>
[MediaIcon(Media.MediaNames.groww)]
[Doc("topics/api/connectors/stock_market/groww.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GrowwKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
[OrderCondition(typeof(GrowwOrderCondition))]
public partial class GrowwMessageAdapter : MessageAdapter, IKeySecretAdapter, ITokenAdapter
{
	/// <summary>Daily access token generated in Groww settings.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.GrowwAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Groww API key or TOTP token.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.GrowwApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Groww API secret used by the approval flow.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.GrowwApiSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Base32 secret used by the TOTP flow.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.GrowwTotpSecretKey,
		Description = LocalizedStrings.GrowwTotpSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public SecureString TotpSecret { get; set; }

	/// <summary>Default order product.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ProductKey,
		Description = LocalizedStrings.ProductKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey, Order = 4)]
	public GrowwProducts DefaultProduct { get; set; } = GrowwProducts.Delivery;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(TotpSecret), TotpSecret)
			.Set(nameof(DefaultProduct), DefaultProduct);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		TotpSecret = storage.GetValue<SecureString>(nameof(TotpSecret));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
	}
}
