namespace StockSharp.Bullish;

/// <summary>
/// Bullish market sections.
/// </summary>
[DataContract]
[Serializable]
public enum BullishSections
{
	/// <summary>
	/// Spot markets.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotKey)]
	Spot,

	/// <summary>
	/// Perpetual, dated-future, and option markets.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DerivativesKey)]
	Derivatives,
}

/// <summary>
/// The message adapter for Bullish.
/// </summary>
[MediaIcon(Media.MediaNames.bullish)]
[Doc("topics/api/connectors/crypto_exchanges/bullish.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BullishKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BullishOrderCondition))]
public partial class BullishMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.exchange.bullish.com";
	private const string _defaultWsEndpoint = "wss://api.exchange.bullish.com";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. BullishExtensions.TimeFrames.Keys];

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
	/// Preferred Bullish trading account identifier. The primary account is used when empty.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string TradingAccountId { get; set; }

	/// <summary>
	/// Optional Bullish rate-limit token assigned to institutional clients.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccessTokenKey,
		Description = LocalizedStrings.AccessTokenKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString RateLimitToken { get; set; }

	private IEnumerable<BullishSections> _sections = Enumerator.GetValues<BullishSections>();

	/// <summary>
	/// Enabled market sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	[ItemsSource(typeof(BullishSections))]
	public IEnumerable<BullishSections> Sections
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
	/// REST API host.
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
	/// WebSocket API host.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
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
			.Set(nameof(TradingAccountId), TradingAccountId)
			.Set(nameof(RateLimitToken), RateLimitToken)
			.Set(nameof(Sections), Sections.Select(static section => section.To<string>()).JoinComma())
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WsEndpoint), WsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		TradingAccountId = storage.GetValue<string>(nameof(TradingAccountId));
		RateLimitToken = storage.GetValue<SecureString>(nameof(RateLimitToken));
		var sections = storage.GetValue<string>(nameof(Sections));
		Sections = sections.IsEmpty()
			? Enumerator.GetValues<BullishSections>()
			: [.. sections.SplitByComma().Select(static section => section.To<BullishSections>())];
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint, "https");
		WsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(WsEndpoint), WsEndpoint),
			_defaultWsEndpoint, "wss");
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
