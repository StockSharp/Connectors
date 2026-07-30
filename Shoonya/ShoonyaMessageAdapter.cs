namespace StockSharp.Shoonya;

/// <summary>The message adapter for Shoonya API.</summary>
[MediaIcon(Media.MediaNames.shoonya)]
[Doc("topics/api/connectors/stock_market/shoonya.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ShoonyaKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions | MessageAdapterCategories.History |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX | MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(ShoonyaOrderCondition))]
public class ShoonyaMessageAdapter : NorenMessageAdapter
{
	private const string _defaultRestEndpoint = "https://api.shoonya.com/NorenWClientTP/";
	private const string _defaultInstrumentEndpointTemplate = "https://api.shoonya.com/{0}_symbols.txt.zip";
	private const string _defaultWebSocketEndpoint = "wss://api.shoonya.com/NorenWSTP/";

	/// <summary>
	/// Initializes a new instance of the <see cref="ShoonyaMessageAdapter"/>.
	/// </summary>
	public ShoonyaMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		RestEndpoint = _defaultRestEndpoint;
		InstrumentEndpointTemplate = _defaultInstrumentEndpointTemplate;
		WebSocketEndpoint = _defaultWebSocketEndpoint;
	}

	/// <summary>Supported candle time frames.</summary>
	public static new IEnumerable<TimeSpan> AllTimeFrames => NorenMessageAdapter.AllTimeFrames;

	/// <summary>Shoonya user identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UserIdKey,
		Description = LocalizedStrings.ShoonyaUserIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public new string UserId
	{
		get => base.UserId;
		set => base.UserId = value;
	}

	/// <summary>Trading account identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.ShoonyaAccountIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public new string AccountId
	{
		get => base.AccountId;
		set => base.AccountId = value;
	}

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.ShoonyaSessionTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public new SecureString Token
	{
		get => base.Token;
		set => base.Token = value;
	}

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ShoonyaDefaultProductKey,
		Description = LocalizedStrings.ShoonyaDefaultProductDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public new ShoonyaProducts DefaultProduct
	{
		get => (ShoonyaProducts)base.DefaultProduct;
		set => base.DefaultProduct = (NorenProducts)value;
	}

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ShoonyaReconnectAttemptsKey,
		Description = LocalizedStrings.ShoonyaReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public new int ReconnectAttempts
	{
		get => base.ReconnectAttempts;
		set => base.ReconnectAttempts = value;
	}

	/// <summary>REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public new string RestEndpoint
	{
		get => base.RestEndpoint;
		set => base.RestEndpoint = value;
	}

	/// <summary>Instrument file endpoint template.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.InstrumentEndpointTemplateKey,
		Description = LocalizedStrings.InstrumentFileEndpointTemplateDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	public new string InstrumentEndpointTemplate
	{
		get => base.InstrumentEndpointTemplate;
		set => base.InstrumentEndpointTemplate = value;
	}

	/// <summary>WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketEndpointKey,
		Description = LocalizedStrings.WebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 7)]
	public new string WebSocketEndpoint
	{
		get => base.WebSocketEndpoint;
		set => base.WebSocketEndpoint = value;
	}

	/// <inheritdoc />
	protected override NorenOrderCondition CreateOrderCondition()
		=> new ShoonyaOrderCondition();
}
