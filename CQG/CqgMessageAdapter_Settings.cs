namespace StockSharp.CQG;

/// <summary>The message adapter for CQG Web API.</summary>
[MediaIcon(Media.MediaNames.cqg)]
[Doc("topics/api/connectors/stock_market/cqg_web_api.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CqgWebApiKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
[OrderCondition(typeof(CqgOrderCondition))]
public partial class CqgMessageAdapter : MessageAdapter, ILoginPasswordAdapter, ITokenAdapter
{
	/// <summary>CQG username.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <summary>CQG password.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Optional CQG one-time password.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CqgOneTimePasswordKey,
		Description = LocalizedStrings.CqgOneTimePasswordDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	public SecureString OneTimePassword { get; set; }

	/// <summary>Optional CQG access token.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.CqgAccessTokenDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public SecureString Token { get; set; }

	/// <summary>CQG private label.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CqgPrivateLabelKey,
		Description = LocalizedStrings.CqgPrivateLabelDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public string PrivateLabel { get; set; } = "WebAPITest";

	/// <summary>CQG-assigned client application identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CqgClientIdKey,
		Description = LocalizedStrings.CqgClientIdDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public string ClientId { get; set; } = "WebAPITest";

	/// <summary>Client application version reported to CQG.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CqgClientVersionKey,
		Description = LocalizedStrings.CqgClientVersionDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public string ClientVersion { get; set; } = "StockSharp 5";

	/// <summary>CQG secure WebSocket endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CqgEndpointKey,
		Description = LocalizedStrings.CqgEndpointDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public string Endpoint { get; set; } = "wss://demoapi.cqg.com:443";

	/// <summary>Optional account number or account name filter.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CqgPortfolioKey,
		Description = LocalizedStrings.CqgPortfolioDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
	public string Portfolio { get; set; }

	/// <summary>Maximum real-time quote collapsing allowed by the client.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CqgCollapsingLevelKey,
		Description = LocalizedStrings.CqgCollapsingLevelDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 9)]
	public CqgCollapsingLevels CollapsingLevel { get; set; } = CqgCollapsingLevels.None;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(OneTimePassword), OneTimePassword)
			.Set(nameof(Token), Token)
			.Set(nameof(PrivateLabel), PrivateLabel)
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(ClientVersion), ClientVersion)
			.Set(nameof(Endpoint), Endpoint)
			.Set(nameof(Portfolio), Portfolio)
			.Set(nameof(CollapsingLevel), CollapsingLevel);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		OneTimePassword = storage.GetValue<SecureString>(nameof(OneTimePassword));
		Token = storage.GetValue<SecureString>(nameof(Token));
		PrivateLabel = storage.GetValue(nameof(PrivateLabel), PrivateLabel);
		ClientId = storage.GetValue(nameof(ClientId), ClientId);
		ClientVersion = storage.GetValue(nameof(ClientVersion), ClientVersion);
		Endpoint = storage.GetValue(nameof(Endpoint), Endpoint);
		Portfolio = storage.GetValue<string>(nameof(Portfolio));
		CollapsingLevel = storage.GetValue(nameof(CollapsingLevel), CollapsingLevel);
	}
}
