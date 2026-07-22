namespace StockSharp.Phemex;

/// <summary>
/// Phemex market sections.
/// </summary>
[DataContract]
[Serializable]
public enum PhemexSections
{
	/// <summary>
	/// Spot market.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotKey)]
	Spot,

	/// <summary>
	/// Perpetual futures market.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey)]
	Futures,
}

/// <summary>
/// The message adapter for Phemex.
/// </summary>
[MediaIcon(Media.MediaNames.phemex)]
[Doc("topics/api/connectors/crypto_exchanges/phemex.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PhemexKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(PhemexOrderCondition))]
public partial class PhemexMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.phemex.com";
	private const string _defaultPublicWsEndpoint = "wss://ws.phemex.com";
	private const string _defaultPrivateWsEndpoint = "wss://ws.phemex.com";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. PhemexExtensions.TimeFrames.Keys];

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

	private IEnumerable<PhemexSections> _sections = Enumerator.GetValues<PhemexSections>();

	/// <summary>
	/// Enabled market sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	[ItemsSource(typeof(PhemexSections))]
	public IEnumerable<PhemexSections> Sections
	{
		get => _sections;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			var sections = value.Distinct().ToArray();
			if (sections.Length == 0)
				throw new ArgumentOutOfRangeException(nameof(value));
			_sections = sections;
		}
	}

	/// <summary>
	/// REST API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>
	/// Public WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string PublicWsEndpoint { get; set; } = _defaultPublicWsEndpoint;

	/// <summary>
	/// Private WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string PrivateWsEndpoint { get; set; } = _defaultPrivateWsEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Sections), Sections.Select(static section => section.To<string>()).JoinComma())
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(PublicWsEndpoint), PublicWsEndpoint)
			.Set(nameof(PrivateWsEndpoint), PrivateWsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		var sections = storage.GetValue<string>(nameof(Sections));
		Sections = sections.IsEmpty()
			? Enumerator.GetValues<PhemexSections>()
			: [.. sections.SplitByComma().Select(static section => section.To<PhemexSections>())];
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint, "https");
		PublicWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(PublicWsEndpoint), PublicWsEndpoint),
			_defaultPublicWsEndpoint, "wss");
		PrivateWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(PrivateWsEndpoint), PrivateWsEndpoint),
			_defaultPrivateWsEndpoint, "wss");
	}

	private static string NormalizeEndpoint(string endpoint, string fallback, string scheme)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Sections={Sections.Select(static section => section.To<string>()).JoinComma()}, Key={Key.ToId()}";
}
