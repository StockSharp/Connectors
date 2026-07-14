namespace StockSharp.Hyperliquid;

/// <summary>
/// Hyperliquid sections.
/// </summary>
[DataContract]
[Serializable]
public enum HyperliquidSections
{
	/// <summary>
	/// Spot market section.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SpotKey)]
	Spot,

	/// <summary>
	/// Derivatives market section.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DerivativesKey)]
	Derivatives,
}

/// <summary>
/// The message adapter for Hyperliquid DEX.
/// </summary>
[MediaIcon(Media.MediaNames.liquid)]
[Doc("topics/api/connectors/crypto_exchanges/hyperliquid.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.HyperliquidKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(HyperliquidOrderCondition))]
public partial class HyperliquidMessageAdapter : MessageAdapter
{
	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. Native.Extensions.TimeFrames.Keys];

	/// <summary>
	/// Wallet address (0x...) used for portfolio and order status lookup.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>
	/// Private key used to sign /exchange actions.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>
	/// Optional vault/sub-account address for trading actions.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VaultAddressKey,
		Description = LocalizedStrings.VaultAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string VaultAddress { get; set; }

	/// <summary>
	/// Optional expiry timestamp for signed actions in Unix milliseconds.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExpirationKey,
		Description = LocalizedStrings.ExpirationKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public long? ExpiresAfter { get; set; }

	/// <summary>
	/// Trading sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	[ItemsSource(typeof(HyperliquidSections))]
	public IEnumerable<HyperliquidSections> Sections
	{
		get => _sections;
		set
		{
			if (value is null)
				throw new ArgumentNullException(nameof(value));

			var arr = value.Distinct().ToArray();

			if (arr.Length == 0)
				throw new ArgumentOutOfRangeException(nameof(value));

			_sections = arr;
		}
	}

	private IEnumerable<HyperliquidSections> _sections = Enumerator.GetValues<HyperliquidSections>();

	/// <summary>
	/// Use Hyperliquid testnet.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public bool IsTestnet { get; set; }

	private const string _mainInfoEndpoint = "https://api.hyperliquid.xyz/info";
	private const string _mainExchangeEndpoint = "https://api.hyperliquid.xyz/exchange";
	private const string _mainWsEndpoint = "wss://api.hyperliquid.xyz/ws";
	private const string _testInfoEndpoint = "https://api.hyperliquid-testnet.xyz/info";
	private const string _testExchangeEndpoint = "https://api.hyperliquid-testnet.xyz/exchange";
	private const string _testWsEndpoint = "wss://api.hyperliquid-testnet.xyz/ws";

	/// <summary>
	/// Full <c>/info</c> endpoint URL.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.InfoKey,
		Description = LocalizedStrings.InfoKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public string InfoEndpoint { get; set; } = _mainInfoEndpoint;

	/// <summary>
	/// Full <c>/exchange</c> endpoint URL.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExchangeKey,
		Description = LocalizedStrings.ExchangeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	[BasicSetting]
	public string ExchangeEndpoint { get; set; } = _mainExchangeEndpoint;

	/// <summary>
	/// Full websocket endpoint URL.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WsEndpointKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	[BasicSetting]
	public string WsEndpoint { get; set; } = _mainWsEndpoint;

	/// <summary>
	/// Default slippage for market orders.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	[BasicSetting]
	public decimal MarketOrderSlippage { get; set; } = 0.05m;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(VaultAddress), VaultAddress)
			.Set(nameof(ExpiresAfter), ExpiresAfter)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(IsTestnet), IsTestnet)
			.Set(nameof(InfoEndpoint), InfoEndpoint)
			.Set(nameof(ExchangeEndpoint), ExchangeEndpoint)
			.Set(nameof(WsEndpoint), WsEndpoint)
			.Set(nameof(MarketOrderSlippage), MarketOrderSlippage);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		VaultAddress = storage.GetValue<string>(nameof(VaultAddress));
		ExpiresAfter = storage.GetValue<long?>(nameof(ExpiresAfter));
		var sectionsValue = storage.GetValue<string>(nameof(Sections));
		if (sectionsValue.IsEmpty())
		{
			var legacySection = storage.GetValue("Section", HyperliquidSections.Derivatives);
			Sections = [legacySection];
		}
		else
		{
			Sections = [.. sectionsValue.SplitByComma().Select(s => s.To<HyperliquidSections>())];
		}
		IsTestnet = storage.GetValue(nameof(IsTestnet), IsTestnet);

		InfoEndpoint = NormalizeEndpoint(storage.GetValue(nameof(InfoEndpoint), InfoEndpoint), IsTestnet ? _testInfoEndpoint : _mainInfoEndpoint, "https");
		ExchangeEndpoint = NormalizeEndpoint(storage.GetValue(nameof(ExchangeEndpoint), ExchangeEndpoint), IsTestnet ? _testExchangeEndpoint : _mainExchangeEndpoint, "https");
		WsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(WsEndpoint), WsEndpoint), IsTestnet ? _testWsEndpoint : _mainWsEndpoint, "wss");
		MarketOrderSlippage = Math.Clamp(storage.GetValue(nameof(MarketOrderSlippage), MarketOrderSlippage), 0m, 0.95m);
	}

	private static string NormalizeEndpoint(string endpoint, string fallback, string scheme)
	{
		if (endpoint.IsEmpty())
			endpoint = fallback;

		endpoint = endpoint.Trim();

		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";

		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Sections={Sections.Select(s => s.To<string>()).JoinComma()}, Wallet={WalletAddress}";
}
