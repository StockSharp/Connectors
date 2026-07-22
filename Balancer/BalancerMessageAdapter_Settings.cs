namespace StockSharp.Balancer;

/// <summary>The message adapter for Balancer V2 and V3 pools.</summary>
[MediaIcon(Media.MediaNames.balancer)]
[Doc("topics/api/connectors/crypto_exchanges/balancer.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BalancerKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class BalancerMessageAdapter : MessageAdapter
{
	private const string _defaultApiEndpoint = "https://api-v3.balancer.fi";
	private BalancerNetworks _network = BalancerNetworks.Ethereum;
	private string _rpcEndpoint = BalancerNetworks.Ethereum
		.GetDeployment().RpcEndpoint;
	private string _webSocketEndpoint = BalancerNetworks.Ethereum
		.GetDeployment().WebSocketEndpoint;

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		BalancerExtensions.TimeFrames;

	/// <summary>Balancer production network.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public BalancerNetworks Network
	{
		get => _network;
		set
		{
			var previous = _network.GetDeployment();
			var next = value.GetDeployment();
			if (_rpcEndpoint.EqualsIgnoreCase(previous.RpcEndpoint))
				_rpcEndpoint = next.RpcEndpoint;
			if (_webSocketEndpoint.EqualsIgnoreCase(previous.WebSocketEndpoint))
				_webSocketEndpoint = next.WebSocketEndpoint;
			_network = value;
		}
	}

	/// <summary>Public wallet address used for balances.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional private key used for local transaction signing.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>EVM HTTP JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	[BasicSetting]
	public string RpcEndpoint
	{
		get => _rpcEndpoint;
		set => _rpcEndpoint = value;
	}

	/// <summary>EVM WebSocket JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	[BasicSetting]
	public string WebSocketEndpoint
	{
		get => _webSocketEndpoint;
		set => _webSocketEndpoint = value;
	}

	/// <summary>Official Balancer GraphQL API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string ApiEndpoint { get; set; } = _defaultApiEndpoint;

	/// <summary>
	/// Optional semicolon-separated definitions using
	/// pool or pool|base-token|quote-token|security-code format.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public string Pools { get; set; }

	private int _maximumDiscoveredPools = 10;

	/// <summary>Maximum number of high-TVL pools discovered automatically.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
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
		Order = 8)]
	public decimal MinimumPoolTvl
	{
		get => _minimumPoolTvl;
		set => _minimumPoolTvl = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Minimum pool TVL cannot be negative.");
	}

	private int _historyMaximum = 1_000;

	/// <summary>Maximum historical swaps requested per query.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public int HistoryMaximum
	{
		get => _historyMaximum;
		set => _historyMaximum = value is >= 1 and <= 1_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History maximum must be between one and 1000.");
	}

	private decimal _probeVolume = 1m;

	/// <summary>Base-token amount used for executable Level1 probes.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 10)]
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
		Order = 11)]
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
		Order = 12)]
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
		Order = 13)]
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
			.Set(nameof(Network), Network)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
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
		Network = storage.GetValue(nameof(Network), Network);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		RpcEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(RpcEndpoint), RpcEndpoint), "https");
		WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(WebSocketEndpoint), WebSocketEndpoint), "wss");
		ApiEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(ApiEndpoint), ApiEndpoint), "https");
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
		ReceiptTimeout = storage.GetValue(nameof(ReceiptTimeout), ReceiptTimeout);
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
		=> base.ToString() + $": {Network}, Wallet={WalletAddress}";
}
