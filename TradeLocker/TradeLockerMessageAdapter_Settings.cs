namespace StockSharp.TradeLocker;

/// <summary>The message adapter for TradeLocker Public API.</summary>
[MediaIcon(Media.MediaNames.tradelocker)]
[Doc("topics/api/connectors/stock_market/tradelocker.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TradeLockerKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Stock | MessageAdapterCategories.FX |
	MessageAdapterCategories.Level1)]
[OrderCondition(typeof(TradeLockerOrderCondition))]
public partial class TradeLockerMessageAdapter : MessageAdapter, ILoginPasswordAdapter, IDemoAdapter
{
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

	/// <summary>Broker server name shown on the TradeLocker login form.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ServerKey,
		Description = LocalizedStrings.ServerKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string Server { get; set; }

	/// <summary>TradeLocker account identifier. May be omitted for a single account.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <summary>Optional key issued by the TradeLocker Developer Program.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TradeLockerDeveloperKeyKey,
		Description = LocalizedStrings.TradeLockerDeveloperKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public SecureString DeveloperApiKey { get; set; }

	/// <summary>Minimum interval between REST polling jobs.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage.Set(nameof(Login), Login).Set(nameof(Password), Password).Set(nameof(Server), Server)
			.Set(nameof(AccountId), AccountId).Set(nameof(IsDemo), IsDemo)
			.Set(nameof(DeveloperApiKey), DeveloperApiKey).Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Server = storage.GetValue<string>(nameof(Server));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		DeveloperApiKey = storage.GetValue<SecureString>(nameof(DeveloperApiKey));
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
	}
}
