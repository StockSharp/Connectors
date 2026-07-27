namespace StockSharp.Ventura;

/// <summary>The message adapter for Ventura Securities EaseAPI.</summary>
[MediaIcon(Media.MediaNames.ventura)]
[Doc("topics/api/connectors/stock_market/ventura.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.VenturaKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Asia |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options)]
[OrderCondition(typeof(VenturaOrderCondition))]
public partial class VenturaMessageAdapter : MessageAdapter,
	IKeySecretAdapter, ITokenAdapter
{
	private static readonly Uri _defaultRestAddress =
		new("https://easeapi.venturasecurities.com/");
	private static readonly Uri _defaultMarketDataAddress =
		new("wss://easeapi-ws.venturasecurities.com/v1/easeapi_mktdata");
	private static readonly Uri _defaultOrderStatusAddress =
		new("wss://easeapi-ws.venturasecurities.com/v1/easeapi_ob");

	/// <inheritdoc />
	[Display(
		Name = "App key",
		Description = "Application key created in the Ventura EaseAPI portal.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		Name = "App secret",
		Description = "Application secret used to create the EaseAPI authorization hash.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>One-time request token returned by the browser login.</summary>
	[Display(
		Name = "Request token",
		Description = "One-time request token returned by the EaseAPI authorization redirect.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public SecureString RequestToken { get; set; }

	/// <inheritdoc />
	[Display(
		Name = "Auth token",
		Description = "EaseAPI bearer token. It is populated after request-token or TOTP login.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Refresh token returned by EaseAPI authorization.</summary>
	[Display(
		Name = "Refresh token",
		Description = "Refresh token returned by EaseAPI authorization.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public SecureString RefreshToken { get; set; }

	/// <summary>Ventura client ID.</summary>
	[Display(
		Name = "Client ID",
		Description = "Ventura trading client ID.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string ClientId { get; set; }

	/// <summary>Ventura account PIN for automated TOTP login.</summary>
	[Display(
		Name = "PIN",
		Description = "Ventura account PIN used only when the auth token and request token are empty.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public SecureString Pin { get; set; }

	/// <summary>Base32 secret for automated TOTP login.</summary>
	[Display(
		Name = "TOTP secret",
		Description = "Base32 authenticator secret used to generate a current TOTP code.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public SecureString TotpSecret { get; set; }

	/// <summary>MAC address registered for TOTP authorization.</summary>
	[Display(
		Name = "MAC address",
		Description = "MAC address sent in the x-mac-address header during TOTP login.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public string MacAddress { get; set; }

	/// <summary>Portfolio name emitted by the connector.</summary>
	[Display(
		Name = "Portfolio name",
		Description = "Portfolio name. When empty, the Ventura client ID is used.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public string PortfolioName { get; set; }

	/// <summary>Default product used for new orders.</summary>
	[Display(
		Name = "Default product",
		Description = "Default Ventura product used for new orders.",
		GroupName = LocalizedStrings.OrderKey,
		Order = 10)]
	public VenturaProducts DefaultProduct { get; set; } =
		VenturaProducts.CashAndCarry;

	/// <summary>Interval for order and portfolio snapshots.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 11)]
	public TimeSpan PollingInterval { get; set; } =
		TimeSpan.FromSeconds(15);

	/// <summary>Maximum number of WebSocket reconnect attempts.</summary>
	[Display(
		Name = "Reconnect attempts",
		Description = "Maximum number of EaseAPI WebSocket reconnect attempts.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 12)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <summary>REST API root address.</summary>
	[Display(
		Name = "REST address",
		Description = "Ventura EaseAPI REST root address.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 13)]
	public Uri RestAddress { get; set; } = _defaultRestAddress;

	/// <summary>Market-data WebSocket address.</summary>
	[Display(
		Name = "Market data address",
		Description = "Ventura EaseAPI market-data WebSocket address.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 14)]
	public Uri MarketDataAddress { get; set; } =
		_defaultMarketDataAddress;

	/// <summary>Order-status WebSocket address.</summary>
	[Display(
		Name = "Order status address",
		Description = "Ventura EaseAPI order-status WebSocket address.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 15)]
	public Uri OrderStatusAddress { get; set; } =
		_defaultOrderStatusAddress;

	/// <summary>Create the browser authorization URL.</summary>
	public Uri CreateAuthorizationUri(string state)
	{
		var key = Key.ThrowIfEmpty(nameof(Key)).UnSecure();
		state.ThrowIfEmpty(nameof(state));
		var root = RestAddress ??
			throw new InvalidOperationException(
				"Ventura EaseAPI REST address is not configured.");
		return new(
			root,
			$"auth/v1/login?app_key={Uri.EscapeDataString(key)}" +
			$"&state={Uri.EscapeDataString(state)}");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(RequestToken), RequestToken)
			.Set(nameof(Token), Token)
			.Set(nameof(RefreshToken), RefreshToken)
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(Pin), Pin)
			.Set(nameof(TotpSecret), TotpSecret)
			.Set(nameof(MacAddress), MacAddress)
			.Set(nameof(PortfolioName), PortfolioName)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts)
			.Set(nameof(RestAddress), RestAddress)
			.Set(nameof(MarketDataAddress), MarketDataAddress)
			.Set(nameof(OrderStatusAddress), OrderStatusAddress);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		RequestToken = storage.GetValue<SecureString>(nameof(RequestToken));
		Token = storage.GetValue<SecureString>(nameof(Token));
		RefreshToken = storage.GetValue<SecureString>(nameof(RefreshToken));
		ClientId = storage.GetValue<string>(nameof(ClientId));
		Pin = storage.GetValue<SecureString>(nameof(Pin));
		TotpSecret = storage.GetValue<SecureString>(nameof(TotpSecret));
		MacAddress = storage.GetValue<string>(nameof(MacAddress));
		PortfolioName = storage.GetValue<string>(nameof(PortfolioName));
		DefaultProduct = storage.GetValue(
			nameof(DefaultProduct),
			DefaultProduct);
		PollingInterval = storage.GetValue(
			nameof(PollingInterval),
			PollingInterval);
		ReconnectAttempts = storage.GetValue(
			nameof(ReconnectAttempts),
			ReconnectAttempts);
		RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
		MarketDataAddress = storage.GetValue(
			nameof(MarketDataAddress),
			MarketDataAddress);
		OrderStatusAddress = storage.GetValue(
			nameof(OrderStatusAddress),
			OrderStatusAddress);
	}
}
