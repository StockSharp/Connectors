namespace StockSharp.HashKey;

/// <summary>
/// HashKey Global market sections.
/// </summary>
[DataContract]
[Serializable]
public enum HashKeySections
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
/// The message adapter for HashKey Global.
/// </summary>
[MediaIcon(Media.MediaNames.hashkey)]
[Doc("topics/api/connectors/crypto_exchanges/hashkey_global.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.HashKeyGlobalKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(HashKeyOrderCondition))]
public partial class HashKeyMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
{
	private const string _productionRestEndpoint = "https://api-glb.hashkey.com";
	private const string _sandboxRestEndpoint = "https://api-glb.sim.hashkeydev.com";
	private const string _productionPublicWsEndpoint =
		"wss://stream-glb.hashkey.com/quote/ws/v2";
	private const string _sandboxPublicWsEndpoint =
		"wss://stream-glb.sim.hashkeydev.com/quote/ws/v2";
	private const string _productionPrivateWsEndpoint =
		"wss://stream-glb.hashkey.com/api/v1/ws";
	private const string _sandboxPrivateWsEndpoint =
		"wss://stream-glb.sim.hashkeydev.com/api/v1/ws";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => HashKeyExtensions.TimeFrames;

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

	private IEnumerable<HashKeySections> _sections = Enumerator.GetValues<HashKeySections>();

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
	[ItemsSource(typeof(HashKeySections))]
	public IEnumerable<HashKeySections> Sections
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

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>
	/// REST API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _productionRestEndpoint;

	/// <summary>
	/// Public WebSocket v2 endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string PublicWebSocketEndpoint { get; set; } = _productionPublicWsEndpoint;

	/// <summary>
	/// Private WebSocket base endpoint. The listen key is appended by the connector.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string PrivateWebSocketEndpoint { get; set; } = _productionPrivateWsEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Sections), Sections.Select(static section => section.To<string>()).JoinComma())
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(PublicWebSocketEndpoint), PublicWebSocketEndpoint)
			.Set(nameof(PrivateWebSocketEndpoint), PrivateWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		var sections = storage.GetValue<string>(nameof(Sections));
		Sections = sections.IsEmpty()
			? Enumerator.GetValues<HashKeySections>()
			: [.. sections.SplitByComma().Select(static section => section.To<HashKeySections>())];
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_productionRestEndpoint, "https");
		PublicWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(PublicWebSocketEndpoint), PublicWebSocketEndpoint),
			_productionPublicWsEndpoint, "wss");
		PrivateWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(PrivateWebSocketEndpoint), PrivateWebSocketEndpoint),
			_productionPrivateWsEndpoint, "wss");
	}

	private string GetRestEndpoint()
		=> IsDemo && RestEndpoint.EqualsIgnoreCase(_productionRestEndpoint)
			? _sandboxRestEndpoint
			: RestEndpoint;

	private string GetPublicWebSocketEndpoint()
		=> IsDemo && PublicWebSocketEndpoint.EqualsIgnoreCase(_productionPublicWsEndpoint)
			? _sandboxPublicWsEndpoint
			: PublicWebSocketEndpoint;

	private string GetPrivateWebSocketEndpoint()
		=> IsDemo && PrivateWebSocketEndpoint.EqualsIgnoreCase(_productionPrivateWsEndpoint)
			? _sandboxPrivateWsEndpoint
			: PrivateWebSocketEndpoint;

	private static string NormalizeEndpoint(string endpoint, string fallback, string scheme)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() +
			$": Sections={Sections.Select(static section => section.To<string>()).JoinComma()}, " +
			$"Demo={IsDemo}, Key={Key.ToId()}";
}
