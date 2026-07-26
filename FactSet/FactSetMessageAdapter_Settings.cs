namespace StockSharp.FactSet;

/// <summary>The message adapter for FactSet Prices API.</summary>
[MediaIcon(Media.MediaNames.factset)]
[Doc("topics/api/connectors/stock_market/factset.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FactSetKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Paid)]
public partial class FactSetMessageAdapter : MessageAdapter, ILoginPasswordAdapter
{
	private const string _defaultRestEndpoint = "https://api.factset.com/content/";
	private const string _defaultOAuthDiscoveryEndpoint = "https://auth.factset.com/.well-known/openid-configuration";

	/// <summary>Authentication scheme.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AuthorizationKey,
		Description = LocalizedStrings.AuthorizationKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public FactSetAuthenticationModes AuthenticationMode { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Path to the OAuth application configuration downloaded from FactSet Developer Portal.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FileKey,
		Description = LocalizedStrings.PathKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string OAuthConfigFile { get; set; }

	/// <summary>Optional ISO currency override. Empty uses each security's local currency.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CurrencyKey,
		Description = LocalizedStrings.CurrencyKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 4)]
	public string Currency { get; set; }

	/// <summary>Equity price adjustment mode.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ModeKey,
		Description = LocalizedStrings.ModeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 5)]
	public FactSetPriceAdjustments PriceAdjustment { get; set; }

	/// <summary>REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>OAuth discovery endpoint.</summary>
	[Display(
		Name = "OAuth discovery endpoint",
		Description = "OAuth OpenID discovery endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string OAuthDiscoveryEndpoint { get; set; } = _defaultOAuthDiscoveryEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AuthenticationMode), AuthenticationMode)
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(OAuthConfigFile), OAuthConfigFile)
			.Set(nameof(Currency), Currency)
			.Set(nameof(PriceAdjustment), PriceAdjustment)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(OAuthDiscoveryEndpoint), OAuthDiscoveryEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AuthenticationMode = storage.GetValue(nameof(AuthenticationMode), AuthenticationMode);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		OAuthConfigFile = storage.GetValue<string>(nameof(OAuthConfigFile));
		Currency = storage.GetValue<string>(nameof(Currency));
		PriceAdjustment = storage.GetValue(nameof(PriceAdjustment), PriceAdjustment);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		OAuthDiscoveryEndpoint = storage.GetValue(nameof(OAuthDiscoveryEndpoint), OAuthDiscoveryEndpoint);
	}
}
