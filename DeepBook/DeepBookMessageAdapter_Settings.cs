namespace StockSharp.DeepBook;

/// <summary>The message adapter for the DeepBook order book on Sui.</summary>
[MediaIcon(Media.MediaNames.deepbook)]
[Doc("topics/api/connectors/crypto_exchanges/deepbook.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DeepBookKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Transactions)]
public partial class DeepBookMessageAdapter : MessageAdapter
{
	private const string _defaultIndexerEndpoint =
		"https://deepbook-indexer.mainnet.mystenlabs.com";
	private const string _defaultGrpcEndpoint =
		"https://fullnode.mainnet.sui.io:443";

	/// <summary>Optional public Sui wallet address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional Sui Ed25519 key used to sign swaps locally.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>DeepBook public indexer endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string IndexerEndpoint { get; set; } = _defaultIndexerEndpoint;

	/// <summary>Sui Full Node gRPC v2 endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string GrpcEndpoint { get; set; } = _defaultGrpcEndpoint;

	/// <summary>Current DeepBook package address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CodeKey,
		Description = LocalizedStrings.CodeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public string PackageId { get; set; } =
		DeepBookExtensions.MainnetPackage;

	/// <summary>Sui system clock shared-object address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CodeKey,
		Description = LocalizedStrings.CodeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string ClockObjectId { get; set; } = DeepBookExtensions.Clock;

	/// <summary>
	/// Optional semicolon-separated pool names, IDs, or security codes.
	/// Empty means every pool returned by the indexer.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string Pools { get; set; }

	private int _orderBookDepth = 100;

	/// <summary>Maximum indexer order-book depth.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDepthKey,
		Description = LocalizedStrings.MarketDepthKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int OrderBookDepth
	{
		get => _orderBookDepth;
		set => _orderBookDepth = value is >= 2 and <= 500 &&
			value % 2 == 0
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Order-book depth must be an even number from 2 to 500.");
	}

	private int _historyLimit = 500;

	/// <summary>Maximum number of trades or candles per request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 500
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History limit must be from 1 to 500.");
	}

	private decimal _slippageTolerance = 0.5m;

	/// <summary>Default swap slippage tolerance in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public decimal SlippageTolerance
	{
		get => _slippageTolerance;
		set => _slippageTolerance = value is >= 0.01m and < 100m &&
			decimal.Round(value, 2) == value
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Slippage tolerance must be at least 0.01 and below 100 " +
					"percent, with at most two decimal places.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Polling interval for public and private snapshots.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(2)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Polling interval cannot be less than two seconds.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(IndexerEndpoint), IndexerEndpoint)
			.Set(nameof(GrpcEndpoint), GrpcEndpoint)
			.Set(nameof(PackageId), PackageId)
			.Set(nameof(ClockObjectId), ClockObjectId)
			.Set(nameof(Pools), Pools)
			.Set(nameof(OrderBookDepth), OrderBookDepth)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		IndexerEndpoint = storage.GetValue(nameof(IndexerEndpoint),
			IndexerEndpoint);
		GrpcEndpoint = storage.GetValue(nameof(GrpcEndpoint), GrpcEndpoint);
		PackageId = storage.GetValue(nameof(PackageId), PackageId);
		ClockObjectId = storage.GetValue(nameof(ClockObjectId), ClockObjectId);
		Pools = storage.GetValue<string>(nameof(Pools));
		OrderBookDepth = storage.GetValue(nameof(OrderBookDepth),
			OrderBookDepth);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Wallet={WalletAddress}";
}
