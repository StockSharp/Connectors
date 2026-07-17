namespace StockSharp.AliceBlue;

/// <summary>The message adapter for Alice Blue ANT API.</summary>
[MediaIcon(Media.MediaNames.alice_blue)]
[Doc("topics/api/connectors/stock_market/alice_blue.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AliceBlueKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions | MessageAdapterCategories.History |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX | MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(AliceBlueOrderCondition))]
public partial class AliceBlueMessageAdapter : MessageAdapter, ITokenAdapter
{
	/// <summary>Alice Blue user identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UserIdKey,
		Description = LocalizedStrings.AliceBlueUserIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string UserId { get; set; }

	/// <summary>Alice Blue trading client identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ClientCodeKey,
		Description = LocalizedStrings.AliceBlueClientIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	public string ClientId { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.AliceBlueSessionTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Stable application device identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AliceBlueDeviceIdKey,
		Description = LocalizedStrings.AliceBlueDeviceIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");

	/// <summary>Default order product.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AliceBlueDefaultProductKey,
		Description = LocalizedStrings.AliceBlueDefaultProductDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 4)]
	public AliceBlueProducts DefaultProduct { get; set; } = AliceBlueProducts.LongTerm;

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AliceBlueReconnectAttemptsKey,
		Description = LocalizedStrings.AliceBlueReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(UserId), UserId)
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(Token), Token)
			.Set(nameof(DeviceId), DeviceId)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		UserId = storage.GetValue<string>(nameof(UserId));
		ClientId = storage.GetValue<string>(nameof(ClientId));
		Token = storage.GetValue<SecureString>(nameof(Token));
		DeviceId = storage.GetValue(nameof(DeviceId), DeviceId);
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
	}
}
