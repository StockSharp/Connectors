namespace StockSharp.TradingTechnologies;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>TT service environments available to external client-side SDK applications.</summary>
[DataContract]
public enum TradingTechnologiesEnvironments
{
	/// <summary>Live production exchange feeds and matching engines.</summary>
	[EnumMember]
	ProdLive,

	/// <summary>Production market data with TT simulated order matching.</summary>
	[EnumMember]
	ProdSim,

	/// <summary>UAT exchange certification environment.</summary>
	[EnumMember]
	UatCert,
}

/// <summary>The message adapter for the client-side Trading Technologies TT .NET SDK.</summary>
[MediaIcon(Media.MediaNames.trading_technologies)]
[Doc("topics/api/connectors/stock_market/trading_technologies.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TradingTechnologiesKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(TradingTechnologiesOrderCondition))]
public partial class TradingTechnologiesMessageAdapter : MessageAdapter
{
	/// <summary>Path to tt-net-api.dll or its containing directory.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PathKey,
		Description = LocalizedStrings.TradingTechnologiesSdkPathDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string SdkPath { get; set; }

	/// <summary>Combined TT application key and secret.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TradingTechnologiesAppSecretKey,
		Description = LocalizedStrings.TradingTechnologiesAppSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString AppSecretKey { get; set; }

	/// <summary>TT service environment.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TradingTechnologiesEnvironmentKey,
		Description = LocalizedStrings.TradingTechnologiesEnvironmentDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public TradingTechnologiesEnvironments Environment { get; set; } = TradingTechnologiesEnvironments.ProdSim;

	/// <summary>SDK initialization timeout in milliseconds.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TradingTechnologiesTimeoutKey,
		Description = LocalizedStrings.TradingTechnologiesTimeoutDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public int InitializationTimeout { get; set; } = 5000;

	/// <summary>Maximum number of aggregated book levels.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TradingTechnologiesDepthKey,
		Description = LocalizedStrings.TradingTechnologiesDepthDescKey,
		GroupName = LocalizedStrings.MarketDataKey, Order = 4)]
	public int MarketDepth { get; set; } = 20;

	/// <summary>Use the TT binary protocol.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TradingTechnologiesBinaryProtocolKey,
		Description = LocalizedStrings.TradingTechnologiesBinaryProtocolDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public bool IsBinaryProtocol { get; set; } = true;

	/// <summary>Enable TT options market data.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TradingTechnologiesOptionsKey,
		Description = LocalizedStrings.TradingTechnologiesOptionsDescKey,
		GroupName = LocalizedStrings.MarketDataKey, Order = 6)]
	public bool IsOptionsEnabled { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(SdkPath), SdkPath)
			.Set(nameof(AppSecretKey), AppSecretKey)
			.Set(nameof(Environment), Environment)
			.Set(nameof(InitializationTimeout), InitializationTimeout)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(IsBinaryProtocol), IsBinaryProtocol)
			.Set(nameof(IsOptionsEnabled), IsOptionsEnabled);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		SdkPath = storage.GetValue<string>(nameof(SdkPath));
		AppSecretKey = storage.GetValue<SecureString>(nameof(AppSecretKey));
		Environment = storage.GetValue(nameof(Environment), Environment);
		InitializationTimeout = storage.GetValue(nameof(InitializationTimeout), InitializationTimeout);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
		IsBinaryProtocol = storage.GetValue(nameof(IsBinaryProtocol), IsBinaryProtocol);
		IsOptionsEnabled = storage.GetValue(nameof(IsOptionsEnabled), IsOptionsEnabled);
	}
}
