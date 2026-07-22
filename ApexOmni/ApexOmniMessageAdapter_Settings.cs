namespace StockSharp.ApexOmni;

/// <summary>
/// ApeX Omni environments.
/// </summary>
[DataContract]
[Serializable]
public enum ApexOmniEnvironments
{
	/// <summary>Production environment.</summary>
	[EnumMember]
	Production,

	/// <summary>Public testnet environment.</summary>
	[EnumMember]
	Testnet,
}

/// <summary>
/// The message adapter for ApeX Omni.
/// </summary>
[MediaIcon(Media.MediaNames.apex_omni)]
[Doc("topics/api/connectors/crypto_exchanges/apex_omni.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ApexOmniKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(ApexOmniOrderCondition))]
public partial class ApexOmniMessageAdapter : MessageAdapter,
	IKeySecretAdapter, IDemoAdapter, IPassphraseAdapter
{
	private const string _productionRest = "https://omni.apex.exchange";
	private const string _testnetRest = "https://testnet.omni.apex.exchange";
	private const string _productionWs = "wss://quote.omni.apex.exchange";
	private const string _testnetWs = "wss://qa-quote.omni.apex.exchange";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		[.. ApexOmniExtensions.TimeFrames.Keys];

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// API-key passphrase.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PassphraseKey,
		Description = LocalizedStrings.PasswordKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Passphrase { get; set; }

	/// <summary>
	/// Hex-encoded zkLink signing seed obtained during ApeX onboarding.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ApexOmniSeedKey,
		Description = LocalizedStrings.ApexOmniSeedDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public SecureString Seeds { get; set; }

	private ApexOmniEnvironments _environment;

	/// <summary>
	/// ApeX Omni environment.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ModeKey,
		Description = LocalizedStrings.ModeKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public ApexOmniEnvironments Environment
	{
		get => _environment;
		set
		{
			if (!Enum.IsDefined(value))
				throw new ArgumentOutOfRangeException(nameof(value), value,
					"Unsupported ApeX Omni environment.");
			if (_environment == value)
				return;
			var old = GetDefaultEndpoints(_environment);
			var next = GetDefaultEndpoints(value);
			RestEndpoint = ReplaceDefault(RestEndpoint, old.RestApi,
				next.RestApi);
			WebSocketEndpoint = ReplaceDefault(WebSocketEndpoint,
				old.WebSocketApi, next.WebSocketApi);
			_environment = value;
		}
	}

	/// <inheritdoc />
	[Browsable(false)]
	public bool IsDemo
	{
		get => Environment == ApexOmniEnvironments.Testnet;
		set => Environment = value
			? ApexOmniEnvironments.Testnet
			: ApexOmniEnvironments.Production;
	}

	/// <summary>
	/// REST endpoint host.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _productionRest;

	/// <summary>
	/// WebSocket endpoint host.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } = _productionWs;

	/// <summary>
	/// Public order-book depth (25 or 200 levels).
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		Description = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.WebSocketKey, Order = 1)]
	[BasicSetting]
	public int MarketDepth { get; set; } = 200;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Passphrase), Passphrase)
			.Set(nameof(Seeds), Seeds)
			.Set(nameof(Environment), Environment)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));
		Seeds = storage.GetValue<SecureString>(nameof(Seeds));
		Environment = storage.GetValue(nameof(Environment), Environment);
		var defaults = GetDefaultEndpoints(Environment);
		RestEndpoint = NormalizeHttp(storage.GetValue(nameof(RestEndpoint),
			RestEndpoint), defaults.RestApi);
		WebSocketEndpoint = NormalizeWebSocket(storage.GetValue(
			nameof(WebSocketEndpoint), WebSocketEndpoint),
			defaults.WebSocketApi);
		MarketDepth = ValidateDepth(storage.GetValue(nameof(MarketDepth),
			MarketDepth));
	}

	internal static int ValidateDepth(int value)
		=> value is 25 or 200
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"ApeX Omni order-book depth must be 25 or 200.");

	private static (string RestApi, string WebSocketApi) GetDefaultEndpoints(
		ApexOmniEnvironments environment)
		=> environment switch
		{
			ApexOmniEnvironments.Production => (_productionRest, _productionWs),
			ApexOmniEnvironments.Testnet => (_testnetRest, _testnetWs),
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, "Unsupported ApeX Omni environment."),
		};

	private static string ReplaceDefault(string endpoint, string oldValue,
		string newValue)
		=> endpoint.IsEmpty() || endpoint.EqualsIgnoreCase(oldValue)
			? newValue
			: endpoint;

	private static string NormalizeHttp(string endpoint, string fallback)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			(!uri.Scheme.Equals(Uri.UriSchemeHttp,
				StringComparison.OrdinalIgnoreCase) &&
			 !uri.Scheme.Equals(Uri.UriSchemeHttps,
				StringComparison.OrdinalIgnoreCase)))
			throw new ArgumentException(
				"ApeX Omni endpoint must be an HTTP or HTTPS URI.",
				nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	private static string NormalizeWebSocket(string endpoint, string fallback)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "wss://" + endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("ws" or "wss"))
			throw new ArgumentException(
				"ApeX Omni endpoint must be a WebSocket URI.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Environment={Environment}, Key={Key.ToId()}";
}
