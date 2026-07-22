namespace StockSharp.Trading212;

/// <summary>The message adapter for the official Trading 212 Public API.</summary>
[MediaIcon(Media.MediaNames.trading212)]
[Doc("topics/api/connectors/stock_market/trading212.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.Trading212Key,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(MessageAdapterCategories.Europe | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Stock)]
[OrderCondition(typeof(Trading212OrderCondition))]
public partial class Trading212MessageAdapter : MessageAdapter, IDemoAdapter, IKeySecretAdapter
{
	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>Trading 212 Public API key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.Trading212ApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Trading 212 Public API secret.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.Trading212ApiSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <summary>Polling interval for account and transaction updates.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.Trading212PollingIntervalKey,
		Description = LocalizedStrings.Trading212PollingIntervalDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value < TimeSpan.FromSeconds(10)
			? TimeSpan.FromSeconds(10)
			: value;
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
	}
}
