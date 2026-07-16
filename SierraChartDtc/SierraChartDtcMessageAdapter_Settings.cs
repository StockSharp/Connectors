namespace StockSharp.SierraChartDtc;

using System.Net;
using System.Security.Authentication;

/// <summary>The message adapter for Sierra Chart and other DTC Protocol servers.</summary>
[MediaIcon(Media.MediaNames.sierrachartdtc)]
[Doc("topics/api/connectors/common/sierra_chart_dtc.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SierraChartDtcKey,
	Description = LocalizedStrings.StockConnectorKey, GroupName = LocalizedStrings.OtherKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.History | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX |
	MessageAdapterCategories.Crypto | MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(SierraChartDtcOrderCondition))]
public partial class SierraChartDtcMessageAdapter : MessageAdapter,
	IAddressAdapter<EndPoint>, ILoginPasswordAdapter
{
	/// <summary>Default DTC market-data and trading endpoint.</summary>
	public static readonly EndPoint DefaultAddress = new IPEndPoint(IPAddress.Loopback, 11099);

	/// <summary>Default Sierra Chart historical DTC endpoint.</summary>
	public static readonly EndPoint DefaultHistoryAddress = new IPEndPoint(IPAddress.Loopback, 11098);

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.SierraChartDtcAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public EndPoint Address { get; set; } = DefaultAddress;

	/// <summary>Endpoint used for historical price requests.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SierraChartDtcHistoryAddressKey,
		Description = LocalizedStrings.SierraChartDtcHistoryAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public EndPoint HistoryAddress { get; set; } = DefaultHistoryAddress;

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public SecureString Password { get; set; }

	/// <summary>Default DTC trade account.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PortfolioKey,
		Description = LocalizedStrings.SierraChartDtcTradeAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public string TradeAccount { get; set; }

	/// <summary>Requested server-side interval for aggregated market-data updates.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SierraChartDtcTransmissionIntervalKey,
		Description = LocalizedStrings.SierraChartDtcTransmissionIntervalDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public TimeSpan MarketDataTransmissionInterval { get; set; }

	/// <summary>Number of order-book levels requested from the DTC server.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DepthKey,
		Description = LocalizedStrings.SierraChartDtcDepthLevelsDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public int MarketDepthLevels { get; set; } = 20;

	/// <summary>TLS protocol used by the market-data and trading connection.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ProtocolKey,
		Description = LocalizedStrings.SslProtocolKey,
		GroupName = LocalizedStrings.SslKey, Order = 100)]
	public SslProtocols SslProtocol { get; set; }

	/// <summary>TLS protocol used by historical connections.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SierraChartDtcHistorySslProtocolKey,
		Description = LocalizedStrings.SslProtocolKey,
		GroupName = LocalizedStrings.SslKey, Order = 101)]
	public SslProtocols HistorySslProtocol { get; set; }

	/// <summary>Whether remote TLS certificates must pass platform validation.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ValidateRemoteCertificatesKey,
		Description = LocalizedStrings.ValidateRemoteCertificatesDescKey,
		GroupName = LocalizedStrings.SslKey, Order = 102)]
	public bool IsCertificateValidation { get; set; } = true;

	/// <summary>Optional TLS server name. The endpoint host is used when empty.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TargetHostKey,
		Description = LocalizedStrings.TargetHostDescKey,
		GroupName = LocalizedStrings.SslKey, Order = 103)]
	public string TargetHost { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Address), Address?.To<string>())
			.Set(nameof(HistoryAddress), HistoryAddress?.To<string>())
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(TradeAccount), TradeAccount)
			.Set(nameof(MarketDataTransmissionInterval), MarketDataTransmissionInterval)
			.Set(nameof(MarketDepthLevels), MarketDepthLevels)
			.Set(nameof(SslProtocol), SslProtocol)
			.Set(nameof(HistorySslProtocol), HistorySslProtocol)
			.Set(nameof(IsCertificateValidation), IsCertificateValidation)
			.Set(nameof(TargetHost), TargetHost);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Address = storage.GetValue<EndPoint>(nameof(Address)) ?? DefaultAddress;
		HistoryAddress = storage.GetValue<EndPoint>(nameof(HistoryAddress)) ?? DefaultHistoryAddress;
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		TradeAccount = storage.GetValue<string>(nameof(TradeAccount));
		MarketDataTransmissionInterval = storage.GetValue(nameof(MarketDataTransmissionInterval), MarketDataTransmissionInterval);
		MarketDepthLevels = storage.GetValue(nameof(MarketDepthLevels), MarketDepthLevels);
		SslProtocol = storage.GetValue(nameof(SslProtocol), SslProtocol);
		HistorySslProtocol = storage.GetValue(nameof(HistorySslProtocol), HistorySslProtocol);
		IsCertificateValidation = storage.GetValue(nameof(IsCertificateValidation), IsCertificateValidation);
		TargetHost = storage.GetValue<string>(nameof(TargetHost));
	}
}
