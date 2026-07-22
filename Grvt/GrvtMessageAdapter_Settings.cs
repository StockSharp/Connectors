namespace StockSharp.Grvt;

/// <summary>
/// GRVT environments.
/// </summary>
[DataContract]
[Serializable]
public enum GrvtEnvironments
{
	/// <summary>
	/// Production environment.
	/// </summary>
	[EnumMember]
	Production,

	/// <summary>
	/// Public testnet environment.
	/// </summary>
	[EnumMember]
	Testnet,
}

/// <summary>
/// The message adapter for GRVT.
/// </summary>
[MediaIcon(Media.MediaNames.grvt)]
[Doc("topics/api/connectors/crypto_exchanges/grvt.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GrvtKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(GrvtOrderCondition))]
public partial class GrvtMessageAdapter : MessageAdapter, IKeySecretAdapter,
	IDemoAdapter
{
	private const string _productionEdge = "https://edge.grvt.io";
	private const string _productionMarket = "https://market-data.grvt.io";
	private const string _productionTrading = "https://trades.grvt.io";
	private const string _productionMarketWs =
		"wss://market-data.grvt.io/ws/full";
	private const string _productionTradingWs =
		"wss://trades.grvt.io/ws/full";
	private const string _testnetEdge = "https://edge.testnet.grvt.io";
	private const string _testnetMarket =
		"https://market-data.testnet.grvt.io";
	private const string _testnetTrading = "https://trades.testnet.grvt.io";
	private const string _testnetMarketWs =
		"wss://market-data.testnet.grvt.io/ws/full";
	private const string _testnetTradingWs =
		"wss://trades.testnet.grvt.io/ws/full";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		[.. GrvtExtensions.TimeFrames.Keys];

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
	/// GRVT trading subaccount identifier.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string SubAccountId { get; set; }

	private GrvtEnvironments _environment;

	/// <summary>
	/// GRVT environment.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ModeKey,
		Description = LocalizedStrings.ModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public GrvtEnvironments Environment
	{
		get => _environment;
		set
		{
			if (!System.Enum.IsDefined(value))
				throw new ArgumentOutOfRangeException(nameof(value), value,
					"Unsupported GRVT environment.");
			if (_environment == value)
				return;
			var old = GetDefaultEndpoints(_environment);
			var next = GetDefaultEndpoints(value);
			EdgeEndpoint = ReplaceDefault(EdgeEndpoint, old.Edge, next.Edge);
			MarketDataEndpoint = ReplaceDefault(MarketDataEndpoint,
				old.Market, next.Market);
			TradingEndpoint = ReplaceDefault(TradingEndpoint, old.Trading,
				next.Trading);
			MarketWebSocketEndpoint = ReplaceDefault(MarketWebSocketEndpoint,
				old.MarketWs, next.MarketWs);
			TradingWebSocketEndpoint = ReplaceDefault(
				TradingWebSocketEndpoint, old.TradingWs, next.TradingWs);
			_environment = value;
		}
	}

	/// <inheritdoc />
	[Browsable(false)]
	public bool IsDemo
	{
		get => Environment == GrvtEnvironments.Testnet;
		set => Environment = value
			? GrvtEnvironments.Testnet
			: GrvtEnvironments.Production;
	}

	/// <summary>
	/// Authentication endpoint host.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string EdgeEndpoint { get; set; } = _productionEdge;

	/// <summary>
	/// Market data REST endpoint host.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDataKey,
		Description = LocalizedStrings.MarketDataKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string MarketDataEndpoint { get; set; } = _productionMarket;

	/// <summary>
	/// Trading REST endpoint host.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TransactionsKey,
		Description = LocalizedStrings.TransactionsKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string TradingEndpoint { get; set; } = _productionTrading;

	/// <summary>
	/// Market data WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDataKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string MarketWebSocketEndpoint { get; set; } =
		_productionMarketWs;

	/// <summary>
	/// Trading WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TransactionsKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string TradingWebSocketEndpoint { get; set; } =
		_productionTradingWs;

	/// <summary>
	/// Snapshot publication interval in milliseconds.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.WebSocketKey,
		Order = 2)]
	[BasicSetting]
	public int SnapshotInterval { get; set; } = 500;

	/// <summary>
	/// Default order-book depth.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		Description = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.WebSocketKey,
		Order = 3)]
	[BasicSetting]
	public int MarketDepth { get; set; } = 50;

	internal int ChainId => Environment == GrvtEnvironments.Production
		? 325
		: 326;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(SubAccountId), SubAccountId)
			.Set(nameof(Environment), Environment)
			.Set(nameof(EdgeEndpoint), EdgeEndpoint)
			.Set(nameof(MarketDataEndpoint), MarketDataEndpoint)
			.Set(nameof(TradingEndpoint), TradingEndpoint)
			.Set(nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint)
			.Set(nameof(TradingWebSocketEndpoint), TradingWebSocketEndpoint)
			.Set(nameof(SnapshotInterval), SnapshotInterval)
			.Set(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		SubAccountId = storage.GetValue<string>(nameof(SubAccountId));
		Environment = storage.GetValue(nameof(Environment), Environment);
		var defaults = GetDefaultEndpoints(Environment);
		EdgeEndpoint = NormalizeHttp(storage.GetValue(nameof(EdgeEndpoint),
			EdgeEndpoint), defaults.Edge);
		MarketDataEndpoint = NormalizeHttp(storage.GetValue(
			nameof(MarketDataEndpoint), MarketDataEndpoint), defaults.Market);
		TradingEndpoint = NormalizeHttp(storage.GetValue(nameof(TradingEndpoint),
			TradingEndpoint), defaults.Trading);
		MarketWebSocketEndpoint = NormalizeWebSocket(storage.GetValue(
			nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint),
			defaults.MarketWs);
		TradingWebSocketEndpoint = NormalizeWebSocket(storage.GetValue(
			nameof(TradingWebSocketEndpoint), TradingWebSocketEndpoint),
			defaults.TradingWs);
		SnapshotInterval = ValidateSnapshotInterval(storage.GetValue(
			nameof(SnapshotInterval), SnapshotInterval));
		MarketDepth = ValidateDepth(storage.GetValue(nameof(MarketDepth),
			MarketDepth));
	}

	internal static int ValidateSnapshotInterval(int value)
		=> value is 500 or 1000 or 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"GRVT snapshot interval must be 500, 1000, or 5000 ms.");

	internal static int ValidateDepth(int value)
		=> value is 10 or 50 or 100 or 500
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"GRVT order-book depth must be 10, 50, 100, or 500.");

	private static (string Edge, string Market, string Trading,
		string MarketWs, string TradingWs) GetDefaultEndpoints(
		GrvtEnvironments environment)
		=> environment switch
		{
			GrvtEnvironments.Production => (_productionEdge,
				_productionMarket, _productionTrading, _productionMarketWs,
				_productionTradingWs),
			GrvtEnvironments.Testnet => (_testnetEdge, _testnetMarket,
				_testnetTrading, _testnetMarketWs, _testnetTradingWs),
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, "Unsupported GRVT environment."),
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
			throw new ArgumentException("GRVT endpoint must be an HTTP or HTTPS URI.",
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
			throw new ArgumentException("GRVT endpoint must be a WebSocket URI.",
				nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Environment={Environment}, " +
			$"SubAccount={SubAccountId}, Key={Key.ToId()}";
}
