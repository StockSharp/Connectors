namespace StockSharp.Drift;

public partial class DriftMessageAdapter
{
	/// <summary>Optional public Solana authority wallet address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional base58 Solana keypair used for transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Optional Drift subaccount public key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string AccountAddress { get; set; }

	/// <summary>Current hosted Data API endpoint.</summary>
	[Display(
		Name = "Data API",
		Description = "Drift Data API endpoint.",
		GroupName = "Addresses",
		Order = 0)]
	public string DataApiEndpoint { get; set; } =
		"https://data.velocity.exchange";

	/// <summary>Current hosted Data API WebSocket endpoint.</summary>
	[Display(
		Name = "Data WebSocket",
		Description = "Drift Data API WebSocket endpoint.",
		GroupName = "Addresses",
		Order = 1)]
	public string DataSocketEndpoint { get; set; } =
		"wss://data.velocity.exchange/ws";

	/// <summary>Current hosted DLOB endpoint.</summary>
	[Display(
		Name = "DLOB API",
		Description = "Drift DLOB REST endpoint.",
		GroupName = "Addresses",
		Order = 2)]
	public string DlobEndpoint { get; set; } =
		"https://dlob.velocity.exchange";

	/// <summary>Current hosted DLOB WebSocket endpoint.</summary>
	[Display(
		Name = "DLOB WebSocket",
		Description = "Drift DLOB WebSocket endpoint.",
		GroupName = "Addresses",
		Order = 3)]
	public string DlobSocketEndpoint { get; set; } =
		"wss://dlob.velocity.exchange/ws";

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Polling interval for account state.</summary>
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
				"Drift polling interval cannot be less than two seconds.");
	}

	private int _historyLimit = 100;

	/// <summary>Maximum number of historical records per request.</summary>
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
				"Drift history limit must be between one and 1000.");
	}

	private int _marketDepth = 100;

	/// <summary>Maximum DLOB depth.</summary>
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
				"Drift market depth must be between one and 1000.");
	}

	/// <summary>Whether prepared transactions are simulated by the API.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EnableSimulatorKey,
		Description = LocalizedStrings.EnableSimulatorKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public bool IsSimulationEnabled { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(AccountAddress), AccountAddress)
			.Set(nameof(DataApiEndpoint), DataApiEndpoint)
			.Set(nameof(DataSocketEndpoint), DataSocketEndpoint)
			.Set(nameof(DlobEndpoint), DlobEndpoint)
			.Set(nameof(DlobSocketEndpoint), DlobSocketEndpoint)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(IsSimulationEnabled), IsSimulationEnabled);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		AccountAddress = storage.GetValue<string>(nameof(AccountAddress));
		DataApiEndpoint = storage.GetValue(nameof(DataApiEndpoint),
			DataApiEndpoint);
		DataSocketEndpoint = storage.GetValue(nameof(DataSocketEndpoint),
			DataSocketEndpoint);
		DlobEndpoint = storage.GetValue(nameof(DlobEndpoint), DlobEndpoint);
		DlobSocketEndpoint = storage.GetValue(nameof(DlobSocketEndpoint),
			DlobSocketEndpoint);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
		IsSimulationEnabled = storage.GetValue(nameof(IsSimulationEnabled),
			IsSimulationEnabled);
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Wallet={WalletAddress}, Account={AccountAddress}";
}
