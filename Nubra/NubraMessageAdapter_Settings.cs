namespace StockSharp.Nubra;

/// <summary>The message adapter for Nubra REST API V3 and WebSocket market data.</summary>
[MediaIcon(Media.MediaNames.nubra)]
[Doc("topics/api/connectors/stock_market/nubra.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.NubraKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Asia |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options |
	MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(NubraOrderCondition))]
public partial class NubraMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	private static readonly Uri _productionRestAddress =
		new("https://api.nubra.io/");
	private static readonly Uri _uatRestAddress =
		new("https://uatapi.nubra.io/");
	private static readonly Uri _productionMarketDataAddress =
		new("wss://api.nubra.io/apibatch/ws");
	private static readonly Uri _uatMarketDataAddress =
		new("wss://uatapi.nubra.io/apibatch/ws");

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromSeconds(1),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(2),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	/// <summary>Possible historical candle time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <inheritdoc />
	[Display(
		Name = "Session token",
		Description = "Bearer session token returned by Nubra login. When empty, TOTP login settings are used.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Registered Nubra phone number.</summary>
	[Display(
		Name = "Phone",
		Description = "Registered phone number used for automated TOTP login.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public string Phone { get; set; }

	/// <summary>Nubra MPIN.</summary>
	[Display(
		Name = "MPIN",
		Description = "Nubra MPIN used after TOTP verification.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public SecureString Mpin { get; set; }

	/// <summary>Base32 TOTP secret enabled in the Nubra account.</summary>
	[Display(
		Name = "TOTP secret",
		Description = "Base32 secret generated when TOTP authentication is enabled in Nubra.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public SecureString TotpSecret { get; set; }

	/// <summary>Device identifier bound to the login session.</summary>
	[Display(
		Name = "Device ID",
		Description = "Stable device identifier sent in the x-device-id header.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public string DeviceId { get; set; } = "StockSharp";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>Portfolio name emitted by the connector.</summary>
	[Display(
		Name = "Portfolio name",
		Description = "Portfolio name. When empty, the Nubra client code is used.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public string PortfolioName { get; set; }

	/// <summary>Default delivery product used for new orders.</summary>
	[Display(
		Name = "Default product",
		Description = "Default Nubra delivery product used for new orders.",
		GroupName = LocalizedStrings.OrderKey,
		Order = 7)]
	public NubraProducts DefaultProduct { get; set; } = NubraProducts.Cnc;

	/// <summary>Interval for order and portfolio snapshot polling.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(15);

	/// <summary>Maximum number of WebSocket reconnect attempts.</summary>
	[Display(
		Name = "Reconnect attempts",
		Description = "Maximum number of Nubra market-data WebSocket reconnect attempts.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <summary>Production REST API address.</summary>
	[Display(
		Name = "REST address",
		Description = "Nubra production REST API root.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 10)]
	public Uri RestAddress { get; set; } = _productionRestAddress;

	/// <summary>UAT REST API address.</summary>
	[Display(
		Name = "UAT REST address",
		Description = "Nubra UAT REST API root.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 11)]
	public Uri UatRestAddress { get; set; } = _uatRestAddress;

	/// <summary>Production market-data WebSocket address.</summary>
	[Display(
		Name = "Market data address",
		Description = "Nubra production batch market-data WebSocket.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 12)]
	public Uri MarketDataAddress { get; set; } =
		_productionMarketDataAddress;

	/// <summary>UAT market-data WebSocket address.</summary>
	[Display(
		Name = "UAT market data address",
		Description = "Nubra UAT batch market-data WebSocket.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 13)]
	public Uri UatMarketDataAddress { get; set; } =
		_uatMarketDataAddress;

	internal Uri EffectiveRestAddress
		=> IsDemo ? UatRestAddress : RestAddress;

	internal Uri EffectiveMarketDataAddress
		=> IsDemo ? UatMarketDataAddress : MarketDataAddress;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Phone), Phone)
			.Set(nameof(Mpin), Mpin)
			.Set(nameof(TotpSecret), TotpSecret)
			.Set(nameof(DeviceId), DeviceId)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(PortfolioName), PortfolioName)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts)
			.Set(nameof(RestAddress), RestAddress)
			.Set(nameof(UatRestAddress), UatRestAddress)
			.Set(nameof(MarketDataAddress), MarketDataAddress)
			.Set(nameof(UatMarketDataAddress), UatMarketDataAddress);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Phone = storage.GetValue<string>(nameof(Phone));
		Mpin = storage.GetValue<SecureString>(nameof(Mpin));
		TotpSecret = storage.GetValue<SecureString>(nameof(TotpSecret));
		DeviceId = storage.GetValue(nameof(DeviceId), DeviceId);
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		PortfolioName = storage.GetValue<string>(nameof(PortfolioName));
		DefaultProduct = storage.GetValue(
			nameof(DefaultProduct),
			DefaultProduct);
		PollingInterval = storage.GetValue(
			nameof(PollingInterval),
			PollingInterval);
		ReconnectAttempts = storage.GetValue(
			nameof(ReconnectAttempts),
			ReconnectAttempts);
		RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
		UatRestAddress = storage.GetValue(
			nameof(UatRestAddress),
			UatRestAddress);
		MarketDataAddress = storage.GetValue(
			nameof(MarketDataAddress),
			MarketDataAddress);
		UatMarketDataAddress = storage.GetValue(
			nameof(UatMarketDataAddress),
			UatMarketDataAddress);
	}
}
