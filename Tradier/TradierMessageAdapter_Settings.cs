namespace StockSharp.Tradier;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for <see cref="Tradier"/>.
/// </summary>
[MediaIcon(Media.MediaNames.tradier)]
[Doc("topics/api/connectors/stock_market/tradier.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TradierKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
public partial class TradierMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	private const string _defaultRestEndpoint = "https://api.tradier.com";
	private const string _defaultDemoRestEndpoint = "https://sandbox.tradier.com";
	private const string _defaultMarketWebSocketEndpoint = "wss://ws.tradier.com/v1/markets/events";
	private const string _defaultDemoMarketWebSocketEndpoint = "wss://ws.tradier.com/v1/markets/events";
	private const string _defaultAccountWebSocketEndpoint = "wss://ws.tradier.com/v1/accounts/events";
	private const string _defaultDemoAccountWebSocketEndpoint = "wss://sandbox-ws.tradier.com/v1/accounts/events";

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. Native.Extensions.TimeFrames.Keys];

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Production REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.ProductionRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Demo REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoRestEndpointKey,
		Description = LocalizedStrings.DemoRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string DemoRestEndpoint { get; set; } = _defaultDemoRestEndpoint;

	/// <summary>Production market WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketWebSocketEndpointKey,
		Description = LocalizedStrings.ProductionMarketWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string MarketWebSocketEndpoint { get; set; } = _defaultMarketWebSocketEndpoint;

	/// <summary>Demo market WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoMarketWebSocketEndpointKey,
		Description = LocalizedStrings.DemoMarketWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string DemoMarketWebSocketEndpoint { get; set; } = _defaultDemoMarketWebSocketEndpoint;

	/// <summary>Production account WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountWebSocketEndpointKey,
		Description = LocalizedStrings.ProductionAccountWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	public string AccountWebSocketEndpoint { get; set; } = _defaultAccountWebSocketEndpoint;

	/// <summary>Demo account WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoAccountWebSocketEndpointKey,
		Description = LocalizedStrings.DemoAccountWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 7)]
	public string DemoAccountWebSocketEndpoint { get; set; } = _defaultDemoAccountWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Token), Token)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(DemoRestEndpoint), DemoRestEndpoint)
			.Set(nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint)
			.Set(nameof(DemoMarketWebSocketEndpoint), DemoMarketWebSocketEndpoint)
			.Set(nameof(AccountWebSocketEndpoint), AccountWebSocketEndpoint)
			.Set(nameof(DemoAccountWebSocketEndpoint), DemoAccountWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		Token = storage.GetValue<SecureString>(nameof(Token));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		DemoRestEndpoint = storage.GetValue(nameof(DemoRestEndpoint), DemoRestEndpoint);
		MarketWebSocketEndpoint = storage.GetValue(nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint);
		DemoMarketWebSocketEndpoint = storage.GetValue(nameof(DemoMarketWebSocketEndpoint), DemoMarketWebSocketEndpoint);
		AccountWebSocketEndpoint = storage.GetValue(nameof(AccountWebSocketEndpoint), AccountWebSocketEndpoint);
		DemoAccountWebSocketEndpoint = storage.GetValue(nameof(DemoAccountWebSocketEndpoint), DemoAccountWebSocketEndpoint);
	}
}
