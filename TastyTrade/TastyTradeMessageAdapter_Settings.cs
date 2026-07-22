namespace StockSharp.TastyTrade;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for the tastytrade Open API.
/// </summary>
[MediaIcon(Media.MediaNames.tastytrade)]
[Doc("topics/api/connectors/stock_market/tastytrade.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TastytradeKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Options | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Crypto | MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks)]
[OrderCondition(typeof(TastyTradeOrderCondition))]
public partial class TastyTradeMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// OAuth client secret.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString ClientSecret { get; set; }

	/// <summary>
	/// OAuth scopes requested while refreshing the token.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ScopesKey,
		Description = LocalizedStrings.ScopesKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public TastyTradeScopes Scopes { get; set; } = TastyTradeScopes.Read | TastyTradeScopes.Trade;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(ClientSecret), ClientSecret)
			.Set(nameof(Scopes), Scopes)
			.Set(nameof(IsDemo), IsDemo);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		ClientSecret = storage.GetValue<SecureString>(nameof(ClientSecret));
		Scopes = storage.GetValue(nameof(Scopes), Scopes);
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
	}
}
