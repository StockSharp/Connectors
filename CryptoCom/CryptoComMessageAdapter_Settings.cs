namespace StockSharp.CryptoCom;

/// <summary>
/// Crypto.com Exchange market sections.
/// </summary>
[DataContract]
[Serializable]
public enum CryptoComSections
{
	/// <summary>
	/// Spot and margin markets.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SpotKey)]
	Spot,

	/// <summary>
	/// Perpetual and dated derivatives.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DerivativesKey)]
	Derivatives,
}

/// <summary>
/// The message adapter for Crypto.com Exchange.
/// </summary>
[MediaIcon(Media.MediaNames.cryptocom)]
[Doc("topics/api/connectors/crypto_exchanges/crypto_com.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CryptoComExchangeKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(CryptoComOrderCondition))]
public partial class CryptoComMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
{
	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. CryptoComExtensions.TimeFrames.Keys];

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
	[ItemsSource(typeof(CryptoComSections))]
	public IEnumerable<CryptoComSections> Sections
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

	private IEnumerable<CryptoComSections> _sections = Enumerator.GetValues<CryptoComSections>();

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	private const string _productionRestEndpoint = "https://api.crypto.com/exchange/v1";
	private const string _productionMarketWsEndpoint = "wss://stream.crypto.com/exchange/v1/market";
	private const string _productionUserWsEndpoint = "wss://stream.crypto.com/exchange/v1/user";
	private const string _demoRestEndpoint = "https://uat-api.3ona.co/exchange/v1";
	private const string _demoMarketWsEndpoint = "wss://uat-stream.3ona.co/exchange/v1/market";
	private const string _demoUserWsEndpoint = "wss://uat-stream.3ona.co/exchange/v1/user";

	/// <summary>
	/// Production REST endpoint. The official UAT endpoint is selected when <see cref="IsDemo"/> is enabled.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _productionRestEndpoint;

	/// <summary>
	/// Production market-data WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDataKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string MarketWsEndpoint { get; set; } = _productionMarketWsEndpoint;

	/// <summary>
	/// Production authenticated WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TransactionsKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string UserWsEndpoint { get; set; } = _productionUserWsEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(MarketWsEndpoint), MarketWsEndpoint)
			.Set(nameof(UserWsEndpoint), UserWsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));

		var sections = storage.GetValue<string>(nameof(Sections));
		Sections = sections.IsEmpty()
			? Enumerator.GetValues<CryptoComSections>()
			: [.. sections.SplitByComma().Select(s => s.To<CryptoComSections>())];

		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_productionRestEndpoint, "https");
		MarketWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(MarketWsEndpoint), MarketWsEndpoint),
			_productionMarketWsEndpoint, "wss");
		UserWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(UserWsEndpoint), UserWsEndpoint),
			_productionUserWsEndpoint, "wss");
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
		=> base.ToString() + $": Sections={Sections.Select(s => s.To<string>()).JoinComma()}, Key={Key.ToId()}";
}
