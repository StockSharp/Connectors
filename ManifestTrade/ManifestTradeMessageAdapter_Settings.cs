namespace StockSharp.ManifestTrade;

/// <summary>The message adapter for Manifest Trade.</summary>
[MediaIcon(Media.MediaNames.manifest_trade)]
[Doc("topics/api/connectors/crypto_exchanges/manifest_trade.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ManifestTradeKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
public partial class ManifestTradeMessageAdapter : MessageAdapter
{
	private const string _defaultStatsEndpoint =
		"https://mfx-stats-mainnet.fly.dev";

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		ManifestTradeExtensions.TimeFrames;

	/// <summary>Solana cluster.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		Description = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public ManifestTradeClusters Cluster { get; set; } =
		ManifestTradeClusters.Mainnet;

	/// <summary>HTTP Solana JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RpcEndpoint { get; set; }

	/// <summary>Solana WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string StreamingEndpoint { get; set; }

	/// <summary>Manifest Trade public market-discovery endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string StatsEndpoint { get; set; } = _defaultStatsEndpoint;

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
	/// Semicolon-separated <c>market|base symbol|quote symbol</c> entries.
	/// Symbol overrides are optional.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string Markets { get; set; }

	private int _maximumDiscoveredMarkets = 20;

	/// <summary>Maximum top-volume markets loaded from the stats API.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int MaximumDiscoveredMarkets
	{
		get => _maximumDiscoveredMarkets;
		set => _maximumDiscoveredMarkets = value is >= 0 and <= 200
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Discovery market limit must be between zero and 200.");
	}

	private int _marketDepth = 50;

	/// <summary>Maximum number of bid and ask levels published.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		Description = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Market depth must be between one and 1000 levels.");
	}

	private decimal _slippageTolerance = 0.5m;

	/// <summary>Market-order slippage tolerance in percent.</summary>
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
				"Slippage must be greater than zero and no more than 50 " +
				"percent, with at most two decimal places.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Polling fallback interval.</summary>
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

	private int _maximumHistoryTransactions = 200;

	/// <summary>Maximum transactions inspected per history request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public int MaximumHistoryTransactions
	{
		get => _maximumHistoryTransactions;
		set => _maximumHistoryTransactions = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History transaction limit must be between one and 1000.");
	}

	private int _computeUnitLimit = 400_000;

	/// <summary>Compute-unit limit attached to transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LimitKey,
		Description = LocalizedStrings.LimitKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public int ComputeUnitLimit
	{
		get => _computeUnitLimit;
		set => _computeUnitLimit = value is >= 50_000 and <= 1_400_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Compute-unit limit must be between 50000 and 1400000.");
	}

	private long _computeUnitPrice;

	/// <summary>Priority fee in micro-lamports; zero selects RPC median.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CommissionKey,
		Description = LocalizedStrings.CommissionKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 10)]
	public long ComputeUnitPrice
	{
		get => _computeUnitPrice;
		set => _computeUnitPrice = value is >= 0 and <= 1_000_000_000_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Compute-unit price must be between zero and one trillion.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Cluster), Cluster)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(StreamingEndpoint), StreamingEndpoint)
			.Set(nameof(StatsEndpoint), StatsEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(Markets), Markets)
			.Set(nameof(MaximumDiscoveredMarkets), MaximumDiscoveredMarkets)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
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
		StatsEndpoint = storage.GetValue(nameof(StatsEndpoint), StatsEndpoint);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		Markets = storage.GetValue<string>(nameof(Markets));
		MaximumDiscoveredMarkets = storage.GetValue(
			nameof(MaximumDiscoveredMarkets), MaximumDiscoveredMarkets);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
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
