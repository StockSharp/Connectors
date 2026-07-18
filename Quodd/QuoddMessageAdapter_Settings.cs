namespace StockSharp.Quodd;

/// <summary>The message adapter for QUODD market data APIs.</summary>
[MediaIcon(Media.MediaNames.quodd)]
[Doc("topics/api/connectors/stock_market/quodd.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.QuoddKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Paid |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Options | MessageAdapterCategories.Level1)]
public partial class QuoddMessageAdapter : MessageAdapter, ITokenAdapter, ILoginPasswordAdapter,
	IAddressAdapter<Uri>
{
	/// <summary>Authentication mode.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AuthorizationKey,
		Description = LocalizedStrings.AuthorizationKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public QuoddAuthenticationModes AuthenticationMode { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Firm login used for HTTP Basic authentication.</summary>
	[Display(Name = "Firm login",
		Description = "Firm login used in the Basic authorization header.",
		GroupName = "Connection", Order = 4)]
	public string FirmLogin { get; set; }

	/// <summary>Firm password used for HTTP Basic authentication.</summary>
	[Display(Name = "Firm password",
		Description = "Firm password used in the Basic authorization header.",
		GroupName = "Connection", Order = 5)]
	public SecureString FirmPassword { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api.quodd.com");

	/// <summary>QUODD JWT service address.</summary>
	[Display(Name = "Authentication address",
		Description = "QUODD JWT service base address.",
		GroupName = "Connection", Order = 7)]
	public Uri AuthenticationAddress { get; set; } = new("https://vor.quodd.com");

	/// <summary>Ticker used to validate market-data entitlement when connecting.</summary>
	[Display(Name = "Validation ticker",
		Description = "Equity ticker used for the initial authenticated snapshot request.",
		GroupName = "Connection", Order = 8)]
	public string ValidationTicker { get; set; } = "MSFT";

	/// <summary>Whether separately entitled Ticker Info is requested during security lookup.</summary>
	[Display(Name = "Ticker Info",
		Description = "Request the separately entitled Ticker Info service during security lookup.",
		GroupName = "Market data", Order = 9)]
	public bool IsTickerInfoEnabled { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AuthenticationMode), AuthenticationMode)
			.Set(nameof(Token), Token)
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(FirmLogin), FirmLogin)
			.Set(nameof(FirmPassword), FirmPassword)
			.Set(nameof(Address), Address)
			.Set(nameof(AuthenticationAddress), AuthenticationAddress)
			.Set(nameof(ValidationTicker), ValidationTicker)
			.Set(nameof(IsTickerInfoEnabled), IsTickerInfoEnabled);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AuthenticationMode = storage.GetValue(nameof(AuthenticationMode), AuthenticationMode);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		FirmLogin = storage.GetValue<string>(nameof(FirmLogin));
		FirmPassword = storage.GetValue<SecureString>(nameof(FirmPassword));
		Address = storage.GetValue(nameof(Address), Address);
		AuthenticationAddress = storage.GetValue(nameof(AuthenticationAddress), AuthenticationAddress);
		ValidationTicker = storage.GetValue(nameof(ValidationTicker), ValidationTicker);
		IsTickerInfoEnabled = storage.GetValue(nameof(IsTickerInfoEnabled), IsTickerInfoEnabled);
	}
}
