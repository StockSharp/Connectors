namespace StockSharp.EodHistoricalData;

/// <summary>The message adapter for EOD Historical Data REST and WebSocket APIs.</summary>
[MediaIcon(Media.MediaNames.eodhd)]
[Doc("topics/api/connectors/stock_market/eodhd.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.EodHistoricalDataKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.FX | MessageAdapterCategories.Crypto |
	MessageAdapterCategories.Options | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.News)]
public partial class EodhdMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
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

	/// <summary>EODHD REST API base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://eodhd.com/api/");

	/// <summary>EODHD US trade WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UsTradeWebSocketKey,
		Description = LocalizedStrings.EodhdUsTradeStreamEndpointDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public Uri StockTradeWebSocketAddress { get; set; } =
		new("wss://ws.eodhistoricaldata.com/ws/us");

	/// <summary>EODHD US quote WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UsQuoteWebSocketKey,
		Description = LocalizedStrings.EodhdUsQuoteStreamEndpointDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public Uri StockQuoteWebSocketAddress { get; set; } =
		new("wss://ws.eodhistoricaldata.com/ws/us-quote");

	/// <summary>EODHD forex WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ForexWebSocketKey,
		Description = LocalizedStrings.EodhdForexQuoteStreamEndpointDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public Uri ForexWebSocketAddress { get; set; } =
		new("wss://ws.eodhistoricaldata.com/ws/forex");

	/// <summary>EODHD crypto WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CryptoWebSocketKey,
		Description = LocalizedStrings.EodhdCryptoTradeStreamEndpointDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public Uri CryptoWebSocketAddress { get; set; } =
		new("wss://ws.eodhistoricaldata.com/ws/crypto");

	/// <summary>Exchange used for unqualified stock identifiers and full stock lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StockExchangeKey,
		Description = LocalizedStrings.EodhdExchangeCodeUsedForUnqualifiedStocksAndFullLookupDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 6)]
	[BasicSetting]
	public string StockExchange { get; set; } = "US";

	/// <summary>Include delisted instruments in exchange symbol lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DelistedSecuritiesKey,
		Description = LocalizedStrings.IncludeDelistedInstrumentsInTheExchangeSymbolListDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 7)]
	public bool IsDelisted { get; set; }

	/// <summary>Maximum number of symbols per EODHD WebSocket product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketSymbolLimitKey,
		Description = LocalizedStrings.ConfiguredSimultaneousSymbolAllowanceForEachWebSocketProductDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 8)]
	public int MaxWebSocketSymbols { get; set; } = 50;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(StockTradeWebSocketAddress), StockTradeWebSocketAddress)
			.Set(nameof(StockQuoteWebSocketAddress), StockQuoteWebSocketAddress)
			.Set(nameof(ForexWebSocketAddress), ForexWebSocketAddress)
			.Set(nameof(CryptoWebSocketAddress), CryptoWebSocketAddress)
			.Set(nameof(StockExchange), StockExchange)
			.Set(nameof(IsDelisted), IsDelisted)
			.Set(nameof(MaxWebSocketSymbols), MaxWebSocketSymbols);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		StockTradeWebSocketAddress = storage.GetValue(nameof(StockTradeWebSocketAddress),
			StockTradeWebSocketAddress);
		StockQuoteWebSocketAddress = storage.GetValue(nameof(StockQuoteWebSocketAddress),
			StockQuoteWebSocketAddress);
		ForexWebSocketAddress = storage.GetValue(nameof(ForexWebSocketAddress),
			ForexWebSocketAddress);
		CryptoWebSocketAddress = storage.GetValue(nameof(CryptoWebSocketAddress),
			CryptoWebSocketAddress);
		StockExchange = storage.GetValue(nameof(StockExchange), StockExchange);
		IsDelisted = storage.GetValue(nameof(IsDelisted), IsDelisted);
		MaxWebSocketSymbols = storage.GetValue(nameof(MaxWebSocketSymbols), MaxWebSocketSymbols);
	}
}
