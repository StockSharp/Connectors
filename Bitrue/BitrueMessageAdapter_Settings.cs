namespace StockSharp.Bitrue;

/// <summary>
/// Bitrue market sections.
/// </summary>
[DataContract]
[Serializable]
public enum BitrueSections
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
/// The message adapter for Bitrue.
/// </summary>
[MediaIcon(Media.MediaNames.bitrue)]
[Doc("topics/api/connectors/crypto_exchanges/bitrue.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitrueKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BitrueOrderCondition))]
public partial class BitrueMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultSpotRestEndpoint = "https://openapi.bitrue.com";
	private const string _defaultSpotStreamRestEndpoint = "https://open.bitrue.com";
	private const string _defaultSpotPublicWsEndpoint = "wss://ws.bitrue.com/market/ws";
	private const string _defaultSpotPrivateWsEndpoint = "wss://wsapi.bitrue.com";
	private const string _defaultFuturesRestEndpoint = "https://fapi.bitrue.com";
	private const string _defaultFuturesStreamRestEndpoint = "https://fapiws-auth.bitrue.com";
	private const string _defaultFuturesPublicWsEndpoint =
		"wss://fmarket-ws.bitrue.com/kline-api/ws";
	private const string _defaultFuturesPrivateWsEndpoint = "wss://fapiws.bitrue.com";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => BitrueExtensions.TimeFrames;

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

	private IEnumerable<BitrueSections> _sections = Enumerator.GetValues<BitrueSections>();

	/// <summary>
	/// Enabled market sections.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	[ItemsSource(typeof(BitrueSections))]
	public IEnumerable<BitrueSections> Sections
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

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(1);

	/// <summary>
	/// Spot REST polling interval for streams that Bitrue does not publish over WebSocket.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set
		{
			if (value <= TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(value), value,
					LocalizedStrings.InvalidValue);
			_pollingInterval = value;
		}
	}

	/// <summary>
	/// Spot trading REST endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string SpotRestEndpoint { get; set; } = _defaultSpotRestEndpoint;

	/// <summary>
	/// Spot listen-key REST endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	[BasicSetting]
	public string SpotStreamRestEndpoint { get; set; } = _defaultSpotStreamRestEndpoint;

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
	/// Futures trading REST endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 2)]
	[BasicSetting]
	public string FuturesRestEndpoint { get; set; } = _defaultFuturesRestEndpoint;

	/// <summary>
	/// Futures listen-key REST endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 3)]
	[BasicSetting]
	public string FuturesStreamRestEndpoint { get; set; } = _defaultFuturesStreamRestEndpoint;

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
			.Set(nameof(Sections), Sections.Select(static section => section.To<string>()).JoinComma())
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(SpotRestEndpoint), SpotRestEndpoint)
			.Set(nameof(SpotStreamRestEndpoint), SpotStreamRestEndpoint)
			.Set(nameof(SpotPublicWsEndpoint), SpotPublicWsEndpoint)
			.Set(nameof(SpotPrivateWsEndpoint), SpotPrivateWsEndpoint)
			.Set(nameof(FuturesRestEndpoint), FuturesRestEndpoint)
			.Set(nameof(FuturesStreamRestEndpoint), FuturesStreamRestEndpoint)
			.Set(nameof(FuturesPublicWsEndpoint), FuturesPublicWsEndpoint)
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
			? Enumerator.GetValues<BitrueSections>()
			: [.. sections.SplitByComma().Select(static section => section.To<BitrueSections>())];
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
		SpotRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotRestEndpoint),
			SpotRestEndpoint), _defaultSpotRestEndpoint, "https");
		SpotStreamRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotStreamRestEndpoint),
			SpotStreamRestEndpoint), _defaultSpotStreamRestEndpoint, "https");
		SpotPublicWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotPublicWsEndpoint),
			SpotPublicWsEndpoint), _defaultSpotPublicWsEndpoint, "wss");
		SpotPrivateWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotPrivateWsEndpoint),
			SpotPrivateWsEndpoint), _defaultSpotPrivateWsEndpoint, "wss");
		FuturesRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(FuturesRestEndpoint),
			FuturesRestEndpoint), _defaultFuturesRestEndpoint, "https");
		FuturesStreamRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(FuturesStreamRestEndpoint),
			FuturesStreamRestEndpoint), _defaultFuturesStreamRestEndpoint, "https");
		FuturesPublicWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(FuturesPublicWsEndpoint),
			FuturesPublicWsEndpoint), _defaultFuturesPublicWsEndpoint, "wss");
		FuturesPrivateWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(FuturesPrivateWsEndpoint),
			FuturesPrivateWsEndpoint), _defaultFuturesPrivateWsEndpoint, "wss");
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
