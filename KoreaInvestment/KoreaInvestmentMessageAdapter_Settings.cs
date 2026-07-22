namespace StockSharp.KoreaInvestment;

/// <summary>The message adapter for Korea Investment &amp; Securities Open API.</summary>
[MediaIcon(Media.MediaNames.koreainvestment)]
[Doc("topics/api/connectors/stock_market/korea_investment.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KoreaInvestmentKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.KoreaExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
[OrderCondition(typeof(KoreaInvestmentOrderCondition))]
public partial class KoreaInvestmentMessageAdapter : MessageAdapter, IDemoAdapter, IKeySecretAdapter
{
	/// <summary>Application key issued by KIS Developers.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KoreaInvestmentAppKeyDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Application secret issued by KIS Developers.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.KoreaInvestmentAppSecretDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>First eight digits of the KIS account number.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KoreaInvestmentAccountKey,
		Description = LocalizedStrings.KoreaInvestmentAccountDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public string AccountNumber { get; set; }

	/// <summary>Two-digit account product code.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KoreaInvestmentProductCodeKey,
		Description = LocalizedStrings.KoreaInvestmentProductCodeDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public string ProductCode { get; set; } = "01";

	/// <summary>KIS Developers HTS ID used by execution-notice WebSocket channels.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KoreaInvestmentHtsIdKey,
		Description = LocalizedStrings.KoreaInvestmentHtsIdDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public string HtsId { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey, GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(AccountNumber), AccountNumber)
			.Set(nameof(ProductCode), ProductCode)
			.Set(nameof(HtsId), HtsId)
			.Set(nameof(IsDemo), IsDemo);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		AccountNumber = storage.GetValue<string>(nameof(AccountNumber));
		ProductCode = storage.GetValue(nameof(ProductCode), ProductCode);
		HtsId = storage.GetValue<string>(nameof(HtsId));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
	}
}
