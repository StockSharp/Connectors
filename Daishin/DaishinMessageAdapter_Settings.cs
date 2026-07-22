namespace StockSharp.Daishin;

/// <summary>Stock quote markets exposed by Daishin CYBOS Plus.</summary>
[DataContract]
[Serializable]
public enum DaishinStockMarkets
{
	/// <summary>Consolidated KRX and NXT market.</summary>
	[EnumMember]
	Consolidated,
	/// <summary>Korea Exchange.</summary>
	[EnumMember]
	Krx,
	/// <summary>Nextrade.</summary>
	[EnumMember]
	Nxt,
}

/// <summary>The message adapter for the official Daishin CYBOS Plus COM API.</summary>
[MediaIcon(Media.MediaNames.daishin)]
[Doc("topics/api/connectors/stock_market/daishin.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DaishinKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.KoreaExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
[OrderCondition(typeof(DaishinOrderCondition))]
[SupportedOSPlatform("windows")]
public partial class DaishinMessageAdapter : MessageAdapter
{
	/// <summary>Optional preferred CYBOS Plus account number.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.DaishinAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	public string Account { get; set; }

	/// <summary>Stock quote market.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DaishinMarketKey,
		Description = LocalizedStrings.DaishinMarketDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public DaishinStockMarkets Market { get; set; } = DaishinStockMarkets.Consolidated;

	/// <summary>Whether trading and account services are initialized.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DaishinTradingEnabledKey,
		Description = LocalizedStrings.DaishinTradingEnabledDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public bool IsTradingEnabled { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Account), Account)
			.Set(nameof(Market), Market)
			.Set(nameof(IsTradingEnabled), IsTradingEnabled);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Account = storage.GetValue<string>(nameof(Account));
		Market = storage.GetValue(nameof(Market), Market);
		IsTradingEnabled = storage.GetValue(nameof(IsTradingEnabled), IsTradingEnabled);
	}
}
