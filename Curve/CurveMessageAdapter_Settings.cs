namespace StockSharp.Curve;

/// <summary>
/// The message adapter for Curve pools on Ethereum.
/// </summary>
[MediaIcon(Media.MediaNames.curve)]
[Doc("topics/api/connectors/crypto_exchanges/curve.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CurveKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class CurveMessageAdapter : MessageAdapter
{
	private const string _defaultRpcEndpoint =
		"https://ethereum-rpc.publicnode.com";
	private const string _defaultWebSocketEndpoint =
		"wss://ethereum-rpc.publicnode.com";
	private const string _defaultApiEndpoint =
		"https://api.curve.finance/v1";
	private const string _defaultPricesEndpoint =
		"https://prices.curve.finance";

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		CurveExtensions.TimeFrames;

	/// <summary>Public wallet address used for balances.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional private key used to sign transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Ethereum HTTP JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RpcEndpoint { get; set; } = _defaultRpcEndpoint;

	/// <summary>Ethereum WebSocket JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } =
		_defaultWebSocketEndpoint;

	/// <summary>Official Curve pools API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string ApiEndpoint { get; set; } = _defaultApiEndpoint;

	/// <summary>Official Curve Prices API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string PricesEndpoint { get; set; } = _defaultPricesEndpoint;

	/// <summary>Curve Router NG contract address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string RouterAddress { get; set; } =
		CurveExtensions.DefaultRouterAddress;

	/// <summary>
	/// Optional semicolon-separated pool definitions. Each item uses
	/// pool or pool|base-token|quote-token|security-code format.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public string Pools { get; set; }

	private int _maximumDiscoveredPools = 10;

	/// <summary>Maximum number of high-TVL pools discovered automatically.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public int MaximumDiscoveredPools
	{
		get => _maximumDiscoveredPools;
		set => _maximumDiscoveredPools = value is >= 0 and <= 50
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Discovered pool count must be between zero and 50.");
	}

	private decimal _minimumPoolTvl = 100_000m;

	/// <summary>Minimum pool TVL in USD for automatic discovery.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceKey,
		Description = LocalizedStrings.PriceKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public decimal MinimumPoolTvl
	{
		get => _minimumPoolTvl;
		set => _minimumPoolTvl = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Minimum pool TVL cannot be negative.");
	}

	private int _historyMaximum = 1_000;

	/// <summary>Maximum trades requested from Curve Prices per query.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int HistoryMaximum
	{
		get => _historyMaximum;
		set => _historyMaximum = value is >= 1 and <= 1_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History maximum must be between one and 1000.");
	}

	private decimal _probeVolume = 1m;

	/// <summary>Base-token amount used for Level1 quote probes.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
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

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Fallback polling interval.</summary>
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

	private TimeSpan _receiptTimeout = TimeSpan.FromMinutes(3);

	/// <summary>Transaction receipt wait timeout.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ConnectionTimeoutKey,
		Description = LocalizedStrings.ConnectionTimeoutKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public TimeSpan ReceiptTimeout
	{
		get => _receiptTimeout;
		set => _receiptTimeout = value >= TimeSpan.FromSeconds(10)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Receipt timeout cannot be less than ten seconds.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(PricesEndpoint), PricesEndpoint)
			.Set(nameof(RouterAddress), RouterAddress)
			.Set(nameof(Pools), Pools)
			.Set(nameof(MaximumDiscoveredPools), MaximumDiscoveredPools)
			.Set(nameof(MinimumPoolTvl), MinimumPoolTvl)
			.Set(nameof(HistoryMaximum), HistoryMaximum)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(ReceiptTimeout), ReceiptTimeout);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		RpcEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(RpcEndpoint), RpcEndpoint), "https");
		WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(WebSocketEndpoint), WebSocketEndpoint), "wss");
		ApiEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(ApiEndpoint), ApiEndpoint), "https");
		PricesEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(PricesEndpoint), PricesEndpoint), "https");
		RouterAddress = storage.GetValue(nameof(RouterAddress), RouterAddress)
			.NormalizeAddress();
		Pools = storage.GetValue<string>(nameof(Pools));
		MaximumDiscoveredPools = storage.GetValue(
			nameof(MaximumDiscoveredPools), MaximumDiscoveredPools);
		MinimumPoolTvl = storage.GetValue(nameof(MinimumPoolTvl),
			MinimumPoolTvl);
		HistoryMaximum = storage.GetValue(nameof(HistoryMaximum),
			HistoryMaximum);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		ReceiptTimeout = storage.GetValue(nameof(ReceiptTimeout),
			ReceiptTimeout);
	}

	private static string NormalizeEndpoint(string endpoint, string scheme)
	{
		endpoint = endpoint?.Trim();
		if (endpoint.IsEmpty())
			return endpoint;
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Ethereum, Wallet={WalletAddress}";
}
