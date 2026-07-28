namespace StockSharp.StonFi;

/// <summary>
/// The message adapter for the STON.fi TON automated market maker.
/// </summary>
[MediaIcon(Media.MediaNames.stonfi)]
[Doc("topics/api/connectors/crypto_exchanges/stonfi.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.StonFiKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class StonFiMessageAdapter : MessageAdapter
{
	private const string _defaultApiEndpoint = "https://api.ston.fi";
	private const string _defaultTonCenterEndpoint =
		"https://toncenter.com/api/v2";

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		StonFiExtensions.TimeFrames;

	/// <summary>STON.fi REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string ApiEndpoint { get; set; } = _defaultApiEndpoint;

	/// <summary>TON Center v2 endpoint used for wallet state and broadcast.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string TonCenterEndpoint { get; set; } =
		_defaultTonCenterEndpoint;

	/// <summary>Optional TON Center API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString TonCenterApiKey { get; set; }

	/// <summary>Public TON Wallet V4 address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>24-word TON mnemonic used to sign swaps.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Mnemonic { get; set; }

	/// <summary>TON Wallet V4 subwallet identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IdKey,
		Description = LocalizedStrings.IdKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[CLSCompliant(false)]
	public uint WalletSubwalletId { get; set; } = WalletTraits.SUBWALLET_ID;

	private int _walletRevision = 2;

	/// <summary>TON Wallet V4 revision.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VersionKey,
		Description = LocalizedStrings.VersionKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int WalletRevision
	{
		get => _walletRevision;
		set => _walletRevision = value is 1 or 2
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"TON Wallet V4 revision must be 1 or 2.");
	}

	/// <summary>
	/// Optional comma- or semicolon-separated STON.fi pool-address filter.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public string Pools { get; set; }

	private int _poolLimit = 100;

	/// <summary>Maximum number of popular pools loaded automatically.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public int PoolLimit
	{
		get => _poolLimit;
		set => _poolLimit = value is >= 1 and <= 500
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"STON.fi pool limit must be between 1 and 500.");
	}

	private decimal _probeVolume = 1m;

	/// <summary>Base-asset amount used for executable Level1 quotes.</summary>
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
				"STON.fi quote probe volume must be positive.");
	}

	private decimal _slippageTolerance = 1m;

	/// <summary>Maximum swap slippage in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public decimal SlippageTolerance
	{
		get => _slippageTolerance;
		set => _slippageTolerance = value is > 0 and < 100
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"STON.fi slippage must be between 0 and 100 percent.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(3);

	/// <summary>Market-data polling interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval =
			value >= TimeSpan.FromSeconds(1) &&
			value <= TimeSpan.FromMinutes(1)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"STON.fi polling interval must be between one second " +
						"and one minute.");
	}

	private int _historyBlockLimit = 30_000;

	/// <summary>Maximum TON block range scanned for event history.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 10)]
	public int HistoryBlockLimit
	{
		get => _historyBlockLimit;
		set => _historyBlockLimit = value is >= 1000 and <= 500_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"STON.fi history block limit must be between 1,000 and " +
					"500,000.");
	}

	private TimeSpan _privatePollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Portfolio and swap-status polling interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 11)]
	public TimeSpan PrivatePollingInterval
	{
		get => _privatePollingInterval;
		set => _privatePollingInterval =
			value >= TimeSpan.FromSeconds(2) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"STON.fi private polling interval must be between two " +
						"seconds and five minutes.");
	}

	private TimeSpan _transactionTimeout = TimeSpan.FromMinutes(15);

	/// <summary>Maximum time to wait for a swap status.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		Description = LocalizedStrings.TimeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 12)]
	public TimeSpan TransactionTimeout
	{
		get => _transactionTimeout;
		set => _transactionTimeout =
			value >= TimeSpan.FromMinutes(1) &&
			value <= TimeSpan.FromHours(2)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"STON.fi transaction timeout must be between one minute " +
						"and two hours.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(TonCenterEndpoint), TonCenterEndpoint)
			.Set(nameof(TonCenterApiKey), TonCenterApiKey)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(Mnemonic), Mnemonic)
			.Set(nameof(WalletSubwalletId), WalletSubwalletId)
			.Set(nameof(WalletRevision), WalletRevision)
			.Set(nameof(Pools), Pools)
			.Set(nameof(PoolLimit), PoolLimit)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(HistoryBlockLimit), HistoryBlockLimit)
			.Set(nameof(PrivatePollingInterval), PrivatePollingInterval)
			.Set(nameof(TransactionTimeout), TransactionTimeout);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(ApiEndpoint), ApiEndpoint)) ?? _defaultApiEndpoint;
		TonCenterEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(TonCenterEndpoint), TonCenterEndpoint)) ??
			_defaultTonCenterEndpoint;
		TonCenterApiKey = storage.GetValue<SecureString>(
			nameof(TonCenterApiKey));
		WalletAddress = NormalizeAddress(storage.GetValue<string>(
			nameof(WalletAddress)));
		Mnemonic = storage.GetValue<SecureString>(nameof(Mnemonic));
		WalletSubwalletId = storage.GetValue(nameof(WalletSubwalletId),
			WalletSubwalletId);
		WalletRevision = storage.GetValue(nameof(WalletRevision),
			WalletRevision);
		Pools = storage.GetValue<string>(nameof(Pools));
		PoolLimit = storage.GetValue(nameof(PoolLimit), PoolLimit);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		SlippageTolerance = storage.GetValue(
			nameof(SlippageTolerance), SlippageTolerance);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		HistoryBlockLimit = storage.GetValue(nameof(HistoryBlockLimit),
			HistoryBlockLimit);
		PrivatePollingInterval = storage.GetValue(
			nameof(PrivatePollingInterval), PrivatePollingInterval);
		TransactionTimeout = storage.GetValue(nameof(TransactionTimeout),
			TransactionTimeout);
	}

	private static string NormalizeEndpoint(string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		if (!value.Contains("://", StringComparison.Ordinal))
			value = "https://" + value.TrimStart('/');
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			!(uri.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				uri.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				$"Invalid HTTP endpoint '{value}'.", nameof(value));
		return value.TrimEnd('/');
	}

	private static string NormalizeAddress(string value)
		=> value.IsEmpty() ? null : value.NormalizeTonAddress();

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Wallet={WalletAddress}";
}
