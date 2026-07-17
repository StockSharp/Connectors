namespace StockSharp.FivePaisa;

/// <summary>The message adapter for the 5paisa Xstream API.</summary>
[MediaIcon(Media.MediaNames.fivepaisa)]
[Doc("topics/api/connectors/stock_market/fivepaisa.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FivePaisaKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.History | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX)]
[OrderCondition(typeof(FivePaisaOrderCondition))]
public partial class FivePaisaMessageAdapter : MessageAdapter, ITokenAdapter
{
	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	/// <summary>Possible time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Application key issued by 5paisa.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FivePaisaAppKeyKey,
		Description = LocalizedStrings.FivePaisaAppKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string AppKey { get; set; }

	/// <summary>5paisa demat account client code.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClientCodeKey,
		Description = LocalizedStrings.FivePaisaClientCodeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string ClientCode { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FivePaisaDefaultProductKey,
		Description = LocalizedStrings.FivePaisaDefaultProductDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public FivePaisaProducts DefaultProduct { get; set; } = FivePaisaProducts.Intraday;

	/// <summary>Algorithm identifier registered with the exchange.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FivePaisaAlgoIdKey,
		Description = LocalizedStrings.FivePaisaAlgoIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public long AlgoId { get; set; }

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FivePaisaReconnectAttemptsKey,
		Description = LocalizedStrings.FivePaisaReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AppKey), AppKey)
			.Set(nameof(ClientCode), ClientCode)
			.Set(nameof(Token), Token)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(AlgoId), AlgoId)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AppKey = storage.GetValue<string>(nameof(AppKey));
		ClientCode = storage.GetValue<string>(nameof(ClientCode));
		Token = storage.GetValue<SecureString>(nameof(Token));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
		AlgoId = storage.GetValue(nameof(AlgoId), AlgoId);
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
	}
}
