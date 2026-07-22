namespace StockSharp.Marketstack;

/// <summary>The message adapter for the Marketstack REST API v2.</summary>
[MediaIcon(Media.MediaNames.marketstack)]
[Doc("topics/api/connectors/stock_market/marketstack.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MarketstackKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles)]
public partial class MarketstackMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
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

	/// <summary>Marketstack API v2 base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api.marketstack.com/v2/");

	/// <summary>Optional exchange MIC used for lookup and market-data requests.</summary>
	[Display(
		Name = "Stock exchange",
		Description = "Optional exchange MIC, for example XNAS or XNYS.",
		GroupName = "Market data",
		Order = 2)]
	public string StockExchange { get; set; }

	/// <summary>Price adjustment used for historical candles.</summary>
	[Display(
		Name = "Price adjustment",
		Description = "Select raw or corporate-action-adjusted OHLC fields.",
		GroupName = "Market data",
		Order = 3)]
	public MarketstackAdjustments PriceAdjustment { get; set; } =
		MarketstackAdjustments.Adjusted;

	/// <summary>Whether intraday requests include pre-market and post-market data.</summary>
	[Display(
		Name = "After hours",
		Description = "Include pre-market and post-market intraday data when entitled.",
		GroupName = "Market data",
		Order = 4)]
	public bool IsAfterHours { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(StockExchange), StockExchange)
			.Set(nameof(PriceAdjustment), PriceAdjustment)
			.Set(nameof(IsAfterHours), IsAfterHours);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		StockExchange = storage.GetValue<string>(nameof(StockExchange));
		PriceAdjustment = storage.GetValue(nameof(PriceAdjustment), PriceAdjustment);
		IsAfterHours = storage.GetValue(nameof(IsAfterHours), IsAfterHours);
	}
}
