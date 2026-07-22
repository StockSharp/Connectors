namespace StockSharp.MiraeSharekhan;

/// <summary>The message adapter for Mirae Asset Sharekhan Trading API.</summary>
[MediaIcon(Media.MediaNames.sharekhan)]
[Doc("topics/api/connectors/stock_market/mirae_asset_sharekhan.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MiraeSharekhanKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options | MessageAdapterCategories.FX)]
[OrderCondition(typeof(MiraeSharekhanOrderCondition))]
public partial class MiraeSharekhanMessageAdapter : MessageAdapter, ITokenAdapter
{
	/// <summary>Trading API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MiraeSharekhanApiKeyKey,
		Description = LocalizedStrings.MiraeSharekhanApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string ApiKey { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccessTokenKey,
		Description = LocalizedStrings.MiraeSharekhanAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Optional vendor key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MiraeSharekhanVendorKeyKey,
		Description = LocalizedStrings.MiraeSharekhanVendorKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public string VendorKey { get; set; }

	/// <summary>Trading customer identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MiraeSharekhanCustomerIdKey,
		Description = LocalizedStrings.MiraeSharekhanCustomerIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string CustomerId { get; set; }

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MiraeSharekhanDefaultProductKey,
		Description = LocalizedStrings.MiraeSharekhanDefaultProductDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public MiraeSharekhanProducts DefaultProduct { get; set; } = MiraeSharekhanProducts.Investment;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(Token), Token)
			.Set(nameof(VendorKey), VendorKey)
			.Set(nameof(CustomerId), CustomerId)
			.Set(nameof(DefaultProduct), DefaultProduct);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiKey = storage.GetValue<string>(nameof(ApiKey));
		Token = storage.GetValue<SecureString>(nameof(Token));
		VendorKey = storage.GetValue<string>(nameof(VendorKey));
		CustomerId = storage.GetValue<string>(nameof(CustomerId));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
	}
}
