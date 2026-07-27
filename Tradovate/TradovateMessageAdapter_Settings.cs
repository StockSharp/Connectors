namespace StockSharp.Tradovate;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for Tradovate API.
/// </summary>
[MediaIcon(Media.MediaNames.tradovate)]
[Doc("topics/api/connectors/stock_market/tradovate.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TradovateKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth)]
public partial class TradovateMessageAdapter : MessageAdapter, ILoginPasswordAdapter, IDemoAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://live.tradovateapi.com/v1/";
	private const string _defaultDemoRestEndpoint = "https://demo.tradovateapi.com/v1/";
	private const string _defaultMarketWebSocketEndpoint = "wss://md.tradovateapi.com/v1/websocket";
	private const string _defaultAccountWebSocketEndpoint = "wss://live.tradovateapi.com/v1/websocket";
	private const string _defaultDemoAccountWebSocketEndpoint = "wss://demo.tradovateapi.com/v1/websocket";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>
	/// API client identifier.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.ClientCodeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>
	/// API client secret.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// Application identifier.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppIdKey,
		Description = LocalizedStrings.AppIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public string AppId { get; set; } = "StockSharp";

	/// <summary>
	/// Application version.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppVersionKey,
		Description = LocalizedStrings.AppVersionKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string AppVersion { get; set; } = "1.0";

	/// <summary>
	/// Stable device identifier.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DeviceIdKey,
		Description = LocalizedStrings.DeviceIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public string DeviceId { get; set; } = Guid.NewGuid().ToString();

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>Production REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.ProductionRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 8)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Demo REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoRestEndpointKey,
		Description = LocalizedStrings.DemoRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 9)]
	public string DemoRestEndpoint { get; set; } = _defaultDemoRestEndpoint;

	/// <summary>Market data WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketWebSocketEndpointKey,
		Description = LocalizedStrings.MarketDataWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 10)]
	public string MarketWebSocketEndpoint { get; set; } = _defaultMarketWebSocketEndpoint;

	/// <summary>Production account WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountWebSocketEndpointKey,
		Description = LocalizedStrings.ProductionAccountWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 11)]
	public string AccountWebSocketEndpoint { get; set; } = _defaultAccountWebSocketEndpoint;

	/// <summary>Demo account WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoAccountWebSocketEndpointKey,
		Description = LocalizedStrings.DemoAccountWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 12)]
	public string DemoAccountWebSocketEndpoint { get; set; } = _defaultDemoAccountWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(AppId), AppId)
			.Set(nameof(AppVersion), AppVersion)
			.Set(nameof(DeviceId), DeviceId)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(DemoRestEndpoint), DemoRestEndpoint)
			.Set(nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint)
			.Set(nameof(AccountWebSocketEndpoint), AccountWebSocketEndpoint)
			.Set(nameof(DemoAccountWebSocketEndpoint), DemoAccountWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		AppId = storage.GetValue(nameof(AppId), AppId);
		AppVersion = storage.GetValue(nameof(AppVersion), AppVersion);
		DeviceId = storage.GetValue(nameof(DeviceId), DeviceId);
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		DemoRestEndpoint = storage.GetValue(nameof(DemoRestEndpoint), DemoRestEndpoint);
		MarketWebSocketEndpoint = storage.GetValue(nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint);
		AccountWebSocketEndpoint = storage.GetValue(nameof(AccountWebSocketEndpoint), AccountWebSocketEndpoint);
		DemoAccountWebSocketEndpoint = storage.GetValue(nameof(DemoAccountWebSocketEndpoint), DemoAccountWebSocketEndpoint);
	}
}
