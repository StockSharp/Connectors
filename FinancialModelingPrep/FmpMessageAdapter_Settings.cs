namespace StockSharp.FinancialModelingPrep;

/// <summary>The message adapter for Financial Modeling Prep REST and WebSocket APIs.</summary>
[MediaIcon(Media.MediaNames.fmp)]
[Doc("topics/api/connectors/stock_market/fmp.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FinancialModelingPrepKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.FX | MessageAdapterCategories.Crypto |
	MessageAdapterCategories.Commodities | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.News)]
public partial class FmpMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
{
	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>FMP stable REST API base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://financialmodelingprep.com/stable/");

	/// <summary>FMP US equity WebSocket address.</summary>
	[Display(
		Name = "Stock WebSocket",
		Description = "FMP US equity realtime endpoint.",
		GroupName = "Connection",
		Order = 2)]
	[BasicSetting]
	public Uri StockWebSocketAddress { get; set; } =
		new("wss://websockets.financialmodelingprep.com");

	/// <summary>FMP forex WebSocket address.</summary>
	[Display(
		Name = "Forex WebSocket",
		Description = "FMP forex realtime endpoint.",
		GroupName = "Connection",
		Order = 3)]
	[BasicSetting]
	public Uri ForexWebSocketAddress { get; set; } =
		new("wss://forex.financialmodelingprep.com");

	/// <summary>FMP crypto WebSocket address.</summary>
	[Display(
		Name = "Crypto WebSocket",
		Description = "FMP crypto realtime endpoint.",
		GroupName = "Connection",
		Order = 4)]
	[BasicSetting]
	public Uri CryptoWebSocketAddress { get; set; } =
		new("wss://crypto.financialmodelingprep.com");

	/// <summary>Optional exchange filter and qualifier for stock symbols.</summary>
	[Display(
		Name = "Stock exchange",
		Description = "Optional FMP exchange code used for stock lookup and manual identifiers.",
		GroupName = "Market data",
		Order = 5)]
	public string StockExchange { get; set; }

	/// <summary>End-of-day stock price adjustment.</summary>
	[Display(
		Name = "EOD adjustment",
		Description = "Price adjustment used by the FMP daily stock history endpoint.",
		GroupName = "Market data",
		Order = 6)]
	public FmpEodAdjustments EodAdjustment { get; set; }

	/// <summary>Time zone used to interpret offset-free intraday timestamps.</summary>
	[Display(
		Name = "Intraday time zone",
		Description = "System time-zone identifier used for FMP intraday date strings.",
		GroupName = "Market data",
		Order = 7)]
	public string IntradayTimeZoneId { get; set; } = TimeZoneInfo.Utc.Id;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(StockWebSocketAddress), StockWebSocketAddress)
			.Set(nameof(ForexWebSocketAddress), ForexWebSocketAddress)
			.Set(nameof(CryptoWebSocketAddress), CryptoWebSocketAddress)
			.Set(nameof(StockExchange), StockExchange)
			.Set(nameof(EodAdjustment), EodAdjustment)
			.Set(nameof(IntradayTimeZoneId), IntradayTimeZoneId);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		StockWebSocketAddress = storage.GetValue(nameof(StockWebSocketAddress),
			StockWebSocketAddress);
		ForexWebSocketAddress = storage.GetValue(nameof(ForexWebSocketAddress),
			ForexWebSocketAddress);
		CryptoWebSocketAddress = storage.GetValue(nameof(CryptoWebSocketAddress),
			CryptoWebSocketAddress);
		StockExchange = storage.GetValue<string>(nameof(StockExchange));
		EodAdjustment = storage.GetValue(nameof(EodAdjustment), EodAdjustment);
		IntradayTimeZoneId = storage.GetValue(nameof(IntradayTimeZoneId),
			IntradayTimeZoneId);
	}
}
