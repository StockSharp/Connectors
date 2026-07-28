namespace StockSharp.ZeroX;

/// <summary>Supported 0x Swap API v2 production networks.</summary>
public enum ZeroXChains
{
	/// <summary>Ethereum Mainnet.</summary>
	Ethereum = 1,
	/// <summary>Optimism.</summary>
	Optimism = 10,
	/// <summary>BNB Smart Chain.</summary>
	Bnb = 56,
	/// <summary>Polygon PoS.</summary>
	Polygon = 137,
	/// <summary>Base.</summary>
	Base = 8453,
	/// <summary>Arbitrum One.</summary>
	Arbitrum = 42161,
	/// <summary>Avalanche C-Chain.</summary>
	Avalanche = 43114,
	/// <summary>Linea.</summary>
	Linea = 59144,
}

/// <summary>The message adapter for the 0x Swap API v2.</summary>
[MediaIcon(Media.MediaNames.zero_x)]
[Doc("topics/api/connectors/crypto_exchanges/zero_x.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ZeroXKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Transactions)]
public partial class ZeroXMessageAdapter : MessageAdapter
{
	private const string _defaultApiEndpoint =
		"https://api.0x.org";

	/// <summary>0x Dashboard API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString ApiKey { get; set; }

	/// <summary>Production network.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		Description = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public ZeroXChains Chain
	{
		get => _chain;
		set
		{
			if (!System.Enum.IsDefined(value))
				throw new ArgumentOutOfRangeException(nameof(value), value,
					"Unsupported 0x chain.");
			var previousDefault = _chain.GetDefaultRpcEndpoint();
			_chain = value;
			if (RpcEndpoint.IsEmpty() ||
				RpcEndpoint.EqualsIgnoreCase(previousDefault))
				RpcEndpoint = value.GetDefaultRpcEndpoint();
		}
	}
	private ZeroXChains _chain = ZeroXChains.Ethereum;

	/// <summary>Public wallet address used for quotes and balances.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional private key used to sign swaps and approvals.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>0x API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string ApiEndpoint { get; set; } = _defaultApiEndpoint;

	/// <summary>EVM HTTP JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string RpcEndpoint { get; set; } =
		ZeroXChains.Ethereum.GetDefaultRpcEndpoint();

	/// <summary>
	/// Semicolon-separated market definitions in
	/// base-token|quote-token|security-code format. The security code is
	/// optional.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string Markets { get; set; }

	private decimal _probeVolume = 0.01m;

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

	/// <summary>Maximum swap slippage in percent.</summary>
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

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Polling interval for quotes, balances, and receipts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(1)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Polling interval cannot be less than one second.");
	}

	private TimeSpan _receiptTimeout = TimeSpan.FromMinutes(3);

	/// <summary>Maximum time to wait for approvals to be mined.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		Description = LocalizedStrings.TimeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public TimeSpan ReceiptTimeout
	{
		get => _receiptTimeout;
		set => _receiptTimeout = value >= TimeSpan.FromSeconds(30) &&
			value <= TimeSpan.FromMinutes(15)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Receipt timeout must be between 30 seconds and 15 minutes.");
	}

	/// <summary>Automatically approve the 0x router when required.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AutoKey,
		Description = LocalizedStrings.AutoKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public bool IsAutoApprove { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(Chain), Chain)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(Markets), Markets)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(ReceiptTimeout), ReceiptTimeout)
			.Set(nameof(IsAutoApprove), IsAutoApprove);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
		Chain = storage.GetValue(nameof(Chain), Chain);
		if (!System.Enum.IsDefined(Chain))
			throw new InvalidDataException($"Unsupported 0x chain '{Chain}'.");
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		ApiEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(ApiEndpoint), ApiEndpoint)) ?? _defaultApiEndpoint;
		RpcEndpoint = NormalizeEndpoint(storage.GetValue<string>(
			nameof(RpcEndpoint))) ?? Chain.GetDefaultRpcEndpoint();
		Markets = storage.GetValue<string>(nameof(Markets));
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
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
		=> base.ToString() + $": {Chain}, Wallet={WalletAddress}";
}
