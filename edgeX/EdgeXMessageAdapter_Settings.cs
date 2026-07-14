namespace StockSharp.EdgeX;

/// <summary>
/// Trading section.
/// </summary>
[DataContract]
[Serializable]
public enum EdgeXSections
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
/// The message adapter for edgeX.
/// </summary>
[Doc("topics/api/connectors/crypto_exchanges/edgex.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.EdgeXKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(EdgeXOrderCondition))]
public partial class EdgeXMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
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
	/// edgeX clearing account.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClearingAccKey,
		Description = LocalizedStrings.ClearingAccKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string ClearingAccount { get; set; }

	/// <summary>
	/// edgeX passphrase.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PassphraseKey,
		Description = LocalizedStrings.PassphraseKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString Passphrase { get; set; }

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
	[ItemsSource(typeof(EdgeXSections))]
	public IEnumerable<EdgeXSections> Sections
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

	private IEnumerable<EdgeXSections> _sections = Enumerator.GetValues<EdgeXSections>();

	/// <summary>
	/// Enable experimental spot section.
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

	private const string _mainRest = "https://pro.edgex.exchange";
	private const string _mainPublicWs = "wss://pro.edgex.exchange/api/v1/public/ws";
	private const string _mainPrivateWs = "wss://pro.edgex.exchange/api/v1/private/ws";

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
	public string SpotWsEndpoint { get; set; } = _mainPublicWs;

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
	public string DerivativesWsEndpoint { get; set; } = _mainPublicWs;

	/// <summary>
	/// Derivatives private websocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DerivativesPrivateWsKey,
		Description = LocalizedStrings.DerivativesPrivateWsKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 2)]
	[BasicSetting]
	public string DerivativesPrivateWsEndpoint { get; set; } = _mainPrivateWs;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(ClearingAccount), ClearingAccount)
			.Set(nameof(Passphrase), Passphrase)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(EnableSpotSection), EnableSpotSection)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(SpotRestEndpoint), SpotRestEndpoint)
			.Set(nameof(DerivativesRestEndpoint), DerivativesRestEndpoint)
			.Set(nameof(SpotWsEndpoint), SpotWsEndpoint)
			.Set(nameof(DerivativesWsEndpoint), DerivativesWsEndpoint)
			.Set(nameof(DerivativesPrivateWsEndpoint), DerivativesPrivateWsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		ClearingAccount = storage.GetValue<string>(nameof(ClearingAccount));
		Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));
		var sectionsValue = storage.GetValue<string>(nameof(Sections));
		if (sectionsValue.IsEmpty())
		{
			var legacySection = storage.GetValue("Section", EdgeXSections.Derivatives);
			Sections = [legacySection];
		}
		else
		{
			Sections = [.. sectionsValue.SplitByComma().Select(s => s.To<EdgeXSections>())];
		}
		EnableSpotSection = storage.GetValue(nameof(EnableSpotSection), EnableSpotSection);
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		SpotRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotRestEndpoint), SpotRestEndpoint), _mainRest, "https");
		DerivativesRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(DerivativesRestEndpoint), DerivativesRestEndpoint), _mainRest, "https");
		SpotWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotWsEndpoint), SpotWsEndpoint), _mainPublicWs, "wss");
		DerivativesWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(DerivativesWsEndpoint), DerivativesWsEndpoint), _mainPublicWs, "wss");
		DerivativesPrivateWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(DerivativesPrivateWsEndpoint), DerivativesPrivateWsEndpoint), _mainPrivateWs, "wss");
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
