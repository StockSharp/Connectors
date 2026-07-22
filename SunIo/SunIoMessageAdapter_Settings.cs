namespace StockSharp.SunIo;

/// <summary>The message adapter for SUN.io Smart Router markets.</summary>
[MediaIcon(Media.MediaNames.sunio)]
[Doc("topics/api/connectors/crypto_exchanges/sunio.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SunIoKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class SunIoMessageAdapter : MessageAdapter
{
	private const string _defaultDataEndpoint = "https://open.sun.io";
	private const string _defaultRouterEndpoint =
		"https://rot.endjgfsv.link";
	private const string _defaultNodeEndpoint = "https://api.trongrid.io";
	private const string _defaultSmartRouterAddress =
		"TGnC7LMji8hBpyvZt1TTEJhVpAZ5HFyJ3r";

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		SunIoExtensions.TimeFrames;

	/// <summary>Public TRON wallet address used for balances.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional hexadecimal key used to sign TRON transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Optional SUN.io read API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public SecureString SunApiKey { get; set; }

	/// <summary>Optional TronGrid API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public SecureString TronApiKey { get; set; }

	/// <summary>SUN.io read API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string DataEndpoint { get; set; } = _defaultDataEndpoint;

	/// <summary>Official Smart Router calculation endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string RouterEndpoint { get; set; } = _defaultRouterEndpoint;

	/// <summary>TRON FullNode HTTP endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string NodeEndpoint { get; set; } = _defaultNodeEndpoint;

	/// <summary>Official SUN.io Smart Router contract address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string SmartRouterAddress { get; set; } =
		_defaultSmartRouterAddress;

	/// <summary>
	/// Optional semicolon-separated token-address or address|security-code
	/// entries.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string Markets { get; set; }

	private int _maximumDiscoveredMarkets = 20;

	/// <summary>Maximum number of liquid tokens discovered automatically.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int MaximumDiscoveredMarkets
	{
		get => _maximumDiscoveredMarkets;
		set => _maximumDiscoveredMarkets = value is >= 1 and <= 50
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Discovered market count must be between one and 50.");
	}

	private decimal _minimumLiquidityUsd = 100_000m;

	/// <summary>Minimum token liquidity for automatic discovery.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceKey,
		Description = LocalizedStrings.PriceKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public decimal MinimumLiquidityUsd
	{
		get => _minimumLiquidityUsd;
		set => _minimumLiquidityUsd = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Minimum liquidity cannot be negative.");
	}

	private decimal _probeVolume = 10m;

	/// <summary>TRX amount used for executable Level1 quotes.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public decimal ProbeVolume
	{
		get => _probeVolume;
		set => _probeVolume = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Quote probe volume must be positive.");
	}

	private decimal _slippageTolerance = 1m;

	/// <summary>Default transaction slippage tolerance in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public decimal SlippageTolerance
	{
		get => _slippageTolerance;
		set => _slippageTolerance = value is > 0 and < 100 &&
			decimal.Round(value, 2) == value
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Slippage tolerance must be between 0.01 and 99.99 percent.");
	}

	private decimal _feeLimit = 500m;

	/// <summary>Maximum TRX burned for one contract transaction.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CommissionKey,
		Description = LocalizedStrings.CommissionKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public decimal FeeLimit
	{
		get => _feeLimit;
		set => _feeLimit = value is >= 1 and <= 15_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"TRON fee limit must be between one and 15000 TRX.");
	}

	private TimeSpan _deadlineInterval = TimeSpan.FromMinutes(20);

	/// <summary>Default on-chain swap deadline interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 10)]
	public TimeSpan DeadlineInterval
	{
		get => _deadlineInterval;
		set => _deadlineInterval = value >= TimeSpan.FromMinutes(1) &&
			value <= TimeSpan.FromDays(1)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Swap deadline must be between one minute and one day.");
	}

	private int _historyMaximum = 1_000;

	/// <summary>Maximum indexed swaps loaded for one history request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 11)]
	public int HistoryMaximum
	{
		get => _historyMaximum;
		set => _historyMaximum = value is >= 1 and <= 5_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History maximum must be between one and 5000.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>Polling interval for market and wallet state.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 12)]
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
			.Set(nameof(SunApiKey), SunApiKey)
			.Set(nameof(TronApiKey), TronApiKey)
			.Set(nameof(DataEndpoint), DataEndpoint)
			.Set(nameof(RouterEndpoint), RouterEndpoint)
			.Set(nameof(NodeEndpoint), NodeEndpoint)
			.Set(nameof(SmartRouterAddress), SmartRouterAddress)
			.Set(nameof(Markets), Markets)
			.Set(nameof(MaximumDiscoveredMarkets), MaximumDiscoveredMarkets)
			.Set(nameof(MinimumLiquidityUsd), MinimumLiquidityUsd)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(FeeLimit), FeeLimit)
			.Set(nameof(DeadlineInterval), DeadlineInterval)
			.Set(nameof(HistoryMaximum), HistoryMaximum)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		SunApiKey = storage.GetValue<SecureString>(nameof(SunApiKey));
		TronApiKey = storage.GetValue<SecureString>(nameof(TronApiKey));
		DataEndpoint = storage.GetValue(nameof(DataEndpoint), DataEndpoint);
		RouterEndpoint = storage.GetValue(nameof(RouterEndpoint),
			RouterEndpoint);
		NodeEndpoint = storage.GetValue(nameof(NodeEndpoint), NodeEndpoint);
		SmartRouterAddress = storage.GetValue(nameof(SmartRouterAddress),
			SmartRouterAddress);
		Markets = storage.GetValue<string>(nameof(Markets));
		MaximumDiscoveredMarkets = storage.GetValue(
			nameof(MaximumDiscoveredMarkets), MaximumDiscoveredMarkets);
		MinimumLiquidityUsd = storage.GetValue(nameof(MinimumLiquidityUsd),
			MinimumLiquidityUsd);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		FeeLimit = storage.GetValue(nameof(FeeLimit), FeeLimit);
		DeadlineInterval = storage.GetValue(nameof(DeadlineInterval),
			DeadlineInterval);
		HistoryMaximum = storage.GetValue(nameof(HistoryMaximum),
			HistoryMaximum);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Wallet={WalletAddress}";
}
