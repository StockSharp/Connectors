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
	private const string _defaultRestEndpoint = "https://api.tastyworks.com/";
	private const string _defaultDemoRestEndpoint = "https://api.cert.tastyworks.com/";
	private const string _defaultAccountWebSocketEndpoint = "wss://streamer.tastyworks.com";
	private const string _defaultDemoAccountWebSocketEndpoint = "wss://streamer.cert.tastyworks.com";

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

	/// <summary>Production REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.ProductionRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Demo REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoRestEndpointKey,
		Description = LocalizedStrings.DemoRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string DemoRestEndpoint { get; set; } = _defaultDemoRestEndpoint;

	/// <summary>Production account WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountWebSocketEndpointKey,
		Description = LocalizedStrings.ProductionAccountWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	public string AccountWebSocketEndpoint { get; set; } = _defaultAccountWebSocketEndpoint;

	/// <summary>Demo account WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoAccountWebSocketEndpointKey,
		Description = LocalizedStrings.DemoAccountWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 7)]
	public string DemoAccountWebSocketEndpoint { get; set; } = _defaultDemoAccountWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(ClientSecret), ClientSecret)
			.Set(nameof(Scopes), Scopes)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(DemoRestEndpoint), DemoRestEndpoint)
			.Set(nameof(AccountWebSocketEndpoint), AccountWebSocketEndpoint)
			.Set(nameof(DemoAccountWebSocketEndpoint), DemoAccountWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		ClientSecret = storage.GetValue<SecureString>(nameof(ClientSecret));
		Scopes = storage.GetValue(nameof(Scopes), Scopes);
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		DemoRestEndpoint = storage.GetValue(nameof(DemoRestEndpoint), DemoRestEndpoint);
		AccountWebSocketEndpoint = storage.GetValue(nameof(AccountWebSocketEndpoint), AccountWebSocketEndpoint);
		DemoAccountWebSocketEndpoint = storage.GetValue(nameof(DemoAccountWebSocketEndpoint), DemoAccountWebSocketEndpoint);
	}
}
