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
public partial class GrowwMessageAdapter : MessageAdapter
{
	/// <summary>Daily access token generated in Groww settings.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.GrowwAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString AccessToken { get; set; }

	/// <summary>Groww API key or TOTP token.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.GrowwApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString ApiKey { get; set; }

	/// <summary>Groww API secret used by the approval flow.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.GrowwApiSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString ApiSecret { get; set; }

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
			.Set(nameof(AccessToken), AccessToken)
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(ApiSecret), ApiSecret)
			.Set(nameof(TotpSecret), TotpSecret)
			.Set(nameof(DefaultProduct), DefaultProduct);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AccessToken = storage.GetValue<SecureString>(nameof(AccessToken));
		ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
		ApiSecret = storage.GetValue<SecureString>(nameof(ApiSecret));
		TotpSecret = storage.GetValue<SecureString>(nameof(TotpSecret));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
	}
}
