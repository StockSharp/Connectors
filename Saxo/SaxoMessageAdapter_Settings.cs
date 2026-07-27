namespace StockSharp.Saxo;

/// <summary>The message adapter for Saxo OpenAPI.</summary>
[MediaIcon(Media.MediaNames.saxo)]
[Doc("topics/api/connectors/stock_market/saxo.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SaxoKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX)]
[OrderCondition(typeof(SaxoOrderCondition))]
public partial class SaxoMessageAdapter : MessageAdapter, IKeySecretAdapter, ITokenAdapter
{
	private const string _simulationRestEndpoint = "https://gateway.saxobank.com/sim/openapi/";
	private const string _liveRestEndpoint = "https://gateway.saxobank.com/openapi/";
	private const string _simulationTokenEndpoint = "https://sim.logonvalidation.net/token";
	private const string _liveTokenEndpoint = "https://live.logonvalidation.net/token";
	private const string _simulationStreamAuthorizeEndpoint = "https://sim-streaming.saxobank.com/sim/oapi/streaming/ws/authorize";
	private const string _liveStreamAuthorizeEndpoint = "https://live-streaming.saxobank.com/oapi/streaming/ws/authorize";
	private const string _simulationWebSocketEndpoint = "wss://sim-streaming.saxobank.com/sim/oapi/streaming/ws/connect";
	private const string _liveWebSocketEndpoint = "wss://live-streaming.saxobank.com/oapi/streaming/ws/connect";

	private SaxoEnvironments _environment = SaxoEnvironments.Simulation;
	private string _restEndpoint = _simulationRestEndpoint;
	private string _tokenEndpoint = _simulationTokenEndpoint;
	private string _streamAuthorizeEndpoint = _simulationStreamAuthorizeEndpoint;
	private string _webSocketEndpoint = _simulationWebSocketEndpoint;

	/// <summary>OAuth access token.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.SaxoAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>OAuth refresh token.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SaxoRefreshTokenKey,
		Description = LocalizedStrings.SaxoRefreshTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public SecureString RefreshToken { get; set; }

	/// <summary>OAuth application key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.SaxoClientIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public SecureString Key { get; set; }

	/// <summary>OAuth application secret.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SaxoClientSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public SecureString Secret { get; set; }

	/// <summary>OAuth redirect URI registered for the application.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SaxoRedirectUriKey,
		Description = LocalizedStrings.SaxoRedirectUriDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string RedirectUri { get; set; }

	/// <summary>Optional default account key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SaxoAccountKeyKey,
		Description = LocalizedStrings.SaxoAccountKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public string AccountKey { get; set; }

	/// <summary>Saxo environment.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SaxoEnvironmentKey,
		Description = LocalizedStrings.SaxoEnvironmentDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public SaxoEnvironments Environment
	{
		get => _environment;
		set
		{
			if (_restEndpoint.EqualsIgnoreCase(GetRestEndpoint(_environment)))
				_restEndpoint = GetRestEndpoint(value);
			if (_tokenEndpoint.EqualsIgnoreCase(GetTokenEndpoint(_environment)))
				_tokenEndpoint = GetTokenEndpoint(value);
			if (_streamAuthorizeEndpoint.EqualsIgnoreCase(GetStreamAuthorizeEndpoint(_environment)))
				_streamAuthorizeEndpoint = GetStreamAuthorizeEndpoint(value);
			if (_webSocketEndpoint.EqualsIgnoreCase(GetWebSocketEndpoint(_environment)))
				_webSocketEndpoint = GetWebSocketEndpoint(value);
			_environment = value;
		}
	}

	/// <summary>REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 7)]
	public string RestEndpoint
	{
		get => _restEndpoint;
		set => _restEndpoint = value;
	}

	/// <summary>OAuth token endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenEndpointKey,
		Description = LocalizedStrings.OAuthTokenEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 8)]
	public string TokenEndpoint
	{
		get => _tokenEndpoint;
		set => _tokenEndpoint = value;
	}

	/// <summary>Streaming authorization endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StreamingAuthorizationEndpointKey,
		Description = LocalizedStrings.StreamingAuthorizationEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 9)]
	public string StreamAuthorizeEndpoint
	{
		get => _streamAuthorizeEndpoint;
		set => _streamAuthorizeEndpoint = value;
	}

	/// <summary>Streaming WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketEndpointKey,
		Description = LocalizedStrings.StreamingWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 10)]
	public string WebSocketEndpoint
	{
		get => _webSocketEndpoint;
		set => _webSocketEndpoint = value;
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(RefreshToken), RefreshToken)
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(RedirectUri), RedirectUri)
			.Set(nameof(AccountKey), AccountKey)
			.Set(nameof(Environment), Environment)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(TokenEndpoint), TokenEndpoint)
			.Set(nameof(StreamAuthorizeEndpoint), StreamAuthorizeEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		RefreshToken = storage.GetValue<SecureString>(nameof(RefreshToken));
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		RedirectUri = storage.GetValue<string>(nameof(RedirectUri));
		AccountKey = storage.GetValue<string>(nameof(AccountKey));
		Environment = storage.GetValue(nameof(Environment), Environment);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		TokenEndpoint = storage.GetValue(nameof(TokenEndpoint), TokenEndpoint);
		StreamAuthorizeEndpoint = storage.GetValue(nameof(StreamAuthorizeEndpoint), StreamAuthorizeEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	private static string GetRestEndpoint(SaxoEnvironments environment)
		=> environment == SaxoEnvironments.Simulation ? _simulationRestEndpoint : _liveRestEndpoint;

	private static string GetTokenEndpoint(SaxoEnvironments environment)
		=> environment == SaxoEnvironments.Simulation ? _simulationTokenEndpoint : _liveTokenEndpoint;

	private static string GetStreamAuthorizeEndpoint(SaxoEnvironments environment)
		=> environment == SaxoEnvironments.Simulation ? _simulationStreamAuthorizeEndpoint : _liveStreamAuthorizeEndpoint;

	private static string GetWebSocketEndpoint(SaxoEnvironments environment)
		=> environment == SaxoEnvironments.Simulation ? _simulationWebSocketEndpoint : _liveWebSocketEndpoint;
}
