namespace StockSharp.Ctp;

using System.ComponentModel.DataAnnotations;

/// <summary>The message adapter for Shanghai Futures Information Technology CTP 6.7.11.</summary>
[MediaIcon(Media.MediaNames.ctp)]
[Doc("topics/api/connectors/stock_market/ctp.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CtpKey,
	Description = LocalizedStrings.CtpConnectorDescKey,
	GroupName = LocalizedStrings.ChinaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
[OrderCondition(typeof(CtpOrderCondition))]
public partial class CtpMessageAdapter : MessageAdapter, ILoginPasswordAdapter
{
	/// <summary>CTP user identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey, Description = LocalizedStrings.CtpUserIdDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <summary>CTP user password.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey, Description = LocalizedStrings.PasswordKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Broker identifier issued by the futures broker.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CtpBrokerIdKey, Description = LocalizedStrings.CtpBrokerIdDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public string BrokerId { get; set; }

	/// <summary>Investor identifier. When empty, <see cref="Login"/> is used.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CtpInvestorIdKey, Description = LocalizedStrings.CtpInvestorIdDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public string InvestorId { get; set; }

	/// <summary>Market-data front, including the CTP transport scheme and port.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarketDataKey, Description = LocalizedStrings.CtpMarketAddressDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public string MarketDataAddress { get; set; }

	/// <summary>Trader front, including the CTP transport scheme and port.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TransactionsKey, Description = LocalizedStrings.CtpTraderAddressDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	[BasicSetting]
	public string TraderAddress { get; set; }

	/// <summary>Application identifier registered with the broker.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AppIdKey, Description = LocalizedStrings.CtpAppIdDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public string AppId { get; set; }

	/// <summary>Application authentication code registered with the broker.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CtpAuthCodeKey, Description = LocalizedStrings.CtpAuthCodeDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public SecureString AuthCode { get; set; }

	/// <summary>Client product information reported to the CTP front.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CtpProductInfoKey, Description = LocalizedStrings.CtpProductInfoDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
	public string ProductInfo { get; set; } = "StockSharp";

	/// <summary>Private and public topic recovery mode.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CtpResumeTypeKey, Description = LocalizedStrings.CtpResumeTypeDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 9)]
	public CtpResumeTypes ResumeType { get; set; } = CtpResumeTypes.Quick;

	/// <summary>Use the production-mode switch of the CTP 6.7.11 API.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CtpProductionModeKey, Description = LocalizedStrings.CtpProductionModeDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 10)]
	public bool ProductionMode { get; set; } = true;

	/// <summary>Writable native flow and log directory.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DataDirectoryKey, Description = LocalizedStrings.CtpDataDirectoryDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 11)]
	public string DataPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockSharp", "Ctp");

	/// <summary>Minimum delay between broker query requests.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CtpQueryIntervalKey, Description = LocalizedStrings.CtpQueryIntervalDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 12)]
	public TimeSpan QueryInterval { get; set; } = TimeSpan.FromSeconds(1);

	/// <summary>Maximum time allowed for native authentication, login, and settlement confirmation.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TimeOutKey, Description = LocalizedStrings.CtpConnectionTimeoutDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 13)]
	public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(BrokerId), BrokerId)
			.Set(nameof(InvestorId), InvestorId)
			.Set(nameof(MarketDataAddress), MarketDataAddress)
			.Set(nameof(TraderAddress), TraderAddress)
			.Set(nameof(AppId), AppId)
			.Set(nameof(AuthCode), AuthCode)
			.Set(nameof(ProductInfo), ProductInfo)
			.Set(nameof(ResumeType), ResumeType)
			.Set(nameof(ProductionMode), ProductionMode)
			.Set(nameof(DataPath), DataPath)
			.Set(nameof(QueryInterval), QueryInterval)
			.Set(nameof(ConnectionTimeout), ConnectionTimeout);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		BrokerId = storage.GetValue<string>(nameof(BrokerId));
		InvestorId = storage.GetValue<string>(nameof(InvestorId));
		MarketDataAddress = storage.GetValue<string>(nameof(MarketDataAddress));
		TraderAddress = storage.GetValue<string>(nameof(TraderAddress));
		AppId = storage.GetValue<string>(nameof(AppId));
		AuthCode = storage.GetValue<SecureString>(nameof(AuthCode));
		ProductInfo = storage.GetValue(nameof(ProductInfo), "StockSharp");
		ResumeType = storage.GetValue(nameof(ResumeType), CtpResumeTypes.Quick);
		ProductionMode = storage.GetValue(nameof(ProductionMode), true);
		DataPath = storage.GetValue(nameof(DataPath), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockSharp", "Ctp"));
		QueryInterval = storage.GetValue(nameof(QueryInterval), TimeSpan.FromSeconds(1));
		ConnectionTimeout = storage.GetValue(nameof(ConnectionTimeout), TimeSpan.FromSeconds(30));
	}
}
