namespace StockSharp.Bluefin;

/// <summary>Bluefin environments.</summary>
[DataContract]
public enum BluefinEnvironments
{
	/// <summary>Mainnet.</summary>
	[EnumMember]
	[Display(
		Name = "Mainnet")]
	Mainnet,

	/// <summary>Staging testnet.</summary>
	[EnumMember]
	[Display(
		Name = "Testnet")]
	Testnet,
}

public partial class BluefinMessageAdapter
{
	/// <summary>Bluefin environment.</summary>
	[Display(
		Name = "Environment",
		Description = "Bluefin API environment.",
		GroupName = "Connection",
		Order = 0)]
	[BasicSetting]
	public BluefinEnvironments Environment { get; set; }

	/// <summary>Optional Sui account address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional Sui Ed25519 private key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Optional exchange REST endpoint override.</summary>
	[Display(
		Name = "Exchange endpoint",
		Description = "Optional Bluefin exchange and account-data REST endpoint override.",
		GroupName = "Addresses",
		Order = 0)]
	public string ExchangeEndpoint { get; set; }

	/// <summary>Optional trading REST endpoint override.</summary>
	[Display(
		Name = "Trade endpoint",
		Description = "Optional Bluefin trading REST endpoint override.",
		GroupName = "Addresses",
		Order = 1)]
	public string TradeEndpoint { get; set; }

	/// <summary>Optional authentication REST endpoint override.</summary>
	[Display(
		Name = "Authentication endpoint",
		Description = "Optional Bluefin authentication REST endpoint override.",
		GroupName = "Addresses",
		Order = 2)]
	public string AuthEndpoint { get; set; }

	/// <summary>Optional market WebSocket endpoint override.</summary>
	[Display(
		Name = "Market WebSocket endpoint",
		Description = "Optional Bluefin market WebSocket endpoint override.",
		GroupName = "Addresses",
		Order = 3)]
	public string MarketSocketEndpoint { get; set; }

	/// <summary>Optional account WebSocket endpoint override.</summary>
	[Display(
		Name = "Account WebSocket endpoint",
		Description = "Optional Bluefin account WebSocket endpoint override.",
		GroupName = "Addresses",
		Order = 4)]
	public string AccountSocketEndpoint { get; set; }

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Private REST fallback polling interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(2)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Bluefin polling interval cannot be less than two seconds.");
	}

	private int _historyLimit = 500;

	/// <summary>Maximum history records per request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Bluefin history limit must be between one and 1000.");
	}

	private int _marketDepth = 100;

	/// <summary>Maximum published order-book depth.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDepthKey,
		Description = LocalizedStrings.MarketDepthKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Bluefin market depth must be between one and 1000.");
	}

	private TimeSpan _orderExpiry = TimeSpan.FromDays(30);

	/// <summary>Default order expiry when no expiry is supplied.</summary>
	[Display(
		Name = "Order expiry",
		Description = "Default lifetime for signed Bluefin orders.",
		GroupName = "Connection",
		Order = 6)]
	public TimeSpan OrderExpiry
	{
		get => _orderExpiry;
		set => _orderExpiry = value >= TimeSpan.FromMinutes(1) &&
			value <= TimeSpan.FromDays(30)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Bluefin order expiry must be between one minute and 30 days.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Environment), Environment)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(ExchangeEndpoint), ExchangeEndpoint)
			.Set(nameof(TradeEndpoint), TradeEndpoint)
			.Set(nameof(AuthEndpoint), AuthEndpoint)
			.Set(nameof(MarketSocketEndpoint), MarketSocketEndpoint)
			.Set(nameof(AccountSocketEndpoint), AccountSocketEndpoint)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(OrderExpiry), OrderExpiry);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Environment = storage.GetValue(nameof(Environment), Environment);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		ExchangeEndpoint = storage.GetValue<string>(nameof(ExchangeEndpoint));
		TradeEndpoint = storage.GetValue<string>(nameof(TradeEndpoint));
		AuthEndpoint = storage.GetValue<string>(nameof(AuthEndpoint));
		MarketSocketEndpoint = storage.GetValue<string>(
			nameof(MarketSocketEndpoint));
		AccountSocketEndpoint = storage.GetValue<string>(
			nameof(AccountSocketEndpoint));
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
		OrderExpiry = storage.GetValue(nameof(OrderExpiry), OrderExpiry);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new BluefinMessageAdapter(TransactionIdGenerator)
		{
			Environment = Environment,
			WalletAddress = WalletAddress,
			PrivateKey = PrivateKey,
			ExchangeEndpoint = ExchangeEndpoint,
			TradeEndpoint = TradeEndpoint,
			AuthEndpoint = AuthEndpoint,
			MarketSocketEndpoint = MarketSocketEndpoint,
			AccountSocketEndpoint = AccountSocketEndpoint,
			PollingInterval = PollingInterval,
			HistoryLimit = HistoryLimit,
			MarketDepth = MarketDepth,
			OrderExpiry = OrderExpiry,
		};
}
