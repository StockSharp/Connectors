namespace StockSharp.Swissquote;

/// <summary>The message adapter for Swissquote OpenWealth APIs.</summary>
[MediaIcon(Media.MediaNames.swissquote)]
[Doc("topics/api/connectors/stock_market/swissquote.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SwissQuoteKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(MessageAdapterCategories.Transactions | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options | MessageAdapterCategories.Crypto)]
[OrderCondition(typeof(SwissquoteOrderCondition))]
public partial class SwissquoteMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	private const string _defaultTradingEndpoint = "https://bankingapi.swissquote.ch/ow-trading/api/v1/";
	private const string _defaultDemoTradingEndpoint = "https://bankingapi.simulator.swissquote.ch/ow-trading/api/v1/";
	private const string _defaultCustodyEndpoint = "https://bankingapi.swissquote.ch/ow-custody/api/v1/";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.SwissquoteAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Optional Swissquote customer identifier used to narrow account discovery.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SwissquoteCustomerIdKey,
		Description = LocalizedStrings.SwissquoteCustomerIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public string CustomerId { get; set; }

	/// <summary>Default safekeeping account used for securities positions and orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SwissquoteSafekeepingAccountKey,
		Description = LocalizedStrings.SwissquoteSafekeepingAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string SafekeepingAccountId { get; set; }

	/// <summary>Default cash account included in securities order allocations.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SwissquoteCashAccountKey,
		Description = LocalizedStrings.SwissquoteCashAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string CashAccountId { get; set; }

	/// <summary>Default account and order currency.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SwissquoteAccountCurrencyKey,
		Description = LocalizedStrings.SwissquoteAccountCurrencyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string AccountCurrency { get; set; } = "CHF";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <summary>Validate orders without exchange execution.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SwissquoteDryRunKey,
		Description = LocalizedStrings.SwissquoteDryRunDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public bool IsDryRun { get; set; }

	/// <summary>Allow independent handling of allocations in a bulk order.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SwissquoteBestEffortKey,
		Description = LocalizedStrings.SwissquoteBestEffortDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public bool IsBestEffort { get; set; }

	/// <summary>Production trading REST endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TradingRestEndpointKey,
		Description = LocalizedStrings.ProductionTradingRestEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 8)]
	public string TradingEndpoint { get; set; } = _defaultTradingEndpoint;

	/// <summary>Simulation trading REST endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoTradingRestEndpointKey,
		Description = LocalizedStrings.SimulationTradingRestEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 9)]
	public string DemoTradingEndpoint { get; set; } = _defaultDemoTradingEndpoint;

	/// <summary>Custody REST endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CustodyRestEndpointKey,
		Description = LocalizedStrings.CustodyRestEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 10)]
	public string CustodyEndpoint { get; set; } = _defaultCustodyEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(CustomerId), CustomerId)
			.Set(nameof(SafekeepingAccountId), SafekeepingAccountId)
			.Set(nameof(CashAccountId), CashAccountId)
			.Set(nameof(AccountCurrency), AccountCurrency)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(IsDryRun), IsDryRun)
			.Set(nameof(IsBestEffort), IsBestEffort)
			.Set(nameof(TradingEndpoint), TradingEndpoint)
			.Set(nameof(DemoTradingEndpoint), DemoTradingEndpoint)
			.Set(nameof(CustodyEndpoint), CustodyEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		CustomerId = storage.GetValue<string>(nameof(CustomerId));
		SafekeepingAccountId = storage.GetValue<string>(nameof(SafekeepingAccountId));
		CashAccountId = storage.GetValue<string>(nameof(CashAccountId));
		AccountCurrency = storage.GetValue(nameof(AccountCurrency), AccountCurrency);
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		IsDryRun = storage.GetValue(nameof(IsDryRun), IsDryRun);
		IsBestEffort = storage.GetValue(nameof(IsBestEffort), IsBestEffort);
		TradingEndpoint = storage.GetValue(nameof(TradingEndpoint), TradingEndpoint);
		DemoTradingEndpoint = storage.GetValue(nameof(DemoTradingEndpoint), DemoTradingEndpoint);
		CustodyEndpoint = storage.GetValue(nameof(CustodyEndpoint), CustodyEndpoint);
	}
}
