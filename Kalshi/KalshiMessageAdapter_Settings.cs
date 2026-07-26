namespace StockSharp.Kalshi;

public partial class KalshiMessageAdapter
{
	private const string _productionRestEndpoint =
		"https://external-api.kalshi.com/trade-api/v2";
	private const string _productionSocketEndpoint =
		"wss://external-api-ws.kalshi.com/trade-api/ws/v2";
	private const string _demoRestEndpoint =
		"https://external-api.demo.kalshi.co/trade-api/v2";
	private const string _demoSocketEndpoint =
		"wss://external-api-ws.demo.kalshi.co/trade-api/ws/v2";

	/// <summary>Kalshi API key ID.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string ApiKey { get; set; }

	/// <summary>PEM-encoded RSA private key used to sign API requests.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	private int _subaccount;

	/// <summary>Kalshi subaccount number, zero for the primary account.</summary>
	[Display(
		Name = "Subaccount",
		Description = "Kalshi subaccount number (0-63).",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public int Subaccount
	{
		get => _subaccount;
		set => _subaccount = value is >= 0 and <= 63
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Kalshi subaccount must be between zero and 63.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>Private account reconciliation interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(2) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Kalshi polling interval must be between two seconds and five minutes.");
	}

	private int _historyLimit = 1000;

	/// <summary>Maximum private or public history records per request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 10000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Kalshi history limit must be between one and 10000.");
	}

	private int _securityLookupLimit = 10000;

	/// <summary>Maximum markets returned by an unbounded security lookup.</summary>
	[Display(
		Name = "Security lookup limit",
		Description = "Maximum open Kalshi markets returned by one lookup.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int SecurityLookupLimit
	{
		get => _securityLookupLimit;
		set => _securityLookupLimit = value is >= 1 and <= 50000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Kalshi security lookup limit must be between one and 50000.");
	}

	private int _marketDepth = 100;

	/// <summary>Maximum published order-book depth.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDepthKey,
		Description = LocalizedStrings.MarketDepthKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 100
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Kalshi market depth must be between one and 100.");
	}

	/// <summary>Production REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "Production REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 7)]
	public string ProductionRestEndpoint { get; set; } = _productionRestEndpoint;

	/// <summary>Demo REST API endpoint.</summary>
	[Display(
		Name = "Demo REST endpoint",
		Description = "Demo REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 8)]
	public string DemoRestEndpoint { get; set; } = _demoRestEndpoint;

	/// <summary>Production WebSocket endpoint.</summary>
	[Display(
		Name = "WebSocket endpoint",
		Description = "Production WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 9)]
	public string ProductionSocketEndpoint { get; set; } = _productionSocketEndpoint;

	/// <summary>Demo WebSocket endpoint.</summary>
	[Display(
		Name = "Demo WebSocket endpoint",
		Description = "Demo WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 10)]
	public string DemoSocketEndpoint { get; set; } = _demoSocketEndpoint;

	private string RestEndpoint => IsDemo
		? DemoRestEndpoint
		: ProductionRestEndpoint;

	private string SocketEndpoint => IsDemo
		? DemoSocketEndpoint
		: ProductionSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(Subaccount), Subaccount)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(SecurityLookupLimit), SecurityLookupLimit)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(ProductionRestEndpoint), ProductionRestEndpoint)
			.Set(nameof(DemoRestEndpoint), DemoRestEndpoint)
			.Set(nameof(ProductionSocketEndpoint), ProductionSocketEndpoint)
			.Set(nameof(DemoSocketEndpoint), DemoSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiKey = storage.GetValue<string>(nameof(ApiKey));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		Subaccount = storage.GetValue(nameof(Subaccount), Subaccount);
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		SecurityLookupLimit = storage.GetValue(nameof(SecurityLookupLimit),
			SecurityLookupLimit);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
		ProductionRestEndpoint = storage.GetValue(nameof(ProductionRestEndpoint), ProductionRestEndpoint);
		DemoRestEndpoint = storage.GetValue(nameof(DemoRestEndpoint), DemoRestEndpoint);
		ProductionSocketEndpoint = storage.GetValue(nameof(ProductionSocketEndpoint), ProductionSocketEndpoint);
		DemoSocketEndpoint = storage.GetValue(nameof(DemoSocketEndpoint), DemoSocketEndpoint);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new KalshiMessageAdapter(TransactionIdGenerator)
		{
			ApiKey = ApiKey,
			PrivateKey = PrivateKey,
			IsDemo = IsDemo,
			Subaccount = Subaccount,
			PollingInterval = PollingInterval,
			HistoryLimit = HistoryLimit,
			SecurityLookupLimit = SecurityLookupLimit,
			MarketDepth = MarketDepth,
			ProductionRestEndpoint = ProductionRestEndpoint,
			DemoRestEndpoint = DemoRestEndpoint,
			ProductionSocketEndpoint = ProductionSocketEndpoint,
			DemoSocketEndpoint = DemoSocketEndpoint,
		};

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (IsDemo ? ": Demo" : ApiKey.IsEmpty()
			? ": Public"
			: Subaccount == 0 ? ": Live" : ": Subaccount " +
				Subaccount.ToString(CultureInfo.InvariantCulture));
}
