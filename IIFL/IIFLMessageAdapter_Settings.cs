namespace StockSharp.IIFL;

/// <summary>The message adapter for IIFL Markets Open API.</summary>
[MediaIcon(Media.MediaNames.iifl)]
[Doc("topics/api/connectors/stock_market/iifl.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.IIFLKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Asia |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options)]
[OrderCondition(typeof(IIFLOrderCondition))]
public partial class IIFLMessageAdapter : MessageAdapter,
	IKeySecretAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.iiflcapital.com/v1";
	private const string _defaultBridgeHost =
		"bridge.iiflcapital.com";
	private const int _defaultBridgePort = 8883;
	private const string _defaultTokenValidationEndpoint =
		"https://idaas.iiflsecurities.com/v1/access/check/token";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppKeyKey,
		Description = LocalizedStrings.IIFLAppKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppSecretKey,
		Description = LocalizedStrings.IIFLAppSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>IIFL trading client identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClientIdKey,
		Description = LocalizedStrings.IIFLClientIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string ClientId { get; set; }

	/// <summary>Daily authorization code returned by IIFL.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AuthorizationCodeKey,
		Description = LocalizedStrings.IIFLAuthorizationCodeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string AuthorizationCode { get; set; }

	/// <summary>Existing daily userSession bearer token.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SessionTokenKey,
		Description = LocalizedStrings.IIFLSessionTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public SecureString SessionToken { get; set; }

	/// <summary>Portfolio name exposed to StockSharp.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PortfolioNameKey,
		Description = LocalizedStrings.PortfolioNameKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public string PortfolioName { get; set; }

	/// <summary>IIFL Open API root endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>IIFL bridge MQTT host.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StreamingHostKey,
		Description = LocalizedStrings.IIFLBridgeHostDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string BridgeHost { get; set; } = _defaultBridgeHost;

	private int _bridgePort = _defaultBridgePort;

	/// <summary>IIFL bridge MQTT TLS port.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StreamingPortKey,
		Description = LocalizedStrings.StreamingPortKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public int BridgePort
	{
		get => _bridgePort;
		set => _bridgePort = value is > 0 and <= ushort.MaxValue
			? value
			: throw new ArgumentOutOfRangeException(nameof(value));
	}

	/// <summary>IIFL session-token validation endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StreamingAuthorizationEndpointKey,
		Description = LocalizedStrings.IIFLTokenValidationEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string TokenValidationEndpoint { get; set; } =
		_defaultTokenValidationEndpoint;

	/// <summary>Enable the official MQTT market and order stream.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StreamingKey,
		Description = LocalizedStrings.StreamingKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public bool StreamingEnabled { get; set; } = true;

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>REST polling interval for portfolios and candles.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PollingIntervalKey,
		Description = LocalizedStrings.PollingIntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval =
			value >= TimeSpan.FromSeconds(1) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value),
					value, "IIFL polling interval must be between one " +
						"second and five minutes.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(AuthorizationCode), AuthorizationCode)
			.Set(nameof(SessionToken), SessionToken)
			.Set(nameof(PortfolioName), PortfolioName)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(BridgeHost), BridgeHost)
			.Set(nameof(BridgePort), BridgePort)
			.Set(nameof(TokenValidationEndpoint),
				TokenValidationEndpoint)
			.Set(nameof(StreamingEnabled), StreamingEnabled)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		ClientId = storage.GetValue<string>(nameof(ClientId));
		AuthorizationCode = storage.GetValue<string>(
			nameof(AuthorizationCode));
		SessionToken = storage.GetValue<SecureString>(
			nameof(SessionToken));
		PortfolioName = storage.GetValue<string>(nameof(PortfolioName));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint),
			_defaultRestEndpoint);
		BridgeHost = storage.GetValue(nameof(BridgeHost),
			_defaultBridgeHost);
		BridgePort = storage.GetValue(nameof(BridgePort),
			_defaultBridgePort);
		TokenValidationEndpoint = storage.GetValue(
			nameof(TokenValidationEndpoint),
			_defaultTokenValidationEndpoint);
		StreamingEnabled = storage.GetValue(
			nameof(StreamingEnabled), true);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			TimeSpan.FromSeconds(5));
	}
}
