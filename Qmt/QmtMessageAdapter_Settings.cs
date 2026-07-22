namespace StockSharp.Qmt;

/// <summary>The message adapter for QMT, MiniQMT and the official XtQuant API.</summary>
[MediaIcon(Media.MediaNames.qmt)]
[Doc("topics/api/connectors/stock_market/qmt.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QmtKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.ChinaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock)]
public partial class QmtMessageAdapter : MessageAdapter
{
	/// <summary>Host of the separately started XtQuant gateway.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.QmtGatewayHostKey,
		Description = LocalizedStrings.QmtGatewayHostDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string GatewayHost { get; set; } = "127.0.0.1";

	/// <summary>TCP port of the separately started XtQuant gateway.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.QmtGatewayPortKey,
		Description = LocalizedStrings.QmtGatewayPortDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public int GatewayPort { get; set; } = 58630;

	/// <summary>Shared secret configured in the local XtQuant gateway.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.QmtGatewayTokenKey,
		Description = LocalizedStrings.QmtGatewayTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString GatewayToken { get; set; }

	/// <summary>Maximum number of consecutive gateway reconnect attempts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.QmtReconnectAttemptsKey,
		Description = LocalizedStrings.QmtReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <summary>Maximum time to wait for a gateway response, in seconds.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.QmtRequestTimeoutKey,
		Description = LocalizedStrings.QmtRequestTimeoutDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int RequestTimeout { get; set; } = 30;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(GatewayHost), GatewayHost)
			.Set(nameof(GatewayPort), GatewayPort)
			.Set(nameof(GatewayToken), GatewayToken)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts)
			.Set(nameof(RequestTimeout), RequestTimeout);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		GatewayHost = storage.GetValue(nameof(GatewayHost), GatewayHost);
		GatewayPort = storage.GetValue(nameof(GatewayPort), GatewayPort);
		GatewayToken = storage.GetValue<SecureString>(nameof(GatewayToken));
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
		RequestTimeout = storage.GetValue(nameof(RequestTimeout), RequestTimeout);
	}
}
