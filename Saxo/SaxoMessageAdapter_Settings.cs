namespace StockSharp.Saxo;

/// <summary>The message adapter for Saxo OpenAPI.</summary>
[MediaIcon(Media.MediaNames.saxo)]
[Doc("topics/api/connectors/stock_market/saxo.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SaxoKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX)]
[OrderCondition(typeof(SaxoOrderCondition))]
public partial class SaxoMessageAdapter : MessageAdapter, IKeySecretAdapter, ITokenAdapter
{
	/// <summary>OAuth access token.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.SaxoAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>OAuth refresh token.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SaxoRefreshTokenKey,
		Description = LocalizedStrings.SaxoRefreshTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public SecureString RefreshToken { get; set; }

	/// <summary>OAuth application key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.SaxoClientIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public SecureString Key { get; set; }

	/// <summary>OAuth application secret.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SaxoClientSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public SecureString Secret { get; set; }

	/// <summary>OAuth redirect URI registered for the application.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SaxoRedirectUriKey,
		Description = LocalizedStrings.SaxoRedirectUriDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string RedirectUri { get; set; }

	/// <summary>Optional default account key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SaxoAccountKeyKey,
		Description = LocalizedStrings.SaxoAccountKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public string AccountKey { get; set; }

	/// <summary>Saxo environment.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SaxoEnvironmentKey,
		Description = LocalizedStrings.SaxoEnvironmentDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public SaxoEnvironments Environment { get; set; } = SaxoEnvironments.Simulation;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(RefreshToken), RefreshToken)
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(RedirectUri), RedirectUri)
			.Set(nameof(AccountKey), AccountKey)
			.Set(nameof(Environment), Environment);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		RefreshToken = storage.GetValue<SecureString>(nameof(RefreshToken));
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		RedirectUri = storage.GetValue<string>(nameof(RedirectUri));
		AccountKey = storage.GetValue<string>(nameof(AccountKey));
		Environment = storage.GetValue(nameof(Environment), Environment);
	}
}
