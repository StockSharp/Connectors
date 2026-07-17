namespace StockSharp.MatchTrader;

/// <summary>The message adapter for Match-Trader Platform API.</summary>
[MediaIcon(Media.MediaNames.matchtrader)]
[Doc("topics/api/connectors/forex/matchtrader.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MatchTraderKey,
	Description = LocalizedStrings.ForexConnectorKey, GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Level1)]
[OrderCondition(typeof(MatchTraderOrderCondition))]
public partial class MatchTraderMessageAdapter : MessageAdapter, ILoginPasswordAdapter,
	IAddressAdapter<string>
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string Address { get; set; } = "https://mtr-demo-prod.match-trader.com";

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Broker identifier supplied by the Match-Trader broker.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BrokerKey,
		Description = LocalizedStrings.MatchTraderBrokerDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public string BrokerId { get; set; }

	/// <summary>Trading account ID or UUID. May be omitted for the selected account.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <summary>Minimum interval between REST polling jobs.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage.Set(nameof(Address), Address).Set(nameof(Login), Login).Set(nameof(Password), Password)
			.Set(nameof(BrokerId), BrokerId).Set(nameof(AccountId), AccountId)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Address = storage.GetValue(nameof(Address), Address);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		BrokerId = storage.GetValue<string>(nameof(BrokerId));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
	}
}
