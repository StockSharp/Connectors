namespace StockSharp.Bloomberg;

using System.ComponentModel.DataAnnotations;

/// <summary>The message adapter for Bloomberg BLPAPI and EMSX.</summary>
[MediaIcon(Media.MediaNames.bloomberg)]
[Doc("topics/api/connectors/stock_market/bloomberg.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BloombergKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options | MessageAdapterCategories.FX |
	MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(BloombergOrderCondition))]
public partial class BloombergMessageAdapter : MessageAdapter
{
	private EndPoint _serverAddress = new DnsEndPoint("localhost", 8194);
	private string _emsxService = "//blp/emapisvc";

	/// <summary>Bloomberg BLPAPI service endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ServerKey,
		Description = LocalizedStrings.ServerDescriptionKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public EndPoint ServerAddress
	{
		get => _serverAddress;
		set => _serverAddress = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <summary>Path to Bloomberglp.Blpapi.dll or its containing directory.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PathKey,
		Description = LocalizedStrings.PathDllDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string SdkPath { get; set; }

	/// <summary>Enable the Bloomberg EMSX execution service.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TransactionsKey,
		Description = LocalizedStrings.TransactionsKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public bool IsEmsxEnabled { get; set; }

	/// <summary>Bloomberg EMSX service name.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ServiceKey,
		Description = LocalizedStrings.ServiceKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public string EmsxService
	{
		get => _emsxService;
		set => _emsxService = value.ThrowIfEmpty(nameof(value));
	}

	/// <summary>Default EMSX broker destination.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BrokerKey,
		Description = LocalizedStrings.BrokerKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public string Broker { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ServerAddress), ServerAddress)
			.Set(nameof(SdkPath), SdkPath)
			.Set(nameof(IsEmsxEnabled), IsEmsxEnabled)
			.Set(nameof(EmsxService), EmsxService)
			.Set(nameof(Broker), Broker);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ServerAddress = storage.GetValue(nameof(ServerAddress), ServerAddress);
		SdkPath = storage.GetValue<string>(nameof(SdkPath));
		IsEmsxEnabled = storage.GetValue(nameof(IsEmsxEnabled), IsEmsxEnabled);
		EmsxService = storage.GetValue(nameof(EmsxService), EmsxService);
		Broker = storage.GetValue<string>(nameof(Broker));
	}
}
