namespace StockSharp.Questrade;

/// <summary>The message adapter for Questrade API.</summary>
[MediaIcon(Media.MediaNames.questrade)]
[Doc("topics/api/connectors/stock_market/questrade.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QuestradeKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Stock | MessageAdapterCategories.Options)]
[OrderCondition(typeof(QuestradeOrderCondition))]
public partial class QuestradeMessageAdapter : MessageAdapter, ITokenAdapter
{
	private const string _defaultOAuthEndpoint = "https://login.questrade.com/oauth2/token";

	/// <summary>OAuth access token.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.QuestradeAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>OAuth refresh token.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.QuestradeRefreshTokenKey,
		Description = LocalizedStrings.QuestradeRefreshTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString RefreshToken { get; set; }

	/// <summary>API server returned by Questrade during token redemption.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.QuestradeApiServerKey,
		Description = LocalizedStrings.QuestradeApiServerDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public string ApiServer { get; set; }

	/// <summary>Optional account number.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.QuestradeAccountKey,
		Description = LocalizedStrings.QuestradeAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string Account { get; set; }

	/// <summary>OAuth token endpoint.</summary>
	[Display(
		Name = "OAuth endpoint",
		Description = "OAuth token endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string OAuthEndpoint { get; set; } = _defaultOAuthEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(RefreshToken), RefreshToken)
			.Set(nameof(ApiServer), ApiServer)
			.Set(nameof(Account), Account)
			.Set(nameof(OAuthEndpoint), OAuthEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		RefreshToken = storage.GetValue<SecureString>(nameof(RefreshToken));
		ApiServer = storage.GetValue<string>(nameof(ApiServer));
		Account = storage.GetValue<string>(nameof(Account));
		OAuthEndpoint = storage.GetValue(nameof(OAuthEndpoint), OAuthEndpoint);
	}
}
