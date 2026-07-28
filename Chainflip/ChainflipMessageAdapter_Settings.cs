namespace StockSharp.Chainflip;

/// <summary>
/// The message adapter for the Chainflip cross-chain liquidity network.
/// </summary>
[MediaIcon(Media.MediaNames.chainflip)]
[Doc("topics/api/connectors/crypto_exchanges/chainflip.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ChainflipKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Transactions)]
public partial class ChainflipMessageAdapter : MessageAdapter
{
	private const string _defaultStateRpcEndpoint =
		"https://rpc.mainnet.chainflip.io";
	private const string _defaultBackendEndpoint =
		"https://chainflip-swap.chainflip.io";

	/// <summary>Chainflip State Chain HTTP JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string StateRpcEndpoint { get; set; } =
		_defaultStateRpcEndpoint;

	/// <summary>Chainflip quote and swap backend endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string BackendEndpoint { get; set; } =
		_defaultBackendEndpoint;

	/// <summary>Ethereum HTTP JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string EthereumRpcEndpoint { get; set; } =
		"Ethereum".GetDefaultRpcEndpoint();

	/// <summary>Arbitrum HTTP JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	[BasicSetting]
	public string ArbitrumRpcEndpoint { get; set; } =
		"Arbitrum".GetDefaultRpcEndpoint();

	/// <summary>Optional public EVM wallet address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional EVM private key used to sign vault swaps.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Destination Bitcoin address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public string BitcoinAddress { get; set; }

	/// <summary>Destination Solana address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string SolanaAddress { get; set; }

	/// <summary>Destination Asset Hub address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string AssethubAddress { get; set; }

	/// <summary>Destination Polkadot address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public string PolkadotAddress { get; set; }

	/// <summary>Destination Tron address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public string TronAddress { get; set; }

	/// <summary>
	/// Optional semicolon-separated security codes or asset-chain pairs.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public string Pools { get; set; }

	private decimal _probeVolume = 0.1m;

	/// <summary>Base-asset amount used for connection quote checks.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public decimal ProbeVolume
	{
		get => _probeVolume;
		set => _probeVolume = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Chainflip probe volume must be positive.");
	}

	private int _orderBookDepth = 100;

	/// <summary>Maximum number of aggregated levels per book side.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		Description = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public int OrderBookDepth
	{
		get => _orderBookDepth;
		set => _orderBookDepth = value is >= 1 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Chainflip order-book depth must be between 1 and 5000.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(6);

	/// <summary>Polling interval for public and private state.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 10)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(3)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Chainflip polling interval cannot be less than three " +
					"seconds.");
	}

	private int _maxBlocksPerPoll = 100;

	/// <summary>Maximum State Chain blocks processed per polling pass.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 11)]
	public int MaxBlocksPerPoll
	{
		get => _maxBlocksPerPoll;
		set => _maxBlocksPerPoll = value is >= 1 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Chainflip block polling limit must be between 1 and 5000.");
	}

	private int _initialTickBlocks = 50;

	/// <summary>Recent blocks replayed when the first tick feed starts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 12)]
	public int InitialTickBlocks
	{
		get => _initialTickBlocks;
		set => _initialTickBlocks = value is >= 0 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Chainflip initial tick block count must be between 0 and " +
					"5000.");
	}

	private decimal _slippageTolerance = 1.25m;

	/// <summary>Maximum market-swap slippage in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 13)]
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

	private int _retryDurationBlocks = 300;

	/// <summary>Fill-or-kill retry duration in State Chain blocks.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 14)]
	public int RetryDurationBlocks
	{
		get => _retryDurationBlocks;
		set => _retryDurationBlocks = value is >= 1 and <= 14400
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Chainflip retry duration must be between 1 and 14400 " +
					"blocks.");
	}

	private TimeSpan _receiptTimeout = TimeSpan.FromMinutes(5);

	/// <summary>Maximum time to wait for approval transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		Description = LocalizedStrings.TimeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 15)]
	public TimeSpan ReceiptTimeout
	{
		get => _receiptTimeout;
		set => _receiptTimeout = value >= TimeSpan.FromSeconds(30) &&
			value <= TimeSpan.FromMinutes(30)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Receipt timeout must be between 30 seconds and 30 " +
						"minutes.");
	}

	/// <summary>Automatically approve the Chainflip vault when required.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AutoKey,
		Description = LocalizedStrings.AutoKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 16)]
	public bool IsAutoApprove { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(StateRpcEndpoint), StateRpcEndpoint)
			.Set(nameof(BackendEndpoint), BackendEndpoint)
			.Set(nameof(EthereumRpcEndpoint), EthereumRpcEndpoint)
			.Set(nameof(ArbitrumRpcEndpoint), ArbitrumRpcEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(BitcoinAddress), BitcoinAddress)
			.Set(nameof(SolanaAddress), SolanaAddress)
			.Set(nameof(AssethubAddress), AssethubAddress)
			.Set(nameof(PolkadotAddress), PolkadotAddress)
			.Set(nameof(TronAddress), TronAddress)
			.Set(nameof(Pools), Pools)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(OrderBookDepth), OrderBookDepth)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(MaxBlocksPerPoll), MaxBlocksPerPoll)
			.Set(nameof(InitialTickBlocks), InitialTickBlocks)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(RetryDurationBlocks), RetryDurationBlocks)
			.Set(nameof(ReceiptTimeout), ReceiptTimeout)
			.Set(nameof(IsAutoApprove), IsAutoApprove);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		StateRpcEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(StateRpcEndpoint), StateRpcEndpoint)) ??
			_defaultStateRpcEndpoint;
		BackendEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(BackendEndpoint), BackendEndpoint)) ??
			_defaultBackendEndpoint;
		EthereumRpcEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(EthereumRpcEndpoint), EthereumRpcEndpoint)) ??
			"Ethereum".GetDefaultRpcEndpoint();
		ArbitrumRpcEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(ArbitrumRpcEndpoint), ArbitrumRpcEndpoint)) ??
			"Arbitrum".GetDefaultRpcEndpoint();
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		BitcoinAddress = storage.GetValue<string>(nameof(BitcoinAddress));
		SolanaAddress = storage.GetValue<string>(nameof(SolanaAddress));
		AssethubAddress = storage.GetValue<string>(nameof(AssethubAddress));
		PolkadotAddress = storage.GetValue<string>(nameof(PolkadotAddress));
		TronAddress = storage.GetValue<string>(nameof(TronAddress));
		Pools = storage.GetValue<string>(nameof(Pools));
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		OrderBookDepth = storage.GetValue(nameof(OrderBookDepth),
			OrderBookDepth);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		MaxBlocksPerPoll = storage.GetValue(nameof(MaxBlocksPerPoll),
			MaxBlocksPerPoll);
		InitialTickBlocks = storage.GetValue(nameof(InitialTickBlocks),
			InitialTickBlocks);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		RetryDurationBlocks = storage.GetValue(nameof(RetryDurationBlocks),
			RetryDurationBlocks);
		ReceiptTimeout = storage.GetValue(nameof(ReceiptTimeout),
			ReceiptTimeout);
		IsAutoApprove = storage.GetValue(nameof(IsAutoApprove),
			IsAutoApprove);
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint?.Trim();
		if (endpoint.IsEmpty())
			return null;
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Wallet={WalletAddress}";
}
