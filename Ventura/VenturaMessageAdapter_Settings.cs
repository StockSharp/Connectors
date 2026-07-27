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
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppKeyKey,
		Description = LocalizedStrings.ApplicationKeyCreatedInTheVenturaEaseAPIPortalDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppSecretKey,
		Description = LocalizedStrings.ApplicationSecretUsedToCreateTheEaseAPIAuthorizationHashDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>One-time request token returned by the browser login.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RequestTokenKey,
		Description = LocalizedStrings.OneTimeRequestTokenReturnedByTheEaseAPIAuthorizationRedirectDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public SecureString RequestToken { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AuthTokenKey,
		Description = LocalizedStrings.EaseAPIBearerTokenItIsPopulatedAfterRequestTokenOrTotpLoginDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Refresh token returned by EaseAPI authorization.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RefreshTokenKey,
		Description = LocalizedStrings.RefreshTokenReturnedByEaseAPIAuthorizationDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public SecureString RefreshToken { get; set; }

	/// <summary>Ventura client ID.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClientIdKey,
		Description = LocalizedStrings.VenturaTradingClientIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string ClientId { get; set; }

	/// <summary>Ventura account PIN for automated TOTP login.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PinLabelKey,
		Description = LocalizedStrings.VenturaAccountPinUsedOnlyWhenTheAuthTokenAndRequestTokenAreEmptyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public SecureString Pin { get; set; }

	/// <summary>Base32 secret for automated TOTP login.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TotpSecretKey,
		Description = LocalizedStrings.Base32AuthenticatorSecretUsedToGenerateACurrentTotpCodeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public SecureString TotpSecret { get; set; }

	/// <summary>MAC address registered for TOTP authorization.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MacAddressKey,
		Description = LocalizedStrings.MacAddressSentInTheXMacAddressHeaderDuringTotpLoginDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public string MacAddress { get; set; }

	/// <summary>Portfolio name emitted by the connector.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PortfolioNameLabelKey,
		Description = LocalizedStrings.PortfolioNameWhenEmptyTheVenturaClientIdIsUsedDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public string PortfolioName { get; set; }

	/// <summary>Default product used for new orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DefaultProductKey,
		Description = LocalizedStrings.DefaultVenturaProductUsedForNewOrdersDescKey,
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
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ReconnectAttemptsLabelKey,
		Description = LocalizedStrings.MaximumNumberOfEaseAPIWebSocketReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 12)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <summary>REST API root address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestAddressKey,
		Description = LocalizedStrings.VenturaEaseAPIRestRootAddressDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 13)]
	public Uri RestAddress { get; set; } = _defaultRestAddress;

	/// <summary>Market-data WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDataAddressKey,
		Description = LocalizedStrings.VenturaEaseAPIMarketDataWebSocketAddressDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 14)]
	public Uri MarketDataAddress { get; set; } =
		_defaultMarketDataAddress;

	/// <summary>Order-status WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderStatusAddressKey,
		Description = LocalizedStrings.VenturaEaseAPIOrderStatusWebSocketAddressDescKey,
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
