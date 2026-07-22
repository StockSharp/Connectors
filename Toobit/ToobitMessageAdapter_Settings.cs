namespace StockSharp.Toobit;

/// <summary>
/// Trading sections.
/// </summary>
[DataContract]
[Serializable]
public enum ToobitSections
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
/// The message adapter for Toobit.
/// </summary>
[MediaIcon(Media.MediaNames.toobit)]
[Doc("topics/api/connectors/crypto_exchanges/toobit.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ToobitKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(ToobitOrderCondition))]
public partial class ToobitMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. ToobitExtensions.TimeFrames.Keys];

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
	/// Enabled market sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	[ItemsSource(typeof(ToobitSections))]
	public IEnumerable<ToobitSections> Sections
	{
		get => _sections;
		set
		{
			if (value is null)
				throw new ArgumentNullException(nameof(value));

			var sections = value.Distinct().ToArray();
			if (sections.Length == 0)
				throw new ArgumentOutOfRangeException(nameof(value));

			_sections = sections;
		}
	}

	private IEnumerable<ToobitSections> _sections = Enumerator.GetValues<ToobitSections>();

	private const string _defaultRestEndpoint = "https://api.toobit.com";
	private const string _defaultWsEndpoint = "wss://stream.toobit.com/quote/ws/v1";

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
		Name = LocalizedStrings.WsEndpointKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string WsEndpoint { get; set; } = _defaultWsEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WsEndpoint), WsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));

		var sections = storage.GetValue<string>(nameof(Sections));
		Sections = sections.IsEmpty()
			? Enumerator.GetValues<ToobitSections>()
			: [.. sections.SplitByComma().Select(s => s.To<ToobitSections>())];

		RestEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(RestEndpoint), RestEndpoint), _defaultRestEndpoint, "https");
		WsEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(WsEndpoint), WsEndpoint), _defaultWsEndpoint, "wss");
	}

	private static string NormalizeEndpoint(string endpoint, string fallback, string scheme)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Sections={Sections.Select(s => s.To<string>()).JoinComma()}, Key={Key.ToId()}";
}
