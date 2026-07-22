namespace StockSharp.Weex;

/// <summary>
/// WEEX market sections.
/// </summary>
[DataContract]
[Serializable]
public enum WeexSections
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
	/// USDT-margined perpetual futures.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey)]
	Futures,
}

/// <summary>
/// The message adapter for WEEX.
/// </summary>
[MediaIcon(Media.MediaNames.weex)]
[Doc("topics/api/connectors/crypto_exchanges/weex.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.WEEXKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(WeexOrderCondition))]
public partial class WeexMessageAdapter : MessageAdapter, IKeySecretAdapter, IPassphraseAdapter
{
	private const string _defaultSpotRestEndpoint = "https://api-spot.weex.com";
	private const string _defaultFuturesRestEndpoint = "https://api-contract.weex.com";
	private const string _defaultSpotPublicWsEndpoint = "wss://ws-spot.weex.com/v3/ws/public";
	private const string _defaultSpotPrivateWsEndpoint = "wss://ws-spot.weex.com/v3/ws/private";
	private const string _defaultFuturesPublicWsEndpoint = "wss://ws-contract.weex.com/v3/ws/public";
	private const string _defaultFuturesPrivateWsEndpoint = "wss://ws-contract.weex.com/v3/ws/private";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. WeexExtensions.TimeFrames.Keys];

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
	/// API key passphrase.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PassphraseKey,
		Description = LocalizedStrings.PassphraseKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Passphrase { get; set; }

	private IEnumerable<WeexSections> _sections = Enumerator.GetValues<WeexSections>();

	/// <summary>
	/// Enabled market sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	[ItemsSource(typeof(WeexSections))]
	public IEnumerable<WeexSections> Sections
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
	/// Spot REST API endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string SpotRestEndpoint { get; set; } = _defaultSpotRestEndpoint;

	/// <summary>
	/// Futures REST API endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	[BasicSetting]
	public string FuturesRestEndpoint { get; set; } = _defaultFuturesRestEndpoint;

	/// <summary>
	/// Spot public WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
	[BasicSetting]
	public string SpotPublicWsEndpoint { get; set; } = _defaultSpotPublicWsEndpoint;

	/// <summary>
	/// Spot private WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 1)]
	[BasicSetting]
	public string SpotPrivateWsEndpoint { get; set; } = _defaultSpotPrivateWsEndpoint;

	/// <summary>
	/// Futures public WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 2)]
	[BasicSetting]
	public string FuturesPublicWsEndpoint { get; set; } = _defaultFuturesPublicWsEndpoint;

	/// <summary>
	/// Futures private WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 3)]
	[BasicSetting]
	public string FuturesPrivateWsEndpoint { get; set; } = _defaultFuturesPrivateWsEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Passphrase), Passphrase)
			.Set(nameof(Sections), Sections.Select(static section => section.To<string>()).JoinComma())
			.Set(nameof(SpotRestEndpoint), SpotRestEndpoint)
			.Set(nameof(FuturesRestEndpoint), FuturesRestEndpoint)
			.Set(nameof(SpotPublicWsEndpoint), SpotPublicWsEndpoint)
			.Set(nameof(SpotPrivateWsEndpoint), SpotPrivateWsEndpoint)
			.Set(nameof(FuturesPublicWsEndpoint), FuturesPublicWsEndpoint)
			.Set(nameof(FuturesPrivateWsEndpoint), FuturesPrivateWsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));

		var sections = storage.GetValue<string>(nameof(Sections));
		Sections = sections.IsEmpty()
			? Enumerator.GetValues<WeexSections>()
			: [.. sections.SplitByComma().Select(static section => section.To<WeexSections>())];

		SpotRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotRestEndpoint), SpotRestEndpoint),
			_defaultSpotRestEndpoint, "https");
		FuturesRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(FuturesRestEndpoint), FuturesRestEndpoint),
			_defaultFuturesRestEndpoint, "https");
		SpotPublicWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotPublicWsEndpoint), SpotPublicWsEndpoint),
			_defaultSpotPublicWsEndpoint, "wss");
		SpotPrivateWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotPrivateWsEndpoint), SpotPrivateWsEndpoint),
			_defaultSpotPrivateWsEndpoint, "wss");
		FuturesPublicWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(FuturesPublicWsEndpoint), FuturesPublicWsEndpoint),
			_defaultFuturesPublicWsEndpoint, "wss");
		FuturesPrivateWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(FuturesPrivateWsEndpoint), FuturesPrivateWsEndpoint),
			_defaultFuturesPrivateWsEndpoint, "wss");
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
