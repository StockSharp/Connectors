namespace StockSharp.Yuanta;

/// <summary>Official Yuanta SPARK service environments.</summary>
[DataContract]
[Serializable]
public enum YuantaEnvironments
{
	/// <summary>Production trading environment.</summary>
	[EnumMember]
	Production,
	/// <summary>Broker-approved UAT environment.</summary>
	[EnumMember]
	Uat,
}

/// <summary>The message adapter for the official Yuanta SPARK C# SDK.</summary>
[MediaIcon(Media.MediaNames.yuanta)]
[Doc("topics/api/connectors/stock_market/yuanta.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.YuantaKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.TaiwanStockExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.History | MessageAdapterCategories.Candles | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
[OrderCondition(typeof(YuantaOrderCondition))]
public partial class YuantaMessageAdapter : MessageAdapter
{
	/// <summary>Path to the official Yuanta SPARK SDK assembly or extracted SDK directory.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PathKey,
		Description = LocalizedStrings.YuantaSdkPathDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string SdkPath { get; set; }

	/// <summary>Yuanta securities or futures account.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.YuantaAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string Account { get; set; }

	/// <summary>Yuanta electronic trading password.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>PFX certificate path used by the SDK on Linux and macOS.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CertificateKey,
		Description = LocalizedStrings.YuantaCertificatePathDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public string CertificatePath { get; set; }

	/// <summary>PFX certificate password.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.YuantaCertificatePasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public SecureString CertificatePassword { get; set; }

	/// <summary>Official Yuanta environment.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.YuantaEnvironmentKey,
		Description = LocalizedStrings.YuantaEnvironmentDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	[BasicSetting]
	public YuantaEnvironments Environment { get; set; } = YuantaEnvironments.Production;

	/// <summary>Optional official SDK log directory.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LogDirectoryKey,
		Description = LocalizedStrings.YuantaLogPathDescKey,
		GroupName = LocalizedStrings.LoggingKey, Order = 6)]
	public string LogPath { get; set; }

	/// <summary>Maximum connection restoration attempts.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.YuantaReconnectAttemptsKey,
		Description = LocalizedStrings.YuantaReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(SdkPath), SdkPath)
			.Set(nameof(Account), Account)
			.Set(nameof(Password), Password)
			.Set(nameof(CertificatePath), CertificatePath)
			.Set(nameof(CertificatePassword), CertificatePassword)
			.Set(nameof(Environment), Environment)
			.Set(nameof(LogPath), LogPath)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		SdkPath = storage.GetValue<string>(nameof(SdkPath));
		Account = storage.GetValue<string>(nameof(Account));
		Password = storage.GetValue<SecureString>(nameof(Password));
		CertificatePath = storage.GetValue<string>(nameof(CertificatePath));
		CertificatePassword = storage.GetValue<SecureString>(nameof(CertificatePassword));
		Environment = storage.GetValue(nameof(Environment), Environment);
		LogPath = storage.GetValue<string>(nameof(LogPath));
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
	}
}
