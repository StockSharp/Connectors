namespace StockSharp.Ligther;

/// <summary>
/// Trading section.
/// </summary>
[DataContract]
[Serializable]
public enum LigtherSections
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
/// The message adapter for Ligther (Lighter).
/// </summary>
[Doc("topics/api/connectors/crypto_exchanges/ligther.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LigtherKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(LigtherOrderCondition))]
public partial class LigtherMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
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
	/// Lighter account index.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public int AccountIndex { get; set; }

	/// <summary>
	/// Lighter API key index.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public int ApiKeyIndex { get; set; }

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
	[ItemsSource(typeof(LigtherSections))]
	public IEnumerable<LigtherSections> Sections
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

	private IEnumerable<LigtherSections> _sections = Enumerator.GetValues<LigtherSections>();

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	private const string _mainRest = "https://mainnet.zklighter.elliot.ai";
	private const string _testRest = "https://testnet.zklighter.elliot.ai";
	private const string _mainWs = "wss://mainnet.zklighter.elliot.ai/stream";
	private const string _testWs = "wss://testnet.zklighter.elliot.ai/stream";

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
	/// Use websocket read-only mode query parameter when available.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 2)]
	[BasicSetting]
	public bool UseWsReadOnlyMode { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(AccountIndex), AccountIndex)
			.Set(nameof(ApiKeyIndex), ApiKeyIndex)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(SpotRestEndpoint), SpotRestEndpoint)
			.Set(nameof(DerivativesRestEndpoint), DerivativesRestEndpoint)
			.Set(nameof(SpotWsEndpoint), SpotWsEndpoint)
			.Set(nameof(DerivativesWsEndpoint), DerivativesWsEndpoint)
			.Set(nameof(UseWsReadOnlyMode), UseWsReadOnlyMode);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		AccountIndex = storage.GetValue(nameof(AccountIndex), AccountIndex);
		ApiKeyIndex = storage.GetValue(nameof(ApiKeyIndex), ApiKeyIndex);
		var sectionsValue = storage.GetValue<string>(nameof(Sections));
		if (sectionsValue.IsEmpty())
		{
			var legacySection = storage.GetValue("Section", LigtherSections.Derivatives);
			Sections = [legacySection];
		}
		else
		{
			Sections = [.. sectionsValue.SplitByComma().Select(s => s.To<LigtherSections>())];
		}
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		SpotRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotRestEndpoint), SpotRestEndpoint), IsDemo ? _testRest : _mainRest, "https");
		DerivativesRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(DerivativesRestEndpoint), DerivativesRestEndpoint), IsDemo ? _testRest : _mainRest, "https");
		SpotWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotWsEndpoint), SpotWsEndpoint), IsDemo ? _testWs : _mainWs, "wss");
		DerivativesWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(DerivativesWsEndpoint), DerivativesWsEndpoint), IsDemo ? _testWs : _mainWs, "wss");
		UseWsReadOnlyMode = storage.GetValue(nameof(UseWsReadOnlyMode), UseWsReadOnlyMode);
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
		=> base.ToString() + $": Sections={Sections.Select(s => s.To<string>()).JoinComma()}, Account={AccountIndex}, Key={Key.ToId()}";
}
