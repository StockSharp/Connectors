namespace StockSharp.Fugle;

/// <summary>The message adapter for Fugle Market Data API v1.0.</summary>
[MediaIcon(Media.MediaNames.fugle)]
[Doc("topics/api/connectors/stock_market/fugle.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FugleKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.TaiwanStockExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.History | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
public partial class FugleMessageAdapter : MessageAdapter, ITokenAdapter
{
	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.FugleApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FugleReconnectAttemptsKey,
		Description = LocalizedStrings.FugleReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
	}
}
