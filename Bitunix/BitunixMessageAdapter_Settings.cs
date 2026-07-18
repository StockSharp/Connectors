namespace StockSharp.Bitunix;

/// <summary>
/// Bitunix market sections.
/// </summary>
[DataContract]
[Serializable]
public enum BitunixSections
{
	/// <summary>
	/// Spot market.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SpotKey)]
	Spot,

	/// <summary>
	/// USDT-margined perpetual futures.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FuturesKey)]
	Futures,
}

/// <summary>
/// The message adapter for Bitunix.
/// </summary>
[MediaIcon(Media.MediaNames.bitunix)]
[Doc("topics/api/connectors/crypto_exchanges/bitunix.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitunixKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BitunixOrderCondition))]
public partial class BitunixMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultSpotRestEndpoint = "https://openapi.bitunix.com";
	private const string _defaultFuturesRestEndpoint = "https://fapi.bitunix.com";
	private const string _defaultFuturesPublicWsEndpoint = "wss://fapi.bitunix.com/public/";
	private const string _defaultFuturesPrivateWsEndpoint = "wss://fapi.bitunix.com/private/";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => BitunixExtensions.TimeFrames;

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

	private IEnumerable<BitunixSections> _sections = Enumerator.GetValues<BitunixSections>();

	/// <summary>
	/// Enabled market sections.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	[ItemsSource(typeof(BitunixSections))]
	public IEnumerable<BitunixSections> Sections
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
	/// Spot REST polling interval.
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
				throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue);
			_pollingInterval = value;
		}
	}

	/// <summary>
	/// Spot REST API endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string SpotRestEndpoint { get; set; } = _defaultSpotRestEndpoint;

	/// <summary>
	/// Futures REST API endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	[BasicSetting]
	public string FuturesRestEndpoint { get; set; } = _defaultFuturesRestEndpoint;

	/// <summary>
	/// Futures public WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
	[BasicSetting]
	public string FuturesPublicWsEndpoint { get; set; } = _defaultFuturesPublicWsEndpoint;

	/// <summary>
	/// Futures private WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 1)]
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
			.Set(nameof(FuturesRestEndpoint), FuturesRestEndpoint)
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
			? Enumerator.GetValues<BitunixSections>()
			: [.. sections.SplitByComma().Select(static section => section.To<BitunixSections>())];
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
		SpotRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotRestEndpoint), SpotRestEndpoint),
			_defaultSpotRestEndpoint, "https");
		FuturesRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(FuturesRestEndpoint), FuturesRestEndpoint),
			_defaultFuturesRestEndpoint, "https");
		FuturesPublicWsEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(FuturesPublicWsEndpoint), FuturesPublicWsEndpoint),
			_defaultFuturesPublicWsEndpoint, "wss", false);
		FuturesPrivateWsEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(FuturesPrivateWsEndpoint), FuturesPrivateWsEndpoint),
			_defaultFuturesPrivateWsEndpoint, "wss", false);
	}

	private static string NormalizeEndpoint(string endpoint, string fallback, string scheme,
		bool trimTrailingSlash = true)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
		return trimTrailingSlash ? endpoint.TrimEnd('/') : endpoint.TrimEnd('/') + "/";
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Sections={Sections.Select(static section => section.To<string>()).JoinComma()}, Key={Key.ToId()}";
}
