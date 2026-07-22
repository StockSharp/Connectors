namespace StockSharp.Deriv;

/// <summary>The message adapter for the Deriv Options WebSocket API.</summary>
[MediaIcon(Media.MediaNames.deriv)]
[Doc("topics/api/connectors/forex/deriv.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DerivKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX |
	MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(DerivOrderCondition))]
public partial class DerivMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.AccessTokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Deriv application identifier sent with authenticated REST requests.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppIdKey,
		Description = LocalizedStrings.AppIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string AppId { get; set; }

	/// <summary>Options account identifier. An active account is selected when empty.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <summary>Deriv REST API base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public Uri RestAddress { get; set; } = new("https://api.derivws.com");

	/// <summary>Public Deriv Options WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WebSocketKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public string PublicWebSocketAddress { get; set; } =
		"wss://api.derivws.com/trading/v1/options/ws/public";

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(AppId), AppId)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestAddress), RestAddress)
			.Set(nameof(PublicWebSocketAddress), PublicWebSocketAddress);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		AppId = storage.GetValue<string>(nameof(AppId));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
		PublicWebSocketAddress = storage.GetValue(nameof(PublicWebSocketAddress),
			PublicWebSocketAddress);
	}
}
