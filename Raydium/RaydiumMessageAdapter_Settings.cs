namespace StockSharp.Raydium;

/// <summary>The message adapter for Raydium.</summary>
[MediaIcon(Media.MediaNames.raydium)]
[Doc("topics/api/connectors/crypto_exchanges/raydium.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.RaydiumKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
public partial class RaydiumMessageAdapter : MessageAdapter
{
	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		RaydiumExtensions.TimeFrames;

	/// <summary>Solana cluster.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		Description = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public RaydiumClusters Cluster { get; set; } = RaydiumClusters.Mainnet;

	/// <summary>HTTP Solana JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RpcEndpoint { get; set; }

	/// <summary>Solana WebSocket endpoint used for program logs.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string StreamingEndpoint { get; set; }

	/// <summary>Official Raydium API v3 endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string ApiEndpoint { get; set; }

	/// <summary>Official Raydium Trade API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string TradeEndpoint { get; set; }

	/// <summary>Optional public Solana wallet address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional base58 Solana private key used for swaps.</summary>
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

	/// <summary>Maximum top-volume pools loaded from API v3.</summary>
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

	private int _depthLevelCount = 5;

	/// <summary>Maximum executable quote levels in synthetic depth.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		Description = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public int DepthLevelCount
	{
		get => _depthLevelCount;
		set => _depthLevelCount = value is >= 1 and <= 10
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Depth level count must be between one and ten.");
	}

	private decimal _slippageTolerance = 0.5m;

	/// <summary>Swap slippage tolerance in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
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

	/// <summary>
	/// Whether wrapped SOL is automatically converted to or from native SOL.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AutoKey,
		Description = LocalizedStrings.AutoKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public bool IsNativeSolUsed { get; set; } = true;

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>Polling interval for quotes, history, and receipts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(1)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Polling interval cannot be less than one second.");
	}

	private int _maximumHistoryTransactions = 100;

	/// <summary>Maximum pool transactions inspected per history request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 10)]
	public int MaximumHistoryTransactions
	{
		get => _maximumHistoryTransactions;
		set => _maximumHistoryTransactions = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History transaction limit must be between 1 and 1000.");
	}

	private long _computeUnitPrice;

	/// <summary>
	/// Priority fee in micro-lamports per compute unit. Zero uses Raydium's
	/// current automatic fee.
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

	/// <summary>Automatic priority-fee level used when the price is zero.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LevelKey,
		Description = LocalizedStrings.LevelKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 12)]
	public RaydiumPriorityFeeLevels PriorityFeeLevel { get; set; } =
		RaydiumPriorityFeeLevels.High;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Cluster), Cluster)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(StreamingEndpoint), StreamingEndpoint)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(TradeEndpoint), TradeEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(Pools), Pools)
			.Set(nameof(MaximumDiscoveredPools), MaximumDiscoveredPools)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(DepthLevelCount), DepthLevelCount)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(IsNativeSolUsed), IsNativeSolUsed)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(MaximumHistoryTransactions), MaximumHistoryTransactions)
			.Set(nameof(ComputeUnitPrice), ComputeUnitPrice)
			.Set(nameof(PriorityFeeLevel), PriorityFeeLevel);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Cluster = storage.GetValue(nameof(Cluster), Cluster);
		RpcEndpoint = storage.GetValue<string>(nameof(RpcEndpoint));
		StreamingEndpoint = storage.GetValue<string>(nameof(StreamingEndpoint));
		ApiEndpoint = storage.GetValue<string>(nameof(ApiEndpoint));
		TradeEndpoint = storage.GetValue<string>(nameof(TradeEndpoint));
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		Pools = storage.GetValue<string>(nameof(Pools));
		MaximumDiscoveredPools = storage.GetValue(
			nameof(MaximumDiscoveredPools), MaximumDiscoveredPools);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		DepthLevelCount = storage.GetValue(nameof(DepthLevelCount),
			DepthLevelCount);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		IsNativeSolUsed = storage.GetValue(nameof(IsNativeSolUsed),
			IsNativeSolUsed);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		MaximumHistoryTransactions = storage.GetValue(
			nameof(MaximumHistoryTransactions), MaximumHistoryTransactions);
		ComputeUnitPrice = storage.GetValue(nameof(ComputeUnitPrice),
			ComputeUnitPrice);
		PriorityFeeLevel = storage.GetValue(nameof(PriorityFeeLevel),
			PriorityFeeLevel);
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Cluster={Cluster}, Wallet={WalletAddress}";
}
