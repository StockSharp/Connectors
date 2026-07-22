namespace StockSharp.Paradex;

/// <summary>
/// Trading section.
/// </summary>
[DataContract]
[Serializable]
public enum ParadexSections
{
	/// <summary>
	/// Spot market section.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotKey)]
	Spot,

	/// <summary>
	/// Derivatives market section.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DerivativesKey)]
	Derivatives,
}

/// <summary>
/// The message adapter for Paradex.
/// </summary>
[Doc("topics/api/connectors/crypto_exchanges/paradex.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ParadexKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(ParadexOrderCondition))]
public partial class ParadexMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
{
	/// <summary>
	/// Possible candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. Native.Extensions.TimeFrames.Keys];

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// Starknet account address.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string StarknetAccount { get; set; }

	/// <summary>
	/// Starknet signing key material.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString StarknetPrivateKey { get; set; }

	/// <summary>
	/// Market sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	[ItemsSource(typeof(ParadexSections))]
	public IEnumerable<ParadexSections> Sections
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

	private IEnumerable<ParadexSections> _sections = Enumerator.GetValues<ParadexSections>();

	/// <summary>
	/// Enable spot section.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotKey,
		Description = LocalizedStrings.SpotSectionKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public bool EnableSpotSection { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	private const string _mainRest = "https://api.prod.paradex.trade";
	private const string _testRest = "https://api.testnet.paradex.trade";
	private const string _mainWs = "wss://ws.api.prod.paradex.trade/v1";
	private const string _testWs = "wss://ws.api.testnet.paradex.trade/v1";

	/// <summary>
	/// Spot REST endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotRestKey,
		Description = LocalizedStrings.SpotRestKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string SpotRestEndpoint { get; set; } = _mainRest;

	/// <summary>
	/// Derivatives REST endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DerivativesRestKey,
		Description = LocalizedStrings.DerivativesRestKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string DerivativesRestEndpoint { get; set; } = _mainRest;

	/// <summary>
	/// Spot websocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotWsKey,
		Description = LocalizedStrings.SpotWsKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string SpotWsEndpoint { get; set; } = _mainWs;

	/// <summary>
	/// Derivatives websocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DerivativesWsKey,
		Description = LocalizedStrings.DerivativesWsKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string DerivativesWsEndpoint { get; set; } = _mainWs;

	/// <summary>
	/// Auth endpoint path.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PathKey,
		Description = LocalizedStrings.PathKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string AuthPath { get; set; } = "/v1/auth";

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(StarknetAccount), StarknetAccount)
			.Set(nameof(StarknetPrivateKey), StarknetPrivateKey)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(EnableSpotSection), EnableSpotSection)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(SpotRestEndpoint), SpotRestEndpoint)
			.Set(nameof(DerivativesRestEndpoint), DerivativesRestEndpoint)
			.Set(nameof(SpotWsEndpoint), SpotWsEndpoint)
			.Set(nameof(DerivativesWsEndpoint), DerivativesWsEndpoint)
			.Set(nameof(AuthPath), AuthPath);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		StarknetAccount = storage.GetValue<string>(nameof(StarknetAccount));
		StarknetPrivateKey = storage.GetValue<SecureString>(nameof(StarknetPrivateKey));
		var sectionsValue = storage.GetValue<string>(nameof(Sections));
		if (sectionsValue.IsEmpty())
		{
			var legacySection = storage.GetValue("Section", ParadexSections.Derivatives);
			Sections = [legacySection];
		}
		else
		{
			Sections = [.. sectionsValue.SplitByComma().Select(s => s.To<ParadexSections>())];
		}
		EnableSpotSection = storage.GetValue(nameof(EnableSpotSection), EnableSpotSection);
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		SpotRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotRestEndpoint), SpotRestEndpoint), IsDemo ? _testRest : _mainRest, "https");
		DerivativesRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(DerivativesRestEndpoint), DerivativesRestEndpoint), IsDemo ? _testRest : _mainRest, "https");
		SpotWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotWsEndpoint), SpotWsEndpoint), IsDemo ? _testWs : _mainWs, "wss");
		DerivativesWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(DerivativesWsEndpoint), DerivativesWsEndpoint), IsDemo ? _testWs : _mainWs, "wss");
		AuthPath = storage.GetValue(nameof(AuthPath), AuthPath);

		if (AuthPath.IsEmpty())
			AuthPath = "/v1/auth";
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
		=> base.ToString() + $": Sections={Sections.Select(s => s.To<string>()).JoinComma()}, Starknet={StarknetAccount}";
}
