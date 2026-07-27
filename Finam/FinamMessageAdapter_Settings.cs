namespace StockSharp.Finam;

/// <summary>
/// The message adapter for Finam Trade API.
/// </summary>
[MediaIcon(Media.MediaNames.finam)]
[Doc("topics/api/connectors/russia/finam.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FinamKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Russia |
	MessageAdapterCategories.Transactions |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options |
	MessageAdapterCategories.FX |
	MessageAdapterCategories.Free)]
[OrderCondition(typeof(FinamOrderCondition))]
public partial class FinamMessageAdapter : MessageAdapter, ITokenAdapter
{
	private const string _defaultRestAddress = "https://api.finam.ru/";
	private const string _defaultWebSocketAddress = "wss://api.finam.ru/ws";

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
	/// Trading account identifier. When empty, the first account available to the token is used.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountIdKey,
		Description = LocalizedStrings.AccountIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <summary>
	/// Application identifier sent during session creation.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppIdKey,
		Description = LocalizedStrings.AppIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public string AppId { get; set; } = "StockSharp";

	/// <summary>
	/// Interval for polling account and order snapshots.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PollingIntervalKey,
		Description = LocalizedStrings.PollingIntervalKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>
	/// Maximum number of securities returned by an unrestricted lookup.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LookupLimitKey,
		Description = LocalizedStrings.LookupLimitKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 4)]
	public int LookupLimit { get; set; } = 10000;

	/// <summary>
	/// Finam REST API base address.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestAddressKey,
		Description = LocalizedStrings.RestEndpointKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string RestAddress { get; set; } = _defaultRestAddress;

	/// <summary>
	/// Finam WebSocket API address.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketAddressKey,
		Description = LocalizedStrings.WebSocketEndpointKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	public string WebSocketAddress { get; set; } = _defaultWebSocketAddress;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(AppId), AppId)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(LookupLimit), LookupLimit)
			.Set(nameof(RestAddress), RestAddress)
			.Set(nameof(WebSocketAddress), WebSocketAddress);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		AccountId = storage.GetValue(nameof(AccountId), AccountId);
		AppId = storage.GetValue(nameof(AppId), AppId);
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
		LookupLimit = storage.GetValue(nameof(LookupLimit), LookupLimit);
		RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
		WebSocketAddress = storage.GetValue(nameof(WebSocketAddress), WebSocketAddress);
	}
}
