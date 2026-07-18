namespace StockSharp.BloFin;

/// <summary>
/// The message adapter for BloFin.
/// </summary>
[MediaIcon(Media.MediaNames.blofin)]
[Doc("topics/api/connectors/crypto_exchanges/blofin.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BloFinKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BloFinOrderCondition))]
public partial class BloFinMessageAdapter : MessageAdapter, IDemoAdapter, IKeySecretAdapter, IPassphraseAdapter
{
	private const string _productionRestEndpoint = "https://openapi.blofin.com";
	private const string _productionPublicWsEndpoint = "wss://openapi.blofin.com/ws/public";
	private const string _productionPrivateWsEndpoint = "wss://openapi.blofin.com/ws/private";
	private const string _demoRestEndpoint = "https://demo-trading-openapi.blofin.com";
	private const string _demoPublicWsEndpoint = "wss://demo-trading-openapi.blofin.com/ws/public";
	private const string _demoPrivateWsEndpoint = "wss://demo-trading-openapi.blofin.com/ws/private";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => BloFinExtensions.TimeFrames;

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

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PassphraseKey,
		Description = LocalizedStrings.PassphraseKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Passphrase { get; set; }

	private bool _isDemo;

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public bool IsDemo
	{
		get => _isDemo;
		set
		{
			if (_isDemo == value)
				return;
			_isDemo = value;
			RestEndpoint = value ? _demoRestEndpoint : _productionRestEndpoint;
			PublicWsEndpoint = value ? _demoPublicWsEndpoint : _productionPublicWsEndpoint;
			PrivateWsEndpoint = value ? _demoPrivateWsEndpoint : _productionPrivateWsEndpoint;
			OnPropertyChanged();
		}
	}

	/// <summary>
	/// REST API endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _productionRestEndpoint;

	/// <summary>
	/// Public WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
	[BasicSetting]
	public string PublicWsEndpoint { get; set; } = _productionPublicWsEndpoint;

	/// <summary>
	/// Private WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 1)]
	[BasicSetting]
	public string PrivateWsEndpoint { get; set; } = _productionPrivateWsEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Passphrase), Passphrase)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(PublicWsEndpoint), PublicWsEndpoint)
			.Set(nameof(PrivateWsEndpoint), PrivateWsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			IsDemo ? _demoRestEndpoint : _productionRestEndpoint, "https");
		PublicWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(PublicWsEndpoint), PublicWsEndpoint),
			IsDemo ? _demoPublicWsEndpoint : _productionPublicWsEndpoint, "wss");
		PrivateWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(PrivateWsEndpoint), PrivateWsEndpoint),
			IsDemo ? _demoPrivateWsEndpoint : _productionPrivateWsEndpoint, "wss");
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
		=> base.ToString() + $": Demo={IsDemo}, Key={Key.ToId()}";
}
