namespace StockSharp.KyberSwap;

/// <summary>Supported KyberSwap Aggregator production networks.</summary>
public enum KyberSwapChains
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

/// <summary>The message adapter for the KyberSwap Aggregator API v1.</summary>
[MediaIcon(Media.MediaNames.kyber_swap)]
[Doc("topics/api/connectors/crypto_exchanges/kyber_swap.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KyberSwapKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Transactions)]
public partial class KyberSwapMessageAdapter : MessageAdapter
{
	private const string _defaultApiEndpoint =
		"https://aggregator-api.kyberswap.com";

	private string _clientId = "StockSharp";

	/// <summary>Client identifier sent to the KyberSwap Aggregator.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IdKey,
		Description = LocalizedStrings.IdKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string ClientId
	{
		get => _clientId;
		set
		{
			value = value.ThrowIfEmpty(nameof(value)).Trim();
			if (value.Length > 64 || value.Any(static ch =>
				!char.IsLetterOrDigit(ch) &&
				ch is not '-' and not '_' and not '.'))
				throw new ArgumentException(
					"KyberSwap client id must contain at most 64 letters, " +
						"digits, dots, underscores, or hyphens.",
					nameof(value));
			_clientId = value;
		}
	}

	/// <summary>Production network.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		Description = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public KyberSwapChains Chain
	{
		get => _chain;
		set
		{
			if (!System.Enum.IsDefined(value))
				throw new ArgumentOutOfRangeException(nameof(value), value,
					"Unsupported KyberSwap chain.");
			var previousDefault = _chain.GetDefaultRpcEndpoint();
			_chain = value;
			if (RpcEndpoint.IsEmpty() ||
				RpcEndpoint.EqualsIgnoreCase(previousDefault))
				RpcEndpoint = value.GetDefaultRpcEndpoint();
		}
	}
	private KyberSwapChains _chain = KyberSwapChains.Ethereum;

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

	/// <summary>KyberSwap Aggregator API endpoint.</summary>
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
		KyberSwapChains.Ethereum.GetDefaultRpcEndpoint();

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
		set => _slippageTolerance = value is >= 0 and <= 20 &&
			decimal.Round(value, 3) == value
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Slippage tolerance must be between zero and 20 percent, " +
				"with at most three decimal places.");
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

	private TimeSpan _transactionLifetime = TimeSpan.FromMinutes(5);

	/// <summary>Lifetime of a built KyberSwap transaction.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		Description = LocalizedStrings.TimeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public TimeSpan TransactionLifetime
	{
		get => _transactionLifetime;
		set => _transactionLifetime =
			value >= TimeSpan.FromSeconds(30) &&
			value <= TimeSpan.FromMinutes(20)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Transaction lifetime must be between 30 seconds and " +
						"20 minutes.");
	}

	/// <summary>Automatically approve the KyberSwap router when required.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AutoKey,
		Description = LocalizedStrings.AutoKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 10)]
	public bool IsAutoApprove { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ClientId), ClientId)
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
			.Set(nameof(TransactionLifetime), TransactionLifetime)
			.Set(nameof(IsAutoApprove), IsAutoApprove);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ClientId = storage.GetValue(nameof(ClientId), ClientId);
		Chain = storage.GetValue(nameof(Chain), Chain);
		if (!System.Enum.IsDefined(Chain))
			throw new InvalidDataException(
				$"Unsupported KyberSwap chain '{Chain}'.");
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
		TransactionLifetime = storage.GetValue(nameof(TransactionLifetime),
			TransactionLifetime);
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
