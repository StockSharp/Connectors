namespace StockSharp.Bcs;

/// <summary>
/// The message adapter for BCS Trade API.
/// </summary>
[MediaIcon(Media.MediaNames.bcs)]
[Doc("topics/api/connectors/russia/bcs.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BcsKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Russia | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX | MessageAdapterCategories.Commodities |
	MessageAdapterCategories.Free)]
public partial class BcsMessageAdapter : MessageAdapter, ITokenAdapter
{
	private const string _defaultRestEndpoint = "https://be.broker.ru";
	private const string _defaultWebSocketEndpoint =
		"wss://ws.broker.ru/trade-api-market-data-connector/api/v1/market-data/ws";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RefreshTokenKey,
		Description = LocalizedStrings.RefreshTokenIssuedInTheBcsAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// Whether the token has read-only permissions.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ReadOnlyTokenKey,
		Description = LocalizedStrings.UseTheTradeApiReadOAuthClientInsteadOfTradeApiWriteDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public bool IsReadOnly { get; set; }

	/// <summary>
	/// Fallback portfolio name used when an empty portfolio is returned.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PortfolioNameKey,
		Description = LocalizedStrings.OrderPortfolioNameKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string PortfolioName { get; set; } = "BCS";

	/// <summary>
	/// Interval for polling orders and portfolio data.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(3);

	/// <summary>
	/// REST API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.BcsRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>
	/// Market-data WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketEndpointKey,
		Description = LocalizedStrings.BcsMarketDataWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(IsReadOnly), IsReadOnly)
			.Set(nameof(PortfolioName), PortfolioName)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		IsReadOnly = storage.GetValue<bool>(nameof(IsReadOnly));
		PortfolioName = storage.GetValue(nameof(PortfolioName), PortfolioName);
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}
}
