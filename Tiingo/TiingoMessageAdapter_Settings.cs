namespace StockSharp.Tiingo;

/// <summary>The message adapter for Tiingo REST and WebSocket APIs.</summary>
[MediaIcon(Media.MediaNames.tiingo)]
[Doc("topics/api/connectors/stock_market/tiingo.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TiingoKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.FX | MessageAdapterCategories.Crypto |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles | MessageAdapterCategories.News)]
public partial class TiingoMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
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

	/// <summary>Tiingo REST API base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api.tiingo.com/");

	/// <summary>Tiingo IEX WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IexWebSocketAddressKey,
		Description = LocalizedStrings.TiingoUsEquityStreamEndpointDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public Uri IexWebSocketAddress { get; set; } = new("wss://api.tiingo.com/iex");

	/// <summary>Tiingo forex WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ForexWebSocketAddressKey,
		Description = LocalizedStrings.TiingoForexStreamEndpointDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public Uri ForexWebSocketAddress { get; set; } = new("wss://api.tiingo.com/fx");

	/// <summary>Tiingo crypto WebSocket address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CryptoWebSocketAddressKey,
		Description = LocalizedStrings.TiingoCryptoStreamEndpointDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public Uri CryptoWebSocketAddress { get; set; } = new("wss://api.tiingo.com/crypto");

	/// <summary>Official supported daily-tickers CSV address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SupportedTickersAddressKey,
		Description = LocalizedStrings.OfficialTiingoDailySupportedTickersCsvDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public Uri SupportedTickersAddress { get; set; } =
		new("https://apimedia.tiingo.com/docs/tiingo/daily/supported_tickers.csv");

	/// <summary>Equity real-time feed mode.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EquityStreamKey,
		Description = LocalizedStrings.UsEquityWebSocketEntitlementAndFidelityDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 6)]
	[BasicSetting]
	public TiingoEquityStreamingModes EquityStreamingMode { get; set; } =
		TiingoEquityStreamingModes.ReferencePrice;

	/// <summary>Optional crypto exchange used to select a venue-specific quote.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CryptoExchangeKey,
		Description = LocalizedStrings.OptionalVenueFilterEmptyUsesTheTradeOnlyConsolidatedFirehoseDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 7)]
	public string CryptoExchange { get; set; }

	/// <summary>Historical end-of-day adjustment policy.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceAdjustmentKey,
		Description = LocalizedStrings.RawOrSplitAndDividendAdjustedEndOfDayCandlesDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 8)]
	public TiingoPriceAdjustments PriceAdjustment { get; set; } = TiingoPriceAdjustments.Raw;

	/// <summary>Include eligible US extended-hours intraday data.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExtendedHoursKey,
		Description = LocalizedStrings.IncludeEligiblePreMarketAndPostMarketIntradayDataDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 9)]
	public bool IsAfterHours { get; set; }

	/// <summary>Request Tiingo gap filling for intraday equity candles.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ForceFillKey,
		Description = LocalizedStrings.FillMissingIntradayEquityIntervalsDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 10)]
	public bool IsForceFill { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(IexWebSocketAddress), IexWebSocketAddress)
			.Set(nameof(ForexWebSocketAddress), ForexWebSocketAddress)
			.Set(nameof(CryptoWebSocketAddress), CryptoWebSocketAddress)
			.Set(nameof(SupportedTickersAddress), SupportedTickersAddress)
			.Set(nameof(EquityStreamingMode), EquityStreamingMode)
			.Set(nameof(CryptoExchange), CryptoExchange)
			.Set(nameof(PriceAdjustment), PriceAdjustment)
			.Set(nameof(IsAfterHours), IsAfterHours)
			.Set(nameof(IsForceFill), IsForceFill);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		IexWebSocketAddress = storage.GetValue(nameof(IexWebSocketAddress), IexWebSocketAddress);
		ForexWebSocketAddress = storage.GetValue(nameof(ForexWebSocketAddress), ForexWebSocketAddress);
		CryptoWebSocketAddress = storage.GetValue(nameof(CryptoWebSocketAddress), CryptoWebSocketAddress);
		SupportedTickersAddress = storage.GetValue(nameof(SupportedTickersAddress), SupportedTickersAddress);
		EquityStreamingMode = storage.GetValue(nameof(EquityStreamingMode), EquityStreamingMode);
		CryptoExchange = storage.GetValue<string>(nameof(CryptoExchange));
		PriceAdjustment = storage.GetValue(nameof(PriceAdjustment), PriceAdjustment);
		IsAfterHours = storage.GetValue(nameof(IsAfterHours), IsAfterHours);
		IsForceFill = storage.GetValue(nameof(IsForceFill), IsForceFill);
	}
}
