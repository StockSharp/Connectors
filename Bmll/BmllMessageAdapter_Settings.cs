namespace StockSharp.Bmll;

/// <summary>The message adapter for BMLL historical market data APIs.</summary>
[MediaIcon(Media.MediaNames.bmll)]
[Doc("topics/api/connectors/stock_market/bmll.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BmllKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.Paid |
	MessageAdapterCategories.History | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.OrderLog)]
public partial class BmllMessageAdapter : MessageAdapter, ITokenAdapter,
	ILoginPasswordAdapter
{
	/// <summary>Authentication scheme.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AuthorizationKey,
		Description = LocalizedStrings.AuthorizationKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public BmllAuthenticationModes AuthenticationMode { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string Login { get; set; }

	/// <summary>Path to the PEM-encoded RSA private key registered with BMLL.</summary>
	[Display(Name = "Private key path",
		Description = "Path to the PEM-encoded RSA private key registered with BMLL.",
		GroupName = "Connection", Order = 2)]
	[BasicSetting]
	public string PrivateKeyPath { get; set; }

	/// <inheritdoc />
	[Display(Name = "Private key passphrase",
		Description = "Optional passphrase protecting the BMLL private key.",
		GroupName = "Connection", Order = 3)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Optional BMLL API key accompanying the bearer token.</summary>
	[Display(Name = "API key",
		Description = "Optional BMLL x-api-key accompanying the bearer token.",
		GroupName = "Connection", Order = 5)]
	[BasicSetting]
	public SecureString ApiKey { get; set; }

	/// <summary>BMLL data API base address.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api.data.bmlltech.com/");

	/// <summary>BMLL authentication API base address.</summary>
	[Display(Name = "Authentication address",
		Description = "BMLL authentication API base address.",
		GroupName = "Connection", Order = 7)]
	[BasicSetting]
	public Uri AuthenticationAddress { get; set; } =
		new("https://auth.data.bmlltech.com/");

	/// <summary>Dataset used for tick subscriptions.</summary>
	[Display(Name = "Trades dataset",
		Description = "Entitled BMLL dataset used for tick subscriptions.",
		GroupName = "Datasets", Order = 8)]
	[BasicSetting]
	public string TradesDataset { get; set; } = "trades";

	/// <summary>Dataset used for order-log and market-depth subscriptions.</summary>
	[Display(Name = "Level 3 dataset",
		Description = "Entitled BMLL Level 3 dataset used for order log and depth.",
		GroupName = "Datasets", Order = 9)]
	[BasicSetting]
	public string Level3Dataset { get; set; } = "l3";

	/// <summary>Interval between asynchronous query status polls.</summary>
	[Display(Name = "Query polling interval",
		Description = "Interval between asynchronous query status polls.",
		GroupName = "Queries", Order = 10)]
	public TimeSpan QueryPollingInterval { get; set; } = TimeSpan.FromSeconds(1);

	/// <summary>Maximum time to wait for one asynchronous query.</summary>
	[Display(Name = "Query timeout",
		Description = "Maximum time to wait for one asynchronous query.",
		GroupName = "Queries", Order = 11)]
	public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromMinutes(5);

	/// <summary>Maximum records emitted by one subscription.</summary>
	[Display(Name = "Record limit",
		Description = "Maximum records emitted by one subscription.",
		GroupName = "Limits", Order = 12)]
	public int MaxRecords { get; set; } = 1000000;

	/// <summary>Maximum price levels emitted in one reconstructed depth snapshot.</summary>
	[Display(Name = "Depth limit",
		Description = "Maximum price levels emitted in one reconstructed depth snapshot.",
		GroupName = "Limits", Order = 13)]
	public int MaxDepth { get; set; } = 50;

	/// <summary>Number of UTC dates requested when history has no explicit start.</summary>
	[Display(Name = "Default history days",
		Description = "Number of UTC dates requested when history has no explicit start.",
		GroupName = "History", Order = 14)]
	public int DefaultHistoryDays { get; set; } = 1;

	/// <summary>Emit only BMLL trades marked printable.</summary>
	[Display(Name = "Printable trades only",
		Description = "Emit only BMLL trades marked printable to avoid duplicate reports.",
		GroupName = "Trades", Order = 15)]
	public bool IsPrintableOnly { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AuthenticationMode), AuthenticationMode)
			.Set(nameof(Login), Login)
			.Set(nameof(PrivateKeyPath), PrivateKeyPath)
			.Set(nameof(Password), Password)
			.Set(nameof(Token), Token)
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(Address), Address)
			.Set(nameof(AuthenticationAddress), AuthenticationAddress)
			.Set(nameof(TradesDataset), TradesDataset)
			.Set(nameof(Level3Dataset), Level3Dataset)
			.Set(nameof(QueryPollingInterval), QueryPollingInterval)
			.Set(nameof(QueryTimeout), QueryTimeout)
			.Set(nameof(MaxRecords), MaxRecords)
			.Set(nameof(MaxDepth), MaxDepth)
			.Set(nameof(DefaultHistoryDays), DefaultHistoryDays)
			.Set(nameof(IsPrintableOnly), IsPrintableOnly);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AuthenticationMode = storage.GetValue(nameof(AuthenticationMode), AuthenticationMode);
		Login = storage.GetValue<string>(nameof(Login));
		PrivateKeyPath = storage.GetValue<string>(nameof(PrivateKeyPath));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Token = storage.GetValue<SecureString>(nameof(Token));
		ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
		Address = storage.GetValue(nameof(Address), Address);
		AuthenticationAddress = storage.GetValue(nameof(AuthenticationAddress),
			AuthenticationAddress);
		TradesDataset = storage.GetValue(nameof(TradesDataset), TradesDataset);
		Level3Dataset = storage.GetValue(nameof(Level3Dataset), Level3Dataset);
		QueryPollingInterval = storage.GetValue(nameof(QueryPollingInterval),
			QueryPollingInterval);
		QueryTimeout = storage.GetValue(nameof(QueryTimeout), QueryTimeout);
		MaxRecords = storage.GetValue(nameof(MaxRecords), MaxRecords);
		MaxDepth = storage.GetValue(nameof(MaxDepth), MaxDepth);
		DefaultHistoryDays = storage.GetValue(nameof(DefaultHistoryDays), DefaultHistoryDays);
		IsPrintableOnly = storage.GetValue(nameof(IsPrintableOnly), IsPrintableOnly);
	}
}
