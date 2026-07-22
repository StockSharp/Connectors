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

	private string RestEndpoint => IsDemo
		? _demoRestEndpoint
		: _productionRestEndpoint;

	private string SocketEndpoint => IsDemo
		? _demoSocketEndpoint
		: _productionSocketEndpoint;

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
			.Set(nameof(MarketDepth), MarketDepth);
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
		};

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (IsDemo ? ": Demo" : ApiKey.IsEmpty()
			? ": Public"
			: Subaccount == 0 ? ": Live" : ": Subaccount " +
				Subaccount.ToString(CultureInfo.InvariantCulture));
}
