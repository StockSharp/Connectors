namespace StockSharp.HdfcSecurities;

/// <summary>The message adapter for HDFC Securities InvestRight Open API.</summary>
[MediaIcon(Media.MediaNames.hdfcsecurities)]
[Doc("topics/api/connectors/stock_market/hdfc_securities.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.HdfcSecuritiesKey,
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
	MessageAdapterCategories.Options |
	MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(HdfcOrderCondition))]
public partial class HdfcMessageAdapter : MessageAdapter,
	IKeySecretAdapter, ITokenAdapter
{
	private static readonly Uri _defaultRestAddress =
		new("https://developer.hdfcsec.com/oapi/");
	private static readonly Uri _defaultInstrumentAddress =
		new("https://developer.hdfcsec.com/oapi/v1/security-master");
	private static readonly Uri _defaultWebSocketAddress =
		new("wss://developer.hdfcsec.com/wsapi/v1/session");

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ApiKeyKey,
		Description = LocalizedStrings.ApplicationKeyCreatedInTheInvestRightDeveloperPortalDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ApiSecretKey,
		Description = LocalizedStrings.ApplicationSecretUsedToExchangeARequestTokenForAnAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>One-time request token returned by the authorization redirect.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RequestTokenKey,
		Description = LocalizedStrings.OneTimeRequestTokenReturnedAfterInvestRightLoginAndConsentDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString RequestToken { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccessTokenKey,
		Description = LocalizedStrings.InvestRightAccessTokenItIsPopulatedAfterRequestTokenExchangeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public SecureString Token { get; set; }

	/// <summary>Portfolio name emitted by the connector.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PortfolioNameLabelKey,
		Description = LocalizedStrings.PortfolioNameWhenEmptyTheInvestRightUserIdIsUsedDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string PortfolioName { get; set; }

	/// <summary>Default product used for new orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DefaultProductKey,
		Description = LocalizedStrings.DefaultHdfcSecuritiesProductUsedForNewOrdersDescKey,
		GroupName = LocalizedStrings.OrderKey,
		Order = 5)]
	public HdfcProducts DefaultProduct { get; set; } =
		HdfcProducts.Delivery;

	/// <summary>Interval for order and portfolio snapshots.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public TimeSpan PollingInterval { get; set; } =
		TimeSpan.FromSeconds(15);

	/// <summary>Maximum number of WebSocket reconnect attempts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ReconnectAttemptsLabelKey,
		Description = LocalizedStrings.MaximumNumberOfAttemptsToReconnectTheInvestRightWebSocketDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <summary>REST API root address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestAddressKey,
		Description = LocalizedStrings.InvestRightOpenApiRootAddressDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 8)]
	public Uri RestAddress { get; set; } = _defaultRestAddress;

	/// <summary>Public security-master CSV address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.InstrumentAddressKey,
		Description = LocalizedStrings.PublicInvestRightSecurityMasterCsvAddressDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 9)]
	public Uri InstrumentAddress { get; set; } =
		_defaultInstrumentAddress;

	/// <summary>Market-data WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketAddressKey,
		Description = LocalizedStrings.InvestRightProtobufMarketDataWebSocketAddressDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 10)]
	public Uri WebSocketAddress { get; set; } =
		_defaultWebSocketAddress;

	/// <summary>Create the InvestRight authorization-page URL.</summary>
	public Uri CreateAuthorizationUri()
	{
		var apiKey = Key.ThrowIfEmpty(nameof(Key)).UnSecure();
		var root = RestAddress ??
			throw new InvalidOperationException(
				"HDFC Securities REST address is not configured.");
		return new(
			root,
			$"v1/login?api_key={Uri.EscapeDataString(apiKey)}");
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
			.Set(nameof(PortfolioName), PortfolioName)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts)
			.Set(nameof(RestAddress), RestAddress)
			.Set(nameof(InstrumentAddress), InstrumentAddress)
			.Set(nameof(WebSocketAddress), WebSocketAddress);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		RequestToken = storage.GetValue<SecureString>(nameof(RequestToken));
		Token = storage.GetValue<SecureString>(nameof(Token));
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
		InstrumentAddress = storage.GetValue(
			nameof(InstrumentAddress),
			InstrumentAddress);
		WebSocketAddress = storage.GetValue(
			nameof(WebSocketAddress),
			WebSocketAddress);
	}
}
