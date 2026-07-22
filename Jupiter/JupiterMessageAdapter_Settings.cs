namespace StockSharp.Jupiter;

/// <summary>The message adapter for Jupiter.</summary>
[MediaIcon(Media.MediaNames.jupiter)]
[Doc("topics/api/connectors/crypto_exchanges/jupiter.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.JupiterKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(JupiterOrderCondition))]
public partial class JupiterMessageAdapter : MessageAdapter
{
	private const string _defaultSpotMarkets =
		JupiterExtensions.WrappedSolMint + "|" +
		JupiterExtensions.UsdcMint + "|SOL-USDC;" +
		JupiterExtensions.JupiterMint + "|" +
		JupiterExtensions.UsdcMint + "|JUP-USDC;" +
		JupiterExtensions.WrappedBitcoinMint + "|" +
		JupiterExtensions.UsdcMint + "|BTC-USDC;" +
		JupiterExtensions.WrappedEthereumMint + "|" +
		JupiterExtensions.UsdcMint + "|ETH-USDC";

	/// <summary>Optional Jupiter API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString ApiKey { get; set; }

	/// <summary>Optional public Solana wallet address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional base58 Solana keypair used for transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Jupiter data and Swap API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public string ApiEndpoint { get; set; }

	/// <summary>Jupiter Perps API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string PerpetualEndpoint { get; set; }

	/// <summary>
	/// Semicolon-separated base-mint|quote-mint|security-code definitions.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string SpotMarkets { get; set; } = _defaultSpotMarkets;

	/// <summary>Whether SOL, BTC, and ETH perpetual markets are exposed.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey,
		Description = LocalizedStrings.FuturesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public bool IsPerpetualsEnabled { get; set; } = true;

	private decimal _probeVolume = 1m;

	/// <summary>Base-token quantity used for executable quote probes.</summary>
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

	private decimal _perpetualSlippageTolerance = 2m;

	/// <summary>Maximum Jupiter Perps slippage in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public decimal PerpetualSlippageTolerance
	{
		get => _perpetualSlippageTolerance;
		set => _perpetualSlippageTolerance = value is >= 0 and <= 50 &&
			decimal.Round(value, 2) == value
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Perpetual slippage must be between zero and 50 percent, " +
				"with at most two decimal places.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(30);

	/// <summary>Polling interval for quotes and private state.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(2)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Jupiter polling interval cannot be less than two seconds.");
	}

	private int _historyLimit = 100;

	/// <summary>Maximum private trades loaded per status snapshot.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History limit must be between one and 1000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(PerpetualEndpoint), PerpetualEndpoint)
			.Set(nameof(SpotMarkets), SpotMarkets)
			.Set(nameof(IsPerpetualsEnabled), IsPerpetualsEnabled)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(PerpetualSlippageTolerance),
				PerpetualSlippageTolerance)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		ApiEndpoint = storage.GetValue<string>(nameof(ApiEndpoint));
		PerpetualEndpoint = storage.GetValue<string>(nameof(PerpetualEndpoint));
		SpotMarkets = storage.GetValue(nameof(SpotMarkets), SpotMarkets);
		IsPerpetualsEnabled = storage.GetValue(nameof(IsPerpetualsEnabled),
			IsPerpetualsEnabled);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		PerpetualSlippageTolerance = storage.GetValue(
			nameof(PerpetualSlippageTolerance), PerpetualSlippageTolerance);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Wallet={WalletAddress}";
}
