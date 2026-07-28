namespace StockSharp.Settrade;

/// <summary>The message adapter for Settrade Open API v2.</summary>
[MediaIcon(Media.MediaNames.settrade)]
[Doc("topics/api/connectors/stock_market/settrade.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SettradeKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.StockExchangeofThailandKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Asia |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures)]
[OrderCondition(typeof(SettradeOrderCondition))]
public partial class SettradeMessageAdapter : MessageAdapter,
	IKeySecretAdapter, IDemoAdapter
{
	private const string _defaultRestEndpoint =
		"https://open-api.settrade.com";
	private const string _defaultDemoRestEndpoint =
		"https://open-api-test.settrade.com";
	private const string _defaultMarketEndpoint =
		"https://marketapi.settrade.com";
	private const string _defaultDemoMarketEndpoint =
		"https://marketapi-test.settrade.com";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Application code issued by Settrade.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppCodeKey,
		Description = LocalizedStrings.AppCodeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string AppCode { get; set; }

	/// <summary>Settrade broker identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BrokerKey,
		Description = LocalizedStrings.BrokerKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string BrokerId { get; set; }

	/// <summary>Trading account number.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public string Account { get; set; }

	/// <summary>Trading PIN required by investor endpoints.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PinKey,
		Description = LocalizedStrings.PinKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public SecureString Pin { get; set; }

	/// <summary>Configured account market.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountTypeKey,
		Description = LocalizedStrings.AccountTypeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public SettradeAccountTypes AccountType { get; set; }

	/// <summary>Optional value included in Settrade login signature.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ParametersKey,
		Description = LocalizedStrings.ParametersKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public string LoginParameters { get; set; } = string.Empty;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SandboxKey,
		Description = LocalizedStrings.SandboxKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>Production Open API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Sandbox Open API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoRestEndpointKey,
		Description = LocalizedStrings.SandboxRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string DemoRestEndpoint { get; set; } =
		_defaultDemoRestEndpoint;

	/// <summary>Production market-data endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDataEndpointKey,
		Description = LocalizedStrings.MarketDataRestEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string MarketDataEndpoint { get; set; } =
		_defaultMarketEndpoint;

	/// <summary>Sandbox market-data endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoMarketDataHostKey,
		Description = LocalizedStrings.DemoMarketDataHostDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string DemoMarketDataEndpoint { get; set; } =
		_defaultDemoMarketEndpoint;

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Portfolio and order polling interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PollingIntervalKey,
		Description = LocalizedStrings.PollingIntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval =
			value >= TimeSpan.FromSeconds(1) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value),
					value, "Settrade polling interval must be between " +
						"one second and five minutes.");
	}

	private string EffectiveBrokerId
		=> (IsDemo && BrokerId.IsEmpty() ? "098" : BrokerId)
			.ThrowIfEmpty(nameof(BrokerId));

	private string EffectiveRestEndpoint
		=> (IsDemo ? DemoRestEndpoint : RestEndpoint)
			.ThrowIfEmpty(IsDemo ? nameof(DemoRestEndpoint) :
				nameof(RestEndpoint));

	private string EffectiveMarketEndpoint
		=> (IsDemo ? DemoMarketDataEndpoint : MarketDataEndpoint)
			.ThrowIfEmpty(IsDemo ? nameof(DemoMarketDataEndpoint) :
				nameof(MarketDataEndpoint));

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(AppCode), AppCode)
			.Set(nameof(BrokerId), BrokerId)
			.Set(nameof(Account), Account)
			.Set(nameof(Pin), Pin)
			.Set(nameof(AccountType), AccountType)
			.Set(nameof(LoginParameters), LoginParameters)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(DemoRestEndpoint), DemoRestEndpoint)
			.Set(nameof(MarketDataEndpoint), MarketDataEndpoint)
			.Set(nameof(DemoMarketDataEndpoint), DemoMarketDataEndpoint)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		AppCode = storage.GetValue<string>(nameof(AppCode));
		BrokerId = storage.GetValue<string>(nameof(BrokerId));
		Account = storage.GetValue<string>(nameof(Account));
		Pin = storage.GetValue<SecureString>(nameof(Pin));
		AccountType = storage.GetValue(nameof(AccountType), AccountType);
		LoginParameters = storage.GetValue(nameof(LoginParameters),
			LoginParameters);
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint),
			RestEndpoint);
		DemoRestEndpoint = storage.GetValue(nameof(DemoRestEndpoint),
			DemoRestEndpoint);
		MarketDataEndpoint = storage.GetValue(nameof(MarketDataEndpoint),
			MarketDataEndpoint);
		DemoMarketDataEndpoint = storage.GetValue(
			nameof(DemoMarketDataEndpoint), DemoMarketDataEndpoint);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
	}
}
