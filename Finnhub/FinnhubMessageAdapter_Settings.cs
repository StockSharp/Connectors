namespace StockSharp.Finnhub;

/// <summary>The message adapter for Finnhub REST and WebSocket APIs.</summary>
[MediaIcon(Media.MediaNames.finnhub)]
[Doc("topics/api/connectors/stock_market/finnhub.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FinnhubKey,
	Description = LocalizedStrings.MarketDataConnectorKey, GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.FX | MessageAdapterCategories.Crypto |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles | MessageAdapterCategories.News)]
public partial class FinnhubMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Finnhub REST API base address.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://finnhub.io/api/v1/");

	/// <summary>Finnhub WebSocket address.</summary>
	[Display(Name = "WebSocket address", Description = "Finnhub real-time WebSocket address.",
		GroupName = "Connection", Order = 2)]
	[BasicSetting]
	public Uri WebSocketAddress { get; set; } = new("wss://ws.finnhub.io/");

	/// <summary>Default stock exchange code used for security lookup.</summary>
	[Display(Name = "Stock exchange", Description = "Finnhub stock exchange code used for lookup.",
		GroupName = "Market data", Order = 3)]
	[BasicSetting]
	public string StockExchange { get; set; } = "US";

	/// <summary>Optional MIC filter used for stock security lookup.</summary>
	[Display(Name = "Stock MIC", Description = "Optional market identifier code used for stock lookup.",
		GroupName = "Market data", Order = 4)]
	public string StockMic { get; set; }

	/// <summary>Default forex source used for security lookup.</summary>
	[Display(Name = "Forex exchange", Description = "Finnhub forex source used for lookup.",
		GroupName = "Market data", Order = 5)]
	[BasicSetting]
	public string ForexExchange { get; set; } = "OANDA";

	/// <summary>Default crypto exchange used for security lookup.</summary>
	[Display(Name = "Crypto exchange", Description = "Finnhub crypto exchange used for lookup.",
		GroupName = "Market data", Order = 6)]
	[BasicSetting]
	public string CryptoExchange { get; set; } = "BINANCE";

	/// <summary>Market-news category used when a news subscription has no security.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CategoryKey,
		Description = LocalizedStrings.NewsKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 7)]
	public FinnhubNewsCategories NewsCategory { get; set; } = FinnhubNewsCategories.General;

	/// <summary>Request the separately entitled US stock bid/ask snapshot endpoint.</summary>
	[Display(Name = "Bid/ask snapshots", Description = "Request Finnhub's premium US bid/ask endpoint.",
		GroupName = "Market data", Order = 8)]
	public bool IsBidAskEnabled { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(WebSocketAddress), WebSocketAddress)
			.Set(nameof(StockExchange), StockExchange)
			.Set(nameof(StockMic), StockMic)
			.Set(nameof(ForexExchange), ForexExchange)
			.Set(nameof(CryptoExchange), CryptoExchange)
			.Set(nameof(NewsCategory), NewsCategory)
			.Set(nameof(IsBidAskEnabled), IsBidAskEnabled);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		WebSocketAddress = storage.GetValue(nameof(WebSocketAddress), WebSocketAddress);
		StockExchange = storage.GetValue(nameof(StockExchange), StockExchange);
		StockMic = storage.GetValue<string>(nameof(StockMic));
		ForexExchange = storage.GetValue(nameof(ForexExchange), ForexExchange);
		CryptoExchange = storage.GetValue(nameof(CryptoExchange), CryptoExchange);
		NewsCategory = storage.GetValue(nameof(NewsCategory), NewsCategory);
		IsBidAskEnabled = storage.GetValue(nameof(IsBidAskEnabled), IsBidAskEnabled);
	}
}
