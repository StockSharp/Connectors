namespace StockSharp.OpenMarkets;

/// <summary>The message adapter for the official OpenMarkets APIs.</summary>
[MediaIcon(Media.MediaNames.openmarkets)]
[Doc("topics/api/connectors/stock_market/openmarkets.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OpenMarketsKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AustraliaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.Stock |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Transactions)]
public partial class OpenMarketsMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private TimeSpan _depthPollingInterval = TimeSpan.FromSeconds(2);

	/// <summary>OAuth client identifier issued by OpenMarkets.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.OpenMarketsClientIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>OAuth client secret issued by OpenMarkets.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.OpenMarketsClientSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Order-account code. Empty means all accessible accounts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpenMarketsAccountKey,
		Description = LocalizedStrings.OpenMarketsAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string AccountCode { get; set; }

	/// <summary>Use OpenMarkets test and stage services.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpenMarketsTestKey,
		Description = LocalizedStrings.OpenMarketsTestDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public bool IsTest { get; set; }

	/// <summary>Market-data source used by REST identifiers and SignalR subscriptions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpenMarketsDataSourceKey,
		Description = LocalizedStrings.OpenMarketsDataSourceDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string DataSource { get; set; } = OpenMarketsExtensions.DefaultDataSource;

	/// <summary>Exchange used when a security identifier has no board code.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpenMarketsExchangeKey,
		Description = LocalizedStrings.OpenMarketsExchangeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public string DefaultExchange { get; set; } = OpenMarketsExtensions.DefaultExchange;

	/// <summary>Order destination used for new orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpenMarketsDestinationKey,
		Description = LocalizedStrings.OpenMarketsDestinationDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public string DefaultDestination { get; set; } = OpenMarketsExtensions.DefaultExchange;

	/// <summary>Client or person who requested the order.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpenMarketsOrderGiverKey,
		Description = LocalizedStrings.OpenMarketsOrderGiverDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public string OrderGiver { get; set; }

	/// <summary>Advisor or representative who received the order.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpenMarketsOrderTakerKey,
		Description = LocalizedStrings.OpenMarketsOrderTakerDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public string OrderTaker { get; set; }

	/// <summary>Fallback native-price multiplier when security metadata does not provide one.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpenMarketsPriceMultiplierKey,
		Description = LocalizedStrings.OpenMarketsPriceMultiplierDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public decimal DefaultPriceMultiplier { get; set; } = 0.01m;

	/// <summary>Polling interval for REST market-depth snapshots.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpenMarketsDepthPollingKey,
		Description = LocalizedStrings.OpenMarketsDepthPollingDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 10)]
	public TimeSpan DepthPollingInterval
	{
		get => _depthPollingInterval;
		set => _depthPollingInterval = value < TimeSpan.FromSeconds(1)
			? TimeSpan.FromSeconds(1)
			: value;
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(AccountCode), AccountCode)
			.Set(nameof(IsTest), IsTest)
			.Set(nameof(DataSource), DataSource)
			.Set(nameof(DefaultExchange), DefaultExchange)
			.Set(nameof(DefaultDestination), DefaultDestination)
			.Set(nameof(OrderGiver), OrderGiver)
			.Set(nameof(OrderTaker), OrderTaker)
			.Set(nameof(DefaultPriceMultiplier), DefaultPriceMultiplier)
			.Set(nameof(DepthPollingInterval), DepthPollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		AccountCode = storage.GetValue<string>(nameof(AccountCode));
		IsTest = storage.GetValue(nameof(IsTest), IsTest);
		DataSource = storage.GetValue(nameof(DataSource), DataSource);
		DefaultExchange = storage.GetValue(nameof(DefaultExchange), DefaultExchange);
		DefaultDestination = storage.GetValue(nameof(DefaultDestination), DefaultDestination);
		OrderGiver = storage.GetValue<string>(nameof(OrderGiver));
		OrderTaker = storage.GetValue<string>(nameof(OrderTaker));
		DefaultPriceMultiplier = storage.GetValue(nameof(DefaultPriceMultiplier), DefaultPriceMultiplier);
		DepthPollingInterval = storage.GetValue(nameof(DepthPollingInterval), DepthPollingInterval);
	}
}
