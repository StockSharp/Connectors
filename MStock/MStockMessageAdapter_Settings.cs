namespace StockSharp.MStock;

/// <summary>The message adapter for m.Stock Trading API.</summary>
[MediaIcon(Media.MediaNames.mstock)]
[Doc("topics/api/connectors/stock_market/mstock.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MStockKey,
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
[OrderCondition(typeof(MStockOrderCondition))]
public partial class MStockMessageAdapter : MessageAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.mstock.trade";
	private const string _defaultStreamingEndpoint =
		"wss://ws.mstock.trade";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ApiKeyKey,
		Description = LocalizedStrings.MStockApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>m.Stock client code.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClientCodeKey,
		Description = LocalizedStrings.ClientCodeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string ClientCode { get; set; }

	/// <summary>m.Stock account password.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public SecureString Password { get; set; }

	/// <summary>Current OTP or TOTP code.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MStockOtpKey,
		Description = LocalizedStrings.MStockOtpDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public SecureString Otp { get; set; }

	/// <summary>Use the TOTP verification endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MStockUseTotpKey,
		Description = LocalizedStrings.MStockUseTotpDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public bool UseTotp { get; set; }

	/// <summary>Existing login refresh/request token.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RefreshTokenKey,
		Description = LocalizedStrings.MStockRefreshTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public SecureString RefreshToken { get; set; }

	/// <summary>Existing daily JWT access token.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccessTokenKey,
		Description = LocalizedStrings.MStockAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public SecureString AccessToken { get; set; }

	/// <summary>m.Stock REST root endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public string RestEndpoint { get; set; } =
		_defaultRestEndpoint;

	/// <summary>m.Stock WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StreamingEndpointKey,
		Description = LocalizedStrings.StreamingEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string StreamingEndpoint { get; set; } =
		_defaultStreamingEndpoint;

	/// <summary>Enable the market/order WebSocket stream.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StreamingKey,
		Description = LocalizedStrings.StreamingKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public bool StreamingEnabled { get; set; } = true;

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>REST polling interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PollingIntervalKey,
		Description = LocalizedStrings.PollingIntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval =
			value >= TimeSpan.FromSeconds(1) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value),
					value, "m.Stock polling interval must be " +
						"between one second and five minutes.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(ClientCode), ClientCode)
			.Set(nameof(Password), Password)
			.Set(nameof(Otp), Otp)
			.Set(nameof(UseTotp), UseTotp)
			.Set(nameof(RefreshToken), RefreshToken)
			.Set(nameof(AccessToken), AccessToken)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(StreamingEndpoint), StreamingEndpoint)
			.Set(nameof(StreamingEnabled), StreamingEnabled)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		ClientCode = storage.GetValue<string>(nameof(ClientCode));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Otp = storage.GetValue<SecureString>(nameof(Otp));
		UseTotp = storage.GetValue<bool>(nameof(UseTotp));
		RefreshToken = storage.GetValue<SecureString>(
			nameof(RefreshToken));
		AccessToken = storage.GetValue<SecureString>(
			nameof(AccessToken));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint),
			_defaultRestEndpoint);
		StreamingEndpoint = storage.GetValue(
			nameof(StreamingEndpoint), _defaultStreamingEndpoint);
		StreamingEnabled = storage.GetValue(
			nameof(StreamingEnabled), true);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			TimeSpan.FromSeconds(5));
	}
}
