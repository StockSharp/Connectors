namespace StockSharp.Alor;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for <see cref="Alor"/>.
/// </summary>
[MediaIcon(Media.MediaNames.alor)]
[Doc("topics/api/connectors/russia/alor.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AlorKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Russia | MessageAdapterCategories.Transactions | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Free | MessageAdapterCategories.Ticks)]
[OrderCondition(typeof(AlorOrderCondition))]
public partial class AlorMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	private const string _defaultRestEndpoint = "https://api.alor.ru";
	private const string _defaultDemoRestEndpoint = "https://apidev.alor.ru";
	private const string _defaultWebSocketEndpoint = "wss://api.alor.ru";
	private const string _defaultDemoWebSocketEndpoint = "wss://apidev.alor.ru";
	private const string _defaultOAuthEndpoint = "https://oauth.alor.ru/refresh";
	private const string _defaultDemoOAuthEndpoint = "https://oauthdev.alor.ru/refresh";

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. Native.Extensions.TimeFrames.Keys];

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	private string[] _exchanges = ["MOEX", "SPBX"];

	/// <summary>
	/// Exchanges.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExchangeKey,
		Description = LocalizedStrings.ExchangeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public string Exchanges
	{
		get => _exchanges.JoinComma();
		set => _exchanges = value.ThrowIfEmpty(nameof(value)).SplitByComma(true);
	}

	/// <summary>Production REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "Production REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Demo REST API endpoint.</summary>
	[Display(
		Name = "Demo REST endpoint",
		Description = "Demo REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string DemoRestEndpoint { get; set; } = _defaultDemoRestEndpoint;

	/// <summary>Production WebSocket endpoint.</summary>
	[Display(
		Name = "WebSocket endpoint",
		Description = "Production WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <summary>Demo WebSocket endpoint.</summary>
	[Display(
		Name = "Demo WebSocket endpoint",
		Description = "Demo WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string DemoWebSocketEndpoint { get; set; } = _defaultDemoWebSocketEndpoint;

	/// <summary>Production OAuth refresh endpoint.</summary>
	[Display(
		Name = "OAuth endpoint",
		Description = "Production OAuth refresh endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string OAuthEndpoint { get; set; } = _defaultOAuthEndpoint;

	/// <summary>Demo OAuth refresh endpoint.</summary>
	[Display(
		Name = "Demo OAuth endpoint",
		Description = "Demo OAuth refresh endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string DemoOAuthEndpoint { get; set; } = _defaultDemoOAuthEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Token), Token)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(Exchanges), Exchanges)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(DemoRestEndpoint), DemoRestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(DemoWebSocketEndpoint), DemoWebSocketEndpoint)
			.Set(nameof(OAuthEndpoint), OAuthEndpoint)
			.Set(nameof(DemoOAuthEndpoint), DemoOAuthEndpoint)
			;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		Token = storage.GetValue<SecureString>(nameof(Token));
		Exchanges = storage.GetValue(nameof(Exchanges), Exchanges);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		DemoRestEndpoint = storage.GetValue(nameof(DemoRestEndpoint), DemoRestEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
		DemoWebSocketEndpoint = storage.GetValue(nameof(DemoWebSocketEndpoint), DemoWebSocketEndpoint);
		OAuthEndpoint = storage.GetValue(nameof(OAuthEndpoint), OAuthEndpoint);
		DemoOAuthEndpoint = storage.GetValue(nameof(DemoOAuthEndpoint), DemoOAuthEndpoint);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Demo + " = " + IsDemo;
	}
}
