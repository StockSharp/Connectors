namespace StockSharp.Xtp;

using System.ComponentModel.DataAnnotations;

/// <summary>The message adapter for Zhongtai XTP 2.2.50.8.</summary>
[MediaIcon(Media.MediaNames.xtp)]
[Doc("topics/api/connectors/stock_market/xtp.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.XtpKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.ChinaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Options)]
[OrderCondition(typeof(XtpOrderCondition))]
public partial class XtpMessageAdapter : MessageAdapter, ILoginPasswordAdapter
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey, Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey, Description = LocalizedStrings.PasswordKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>XTP client identifier (1-99 for regular accounts).</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ClientIdKey, Description = LocalizedStrings.XtpClientIdDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public byte ClientId { get; set; } = 1;

	/// <summary>Quote service endpoint supplied by the broker.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarketDataKey, Description = LocalizedStrings.XtpQuoteAddressDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public EndPoint QuoteAddress { get; set; }

	/// <summary>Trader service endpoint supplied by the broker.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TransactionsKey, Description = LocalizedStrings.XtpTraderAddressDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public EndPoint TransactionAddress { get; set; }

	/// <summary>Transport protocol.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ProtocolKey, Description = LocalizedStrings.ProtocolKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public XtpProtocols Protocol { get; set; } = XtpProtocols.Tcp;

	/// <summary>Optional local IP address used to bind the SDK connection.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LocalAddressKey, Description = LocalizedStrings.XtpLocalAddressDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public string LocalAddress { get; set; }

	/// <summary>Software key assigned by Zhongtai.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SoftwareKeyKey, Description = LocalizedStrings.XtpSoftwareKeyDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public string SoftwareKey { get; set; }

	/// <summary>Client software version reported to XTP.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.VersionKey, Description = LocalizedStrings.XtpSoftwareVersionDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
	public string SoftwareVersion { get; set; } = "1.0";

	/// <summary>Writable SDK state and log directory.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DataDirectoryKey, Description = LocalizedStrings.XtpDataDirectoryDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 9)]
	public string DataPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockSharp", "Xtp");

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(QuoteAddress), QuoteAddress)
			.Set(nameof(TransactionAddress), TransactionAddress)
			.Set(nameof(Protocol), Protocol)
			.Set(nameof(LocalAddress), LocalAddress)
			.Set(nameof(SoftwareKey), SoftwareKey)
			.Set(nameof(SoftwareVersion), SoftwareVersion)
			.Set(nameof(DataPath), DataPath);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		ClientId = storage.GetValue(nameof(ClientId), (byte)1);
		QuoteAddress = storage.GetValue<EndPoint>(nameof(QuoteAddress));
		TransactionAddress = storage.GetValue<EndPoint>(nameof(TransactionAddress));
		Protocol = storage.GetValue(nameof(Protocol), XtpProtocols.Tcp);
		LocalAddress = storage.GetValue<string>(nameof(LocalAddress));
		SoftwareKey = storage.GetValue<string>(nameof(SoftwareKey));
		SoftwareVersion = storage.GetValue(nameof(SoftwareVersion), "1.0");
		DataPath = storage.GetValue(nameof(DataPath), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockSharp", "Xtp"));
	}
}
