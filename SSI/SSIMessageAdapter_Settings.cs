namespace StockSharp.SSI;

/// <summary>The message adapter for SSI FastConnect API v3.</summary>
[MediaIcon(Media.MediaNames.ssi)]
[Doc("topics/api/connectors/stock_market/ssi.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SSIKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.VietnamKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Asia |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures)]
[OrderCondition(typeof(SSIOrderCondition))]
public partial class SSIMessageAdapter : MessageAdapter,
	IKeySecretAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.ssi.com.vn";
	private const string _defaultStreamingEndpoint =
		"wss://stream.ssi.com.vn/ws/v3";

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
		Description = LocalizedStrings.SecretKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>FastConnect client identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClientIdKey,
		Description = LocalizedStrings.ClientIdKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string ClientId { get; set; }

	/// <summary>RSA private key issued by SSI.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SSIRsaPrivateKeyKey,
		Description = LocalizedStrings.SSIRsaPrivateKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public SecureString PrivateKey { get; set; }

	/// <summary>Current OTP used when creating a session.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SSITradingOtpKey,
		Description = LocalizedStrings.SSITradingOtpDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public SecureString Otp { get; set; }

	/// <summary>Default SSI trading account.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string Account { get; set; }

	/// <summary>FastConnect REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>FastConnect WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StreamingEndpointKey,
		Description = LocalizedStrings.StreamingEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string StreamingEndpoint { get; set; } =
		_defaultStreamingEndpoint;

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Portfolio and order polling interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PollingIntervalKey,
		Description = LocalizedStrings.PollingIntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval =
			value >= TimeSpan.FromSeconds(1) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value),
					value, "SSI polling interval must be between one " +
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
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(Otp), Otp)
			.Set(nameof(Account), Account)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(StreamingEndpoint), StreamingEndpoint)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		ClientId = storage.GetValue<string>(nameof(ClientId));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		Otp = storage.GetValue<SecureString>(nameof(Otp));
		Account = storage.GetValue<string>(nameof(Account));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint),
			_defaultRestEndpoint);
		StreamingEndpoint = storage.GetValue(
			nameof(StreamingEndpoint), _defaultStreamingEndpoint);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			TimeSpan.FromSeconds(5));
	}
}
