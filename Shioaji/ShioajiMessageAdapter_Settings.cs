namespace StockSharp.Shioaji;

/// <summary>The message adapter for the official SinoPac Shioaji HTTP/SSE server.</summary>
[MediaIcon(Media.MediaNames.shioaji)]
[Doc("topics/api/connectors/stock_market/shioaji.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ShioajiKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.TaiwanStockExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.History | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options)]
[OrderCondition(typeof(ShioajiOrderCondition))]
public partial class ShioajiMessageAdapter : MessageAdapter, IKeySecretAdapter, IAddressAdapter<string>
{
	private string _address = "http://localhost:8080/";

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ShioajiAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string Address
	{
		get => _address;
		set => _address = value.ThrowIfEmpty(nameof(value));
	}

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.ShioajiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.ShioajiSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Maximum number of SSE reconnect attempts.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ShioajiReconnectAttemptsKey,
		Description = LocalizedStrings.ShioajiReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Address), Address)
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Address = storage.GetValue(nameof(Address), Address);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
	}
}
