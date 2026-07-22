namespace StockSharp.Aster;

/// <summary>
/// Trading section.
/// </summary>
[DataContract]
[Serializable]
public enum AsterSections
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
/// The message adapter for Aster.
/// </summary>
[Doc("topics/api/connectors/crypto_exchanges/aster.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AsterKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(AsterOrderCondition))]
public partial class AsterMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
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
	/// Market sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	[ItemsSource(typeof(AsterSections))]
	public IEnumerable<AsterSections> Sections
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

	private IEnumerable<AsterSections> _sections = Enumerator.GetValues<AsterSections>();

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	private const string _mainSpotRest = "https://sapi.asterdex.com";
	private const string _mainDerivativesRest = "https://fapi.asterdex.com";
	private const string _mainSpotWs = "wss://sstream.asterdex.com/ws";
	private const string _mainDerivativesWs = "wss://fstream.asterdex.com/ws";

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
	public string SpotRestEndpoint { get; set; } = _mainSpotRest;

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
	public string DerivativesRestEndpoint { get; set; } = _mainDerivativesRest;

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
	public string SpotWsEndpoint { get; set; } = _mainSpotWs;

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
	public string DerivativesWsEndpoint { get; set; } = _mainDerivativesWs;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(SpotRestEndpoint), SpotRestEndpoint)
			.Set(nameof(DerivativesRestEndpoint), DerivativesRestEndpoint)
			.Set(nameof(SpotWsEndpoint), SpotWsEndpoint)
			.Set(nameof(DerivativesWsEndpoint), DerivativesWsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		var sectionsValue = storage.GetValue<string>(nameof(Sections));
		if (sectionsValue.IsEmpty())
		{
			var legacySection = storage.GetValue("Section", AsterSections.Derivatives);
			Sections = [legacySection];
		}
		else
		{
			Sections = [.. sectionsValue.SplitByComma().Select(s => s.To<AsterSections>())];
		}
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		SpotRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotRestEndpoint), SpotRestEndpoint), _mainSpotRest, "https");
		DerivativesRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(DerivativesRestEndpoint), DerivativesRestEndpoint), _mainDerivativesRest, "https");
		SpotWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotWsEndpoint), SpotWsEndpoint), _mainSpotWs, "wss");
		DerivativesWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(DerivativesWsEndpoint), DerivativesWsEndpoint), _mainDerivativesWs, "wss");
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
		=> base.ToString() + $": Sections={Sections.Select(s => s.To<string>()).JoinComma()}, Key={Key.ToId()}";
}
