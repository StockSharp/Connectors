namespace StockSharp.Pionex;

/// <summary>
/// Pionex market sections.
/// </summary>
[DataContract]
[Serializable]
public enum PionexSections
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
/// The message adapter for Pionex.
/// </summary>
[MediaIcon(Media.MediaNames.pionex)]
[Doc("topics/api/connectors/crypto_exchanges/pionex.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PionexKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(PionexOrderCondition))]
public partial class PionexMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.pionex.com";
	private const string _defaultPublicWsEndpoint = "wss://ws.pionex.com/wsPub";
	private const string _defaultSpotPrivateWsEndpoint = "wss://ws.pionex.com/ws";
	private const string _defaultFuturesPrivateWsEndpoint = "wss://ws.pionex.com/wsUA";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. PionexExtensions.TimeFrames.Keys];

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	private IEnumerable<PionexSections> _sections = Enumerator.GetValues<PionexSections>();

	/// <summary>
	/// Enabled market sections.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	[ItemsSource(typeof(PionexSections))]
	public IEnumerable<PionexSections> Sections
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
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>
	/// Public WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
	[BasicSetting]
	public string PublicWsEndpoint { get; set; } = _defaultPublicWsEndpoint;

	/// <summary>
	/// Spot private WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 1)]
	[BasicSetting]
	public string SpotPrivateWsEndpoint { get; set; } = _defaultSpotPrivateWsEndpoint;

	/// <summary>
	/// Futures private WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 2)]
	[BasicSetting]
	public string FuturesPrivateWsEndpoint { get; set; } = _defaultFuturesPrivateWsEndpoint;

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
			.Set(nameof(SpotPrivateWsEndpoint), SpotPrivateWsEndpoint)
			.Set(nameof(FuturesPrivateWsEndpoint), FuturesPrivateWsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		var sections = storage.GetValue<string>(nameof(Sections));
		Sections = sections.IsEmpty()
			? Enumerator.GetValues<PionexSections>()
			: [.. sections.SplitByComma().Select(static section => section.To<PionexSections>())];
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint, "https");
		PublicWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(PublicWsEndpoint), PublicWsEndpoint),
			_defaultPublicWsEndpoint, "wss");
		SpotPrivateWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotPrivateWsEndpoint), SpotPrivateWsEndpoint),
			_defaultSpotPrivateWsEndpoint, "wss");
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
