namespace StockSharp.Orats;

/// <summary>The message adapter for the ORATS Data API.</summary>
[MediaIcon(Media.MediaNames.orats)]
[Doc("topics/api/connectors/stock_market/orats.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OratsKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Paid |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Options |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Candles)]
public partial class OratsMessageAdapter : MessageAdapter, ITokenAdapter
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>ORATS Data API base address.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api.orats.io/datav2/");

	/// <summary>Current-data entitlement to use.</summary>
	[Display(Name = "Data mode",
		Description = "Use delayed or agreement-gated live current-data endpoints.",
		GroupName = "Market data", Order = 2)]
	[BasicSetting]
	public OratsDataModes DataMode { get; set; }

	/// <summary>Price adjustment used for historical stock candles.</summary>
	[Display(Name = "Price adjustment",
		Description = "Select adjusted or unadjusted daily stock OHLC fields.",
		GroupName = "Market data", Order = 3)]
	public OratsPriceAdjustments PriceAdjustment { get; set; } =
		OratsPriceAdjustments.Adjusted;

	/// <summary>Time zone used to place ORATS trade dates into market sessions.</summary>
	[Display(Name = "Market time zone",
		Description = "System time-zone identifier for US Eastern market dates.",
		GroupName = "Market data", Order = 4)]
	public string MarketTimeZoneId { get; set; } = "America/New_York";

	/// <summary>Start of the daily stock candle session.</summary>
	[Display(Name = "Session start",
		Description = "Open time assigned to daily stock candles.",
		GroupName = "Market data", Order = 5)]
	public TimeSpan SessionStart { get; set; } = new(9, 30, 0);

	/// <summary>End of the daily stock candle session.</summary>
	[Display(Name = "Session end",
		Description = "Close time assigned to daily stock candles and EOD observations.",
		GroupName = "Market data", Order = 6)]
	public TimeSpan SessionEnd { get; set; } = new(16, 0, 0);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(DataMode), DataMode)
			.Set(nameof(PriceAdjustment), PriceAdjustment)
			.Set(nameof(MarketTimeZoneId), MarketTimeZoneId)
			.Set(nameof(SessionStart), SessionStart)
			.Set(nameof(SessionEnd), SessionEnd);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		DataMode = storage.GetValue(nameof(DataMode), DataMode);
		PriceAdjustment = storage.GetValue(nameof(PriceAdjustment), PriceAdjustment);
		MarketTimeZoneId = storage.GetValue(nameof(MarketTimeZoneId), MarketTimeZoneId);
		SessionStart = storage.GetValue(nameof(SessionStart), SessionStart);
		SessionEnd = storage.GetValue(nameof(SessionEnd), SessionEnd);
	}
}
