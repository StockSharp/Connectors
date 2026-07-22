namespace StockSharp.THORChain;

/// <summary>The message adapter for THORChain native swaps.</summary>
[MediaIcon(Media.MediaNames.thorchain)]
[Doc("topics/api/connectors/crypto_exchanges/thorchain.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.THORChainKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class THORChainMessageAdapter : MessageAdapter
{
	private const string _defaultMidgardEndpoint =
		"https://gateway.liquify.com/chain/thorchain_midgard/v2";
	private const string _defaultThornodeEndpoint =
		"https://gateway.liquify.com/chain/thorchain_api";

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		THORChainExtensions.TimeFrames;

	/// <summary>Public THORChain wallet address used for balances.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional hexadecimal key used to sign native transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Official Midgard API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string MidgardEndpoint { get; set; } = _defaultMidgardEndpoint;

	/// <summary>THORNode REST endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string ThornodeEndpoint { get; set; } = _defaultThornodeEndpoint;

	/// <summary>Client identifier sent to public infrastructure providers.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IdKey,
		Description = LocalizedStrings.IdKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public string ClientId { get; set; } = "stocksharp.connector";

	/// <summary>
	/// Optional semicolon-separated asset or asset|security-code entries.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string Markets { get; set; }

	private int _maximumDiscoveredMarkets = 20;

	/// <summary>Maximum number of liquid pools discovered automatically.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int MaximumDiscoveredMarkets
	{
		get => _maximumDiscoveredMarkets;
		set => _maximumDiscoveredMarkets = value is >= 1 and <= 100
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Discovered market count must be between one and 100.");
	}

	private decimal _minimumLiquidityUsd = 100_000m;

	/// <summary>Minimum USD liquidity for automatic pool discovery.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceKey,
		Description = LocalizedStrings.PriceKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public decimal MinimumLiquidityUsd
	{
		get => _minimumLiquidityUsd;
		set => _minimumLiquidityUsd = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Minimum liquidity cannot be negative.");
	}

	private decimal _probeVolume = 25m;

	/// <summary>RUNE amount used for executable Level1 quotes.</summary>
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

	private decimal _slippageTolerance = 1m;

	/// <summary>Default liquidity tolerance in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public decimal SlippageTolerance
	{
		get => _slippageTolerance;
		set => _slippageTolerance = value is > 0 and <= 100 &&
			decimal.Round(value, 2) == value
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Slippage tolerance must be between 0.01 and 100 percent.");
	}

	private int _historyMaximum = 1_000;

	/// <summary>Maximum Midgard actions loaded for one history request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public int HistoryMaximum
	{
		get => _historyMaximum;
		set => _historyMaximum = value is >= 1 and <= 5_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History maximum must be between one and 5000.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>Polling interval for public and private state.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
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
			.Set(nameof(MidgardEndpoint), MidgardEndpoint)
			.Set(nameof(ThornodeEndpoint), ThornodeEndpoint)
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(Markets), Markets)
			.Set(nameof(MaximumDiscoveredMarkets), MaximumDiscoveredMarkets)
			.Set(nameof(MinimumLiquidityUsd), MinimumLiquidityUsd)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(HistoryMaximum), HistoryMaximum)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		MidgardEndpoint = storage.GetValue(nameof(MidgardEndpoint),
			MidgardEndpoint);
		ThornodeEndpoint = storage.GetValue(nameof(ThornodeEndpoint),
			ThornodeEndpoint);
		ClientId = storage.GetValue(nameof(ClientId), ClientId);
		Markets = storage.GetValue<string>(nameof(Markets));
		MaximumDiscoveredMarkets = storage.GetValue(
			nameof(MaximumDiscoveredMarkets), MaximumDiscoveredMarkets);
		MinimumLiquidityUsd = storage.GetValue(nameof(MinimumLiquidityUsd),
			MinimumLiquidityUsd);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		HistoryMaximum = storage.GetValue(nameof(HistoryMaximum),
			HistoryMaximum);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Wallet={WalletAddress}";
}
