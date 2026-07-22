namespace StockSharp.StocksTrader;

/// <summary>The message adapter for the official StocksTrader REST API.</summary>
[MediaIcon(Media.MediaNames.stockstrader)]
[Doc("topics/api/connectors/forex/stocks_trader.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.StocksTraderKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.FX | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(StocksTraderOrderCondition))]
public partial class StocksTraderMessageAdapter : MessageAdapter, ITokenAdapter,
	IDemoAdapter, IAddressAdapter<Uri>
{
	private static readonly Uri _defaultAddress = new("https://api.stockstrader.com/");
	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.AccessTokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>StocksTrader account identifier. Optional when one matching account exists.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	[BasicSetting]
	public Uri Address { get; set; } = _defaultAddress;

	/// <summary>Polling interval for orders, deals, and account state.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalDataUpdatesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value < TimeSpan.FromSeconds(2)
			? TimeSpan.FromSeconds(2)
			: value;
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(Address), Address)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		Address = storage.GetValue(nameof(Address), Address ?? _defaultAddress);
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
	}
}
