namespace StockSharp.TwelveData;

/// <summary>The message adapter for Twelve Data REST and WebSocket APIs.</summary>
[MediaIcon(Media.MediaNames.twelvedata)]
[Doc("topics/api/connectors/stock_market/twelvedata.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TwelveDataKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.FX | MessageAdapterCategories.Crypto |
	MessageAdapterCategories.Commodities | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles)]
public partial class TwelveDataMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
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

	/// <summary>Twelve Data REST API base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api.twelvedata.com/");

	/// <summary>Twelve Data real-time WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketAddressKey,
		Description = LocalizedStrings.TwelveDataRealTimePriceEndpointDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public Uri WebSocketAddress { get; set; } =
		new("wss://ws.twelvedata.com/v1/quotes/price");

	/// <summary>Country used to narrow stock and ETF lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountryKey,
		Description = LocalizedStrings.CountryFilterForStockAndEtfReferenceDataDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 3)]
	[BasicSetting]
	public string Country { get; set; } = "United States";

	/// <summary>Optional exchange used to narrow stock and ETF lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StockExchangeKey,
		Description = LocalizedStrings.OptionalStockAndEtfExchangeFilterDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 4)]
	public string StockExchange { get; set; }

	/// <summary>Optional MIC used to narrow stock and ETF lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StockMicKey,
		Description = LocalizedStrings.OptionalMarketIdentifierCodeForStockAndEtfLookupDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 5)]
	public string StockMic { get; set; }

	/// <summary>Optional crypto exchange used for lookup and ambiguous symbols.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CryptoExchangeKey,
		Description = LocalizedStrings.OptionalCryptocurrencyExchangeFilterDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 6)]
	public string CryptoExchange { get; set; }

	/// <summary>Historical-price adjustment policy.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AdjustmentKey,
		Description = LocalizedStrings.SplitAndDividendAdjustmentForHistoricalCandlesDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 7)]
	public TwelveDataAdjustments Adjustment { get; set; } = TwelveDataAdjustments.Splits;

	/// <summary>Include eligible US pre-market and post-market data.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExtendedHoursKey,
		Description = LocalizedStrings.IncludeEligibleUsPreMarketAndPostMarketDataDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 8)]
	public bool IsPrePost { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(WebSocketAddress), WebSocketAddress)
			.Set(nameof(Country), Country)
			.Set(nameof(StockExchange), StockExchange)
			.Set(nameof(StockMic), StockMic)
			.Set(nameof(CryptoExchange), CryptoExchange)
			.Set(nameof(Adjustment), Adjustment)
			.Set(nameof(IsPrePost), IsPrePost);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		WebSocketAddress = storage.GetValue(nameof(WebSocketAddress), WebSocketAddress);
		Country = storage.GetValue(nameof(Country), Country);
		StockExchange = storage.GetValue<string>(nameof(StockExchange));
		StockMic = storage.GetValue<string>(nameof(StockMic));
		CryptoExchange = storage.GetValue<string>(nameof(CryptoExchange));
		Adjustment = storage.GetValue(nameof(Adjustment), Adjustment);
		IsPrePost = storage.GetValue(nameof(IsPrePost), IsPrePost);
	}
}
