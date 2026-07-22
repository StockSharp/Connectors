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
			.Set(nameof(PriceAdjustment), PriceAdjustment);
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
	}
}
