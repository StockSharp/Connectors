namespace StockSharp.Meteora;

/// <summary>The message adapter for Meteora DLMM.</summary>
[MediaIcon(Media.MediaNames.meteora)]
[Doc("topics/api/connectors/crypto_exchanges/meteora.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MeteoraKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class MeteoraMessageAdapter : MessageAdapter
{
	private const string _defaultApiEndpoint =
		"https://dlmm.datapi.meteora.ag";

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		MeteoraExtensions.TimeFrames;

	/// <summary>Solana cluster.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		Description = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public MeteoraClusters Cluster { get; set; } = MeteoraClusters.Mainnet;

	/// <summary>HTTP Solana JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RpcEndpoint { get; set; }

	/// <summary>Solana WebSocket endpoint used for transaction logs.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string StreamingEndpoint { get; set; }

	/// <summary>Official Meteora public API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string ApiEndpoint { get; set; } = _defaultApiEndpoint;

	/// <summary>Optional public Solana wallet address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional base58 Solana private key used for trading.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>
	/// Semicolon-separated <c>pool|base symbol|quote symbol</c> definitions.
	/// Symbol overrides are optional.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string Pools { get; set; }

	private int _maximumDiscoveredPools = 20;

	/// <summary>Maximum top-volume pools loaded from the official API.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int MaximumDiscoveredPools
	{
		get => _maximumDiscoveredPools;
		set => _maximumDiscoveredPools = value is >= 0 and <= 100
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Discovery pool limit must be between zero and 100.");
	}

	private decimal _probeVolume = 1m;

	/// <summary>Base-token amount used for executable quote probes.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public decimal ProbeVolume
	{
		get => _probeVolume;
		set => _probeVolume = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Quote probe volume must be positive.");
	}

	private decimal _slippageTolerance = 0.5m;

	/// <summary>Swap slippage tolerance in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public decimal SlippageTolerance
	{
		get => _slippageTolerance;
		set => _slippageTolerance = value is > 0 and <= 50 &&
			decimal.Round(value, 2) == value
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Slippage tolerance must be greater than zero and no more " +
				"than 50 percent, with at most two decimal places.");
	}

	private int _maximumBinArraysPerSide = 6;

	/// <summary>Maximum initialized bin arrays loaded in each direction.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		Description = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public int MaximumBinArraysPerSide
	{
		get => _maximumBinArraysPerSide;
		set => _maximumBinArraysPerSide = value is >= 1 and <= 20
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Bin-array count must be between one and 20 per side.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Polling interval for state, history, and receipts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(1)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Polling interval cannot be less than one second.");
	}

	private int _maximumHistoryTransactions = 100;

	/// <summary>Maximum pool transactions fetched per history request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public int MaximumHistoryTransactions
	{
		get => _maximumHistoryTransactions;
		set => _maximumHistoryTransactions = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History transaction limit must be between 1 and 1000.");
	}

	private int _computeUnitLimit = 350_000;

	/// <summary>Compute-unit limit attached to swap transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LimitKey,
		Description = LocalizedStrings.LimitKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 10)]
	public int ComputeUnitLimit
	{
		get => _computeUnitLimit;
		set => _computeUnitLimit = value is >= 50_000 and <= 1_400_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Compute-unit limit must be between 50000 and 1400000.");
	}

	private long _computeUnitPrice;

	/// <summary>
	/// Priority fee in micro-lamports per compute unit. Zero selects the
	/// current RPC median.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CommissionKey,
		Description = LocalizedStrings.CommissionKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 11)]
	public long ComputeUnitPrice
	{
		get => _computeUnitPrice;
		set => _computeUnitPrice = value is >= 0 and <= 1_000_000_000_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Compute-unit price must be between zero and one trillion " +
				"micro-lamports.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Cluster), Cluster)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(StreamingEndpoint), StreamingEndpoint)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(Pools), Pools)
			.Set(nameof(MaximumDiscoveredPools), MaximumDiscoveredPools)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(MaximumBinArraysPerSide), MaximumBinArraysPerSide)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(MaximumHistoryTransactions), MaximumHistoryTransactions)
			.Set(nameof(ComputeUnitLimit), ComputeUnitLimit)
			.Set(nameof(ComputeUnitPrice), ComputeUnitPrice);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Cluster = storage.GetValue(nameof(Cluster), Cluster);
		RpcEndpoint = storage.GetValue<string>(nameof(RpcEndpoint));
		StreamingEndpoint = storage.GetValue<string>(nameof(StreamingEndpoint));
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		Pools = storage.GetValue<string>(nameof(Pools));
		MaximumDiscoveredPools = storage.GetValue(
			nameof(MaximumDiscoveredPools), MaximumDiscoveredPools);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		MaximumBinArraysPerSide = storage.GetValue(
			nameof(MaximumBinArraysPerSide), MaximumBinArraysPerSide);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		MaximumHistoryTransactions = storage.GetValue(
			nameof(MaximumHistoryTransactions), MaximumHistoryTransactions);
		ComputeUnitLimit = storage.GetValue(nameof(ComputeUnitLimit),
			ComputeUnitLimit);
		ComputeUnitPrice = storage.GetValue(nameof(ComputeUnitPrice),
			ComputeUnitPrice);
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Cluster={Cluster}, Wallet={WalletAddress}";
}
