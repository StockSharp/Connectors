namespace StockSharp.CboeDataShop;

/// <summary>Cboe All Access data environment.</summary>
public enum CboeDataModes
{
	/// <summary>Live entitled data.</summary>
	Live,

	/// <summary>Delayed entitled data.</summary>
	Delayed,
}

/// <summary>The message adapter for Cboe DataShop and LiveVol All Access API.</summary>
[MediaIcon(Media.MediaNames.cboedatashop)]
[Doc("topics/api/connectors/stock_market/cboe_datashop.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CboeDataShopKey,
	Description = LocalizedStrings.MarketDataConnectorKey, GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Options |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Paid)]
public partial class CboeDataShopMessageAdapter : MessageAdapter, ILoginPasswordAdapter, IAddressAdapter<Uri>
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>All Access API base address.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api.livevol.com/v1/");

	/// <summary>OAuth token endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AuthorizationKey,
		Description = LocalizedStrings.AuthorizationKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public Uri TokenAddress { get; set; } = new("https://id.livevol.com/connect/token");

	/// <summary>Live or delayed All Access data environment.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ModeKey,
		Description = LocalizedStrings.ModeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 4)]
	[BasicSetting]
	public CboeDataModes DataMode { get; set; } = CboeDataModes.Delayed;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(Address), Address)
			.Set(nameof(TokenAddress), TokenAddress)
			.Set(nameof(DataMode), DataMode);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Address = storage.GetValue(nameof(Address), Address);
		TokenAddress = storage.GetValue(nameof(TokenAddress), TokenAddress);
		DataMode = storage.GetValue(nameof(DataMode), DataMode);
	}
}
