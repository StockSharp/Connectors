namespace StockSharp.Flattrade;

/// <summary>The message adapter for Flattrade API.</summary>
[MediaIcon(Media.MediaNames.flattrade)]
[Doc("topics/api/connectors/stock_market/flattrade.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FlattradeKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions | MessageAdapterCategories.History |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX | MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(FlattradeOrderCondition))]
public partial class FlattradeMessageAdapter : MessageAdapter, ITokenAdapter
{
	/// <summary>Flattrade user identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UserIdKey,
		Description = LocalizedStrings.FlattradeUserIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string UserId { get; set; }

	/// <summary>Trading account identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.FlattradeAccountIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.FlattradeSessionTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FlattradeDefaultProductKey,
		Description = LocalizedStrings.FlattradeDefaultProductDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public FlattradeProducts DefaultProduct { get; set; } = FlattradeProducts.Delivery;

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FlattradeReconnectAttemptsKey,
		Description = LocalizedStrings.FlattradeReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(UserId), UserId)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(Token), Token)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		UserId = storage.GetValue<string>(nameof(UserId));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		Token = storage.GetValue<SecureString>(nameof(Token));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
	}
}
