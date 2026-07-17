namespace StockSharp.CapitalFutures;

/// <summary>Official Capital Futures API environments.</summary>
[DataContract]
[Serializable]
public enum CapitalFuturesEnvironments
{
	/// <summary>Production environment without the separate SGX DMA route.</summary>
	[EnumMember]
	Production = 0,
	/// <summary>Broker-authorized test environment without SGX DMA.</summary>
	[EnumMember]
	Testing = 2,
}

/// <summary>The message adapter for the official Capital Futures API.</summary>
[MediaIcon(Media.MediaNames.capital_futures)]
[Doc("topics/api/connectors/stock_market/capital_futures.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CapitalFuturesKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.TaiwanStockExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
[OrderCondition(typeof(CapitalFuturesOrderCondition))]
public partial class CapitalFuturesMessageAdapter : MessageAdapter
{
	/// <summary>Path to the official Capital API interop assembly or extracted C# package.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PathKey,
		Description = LocalizedStrings.CapitalFuturesSdkPathDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string SdkPath { get; set; }

	/// <summary>Capital Futures login identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string Login { get; set; }

	/// <summary>Capital Futures electronic trading password.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Optional domestic futures account.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.CapitalFuturesAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public string Account { get; set; }

	/// <summary>Official Capital API environment.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CapitalFuturesEnvironmentKey,
		Description = LocalizedStrings.CapitalFuturesEnvironmentDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public CapitalFuturesEnvironments Environment { get; set; } = CapitalFuturesEnvironments.Production;

	/// <summary>Whether order, account, reply, and portfolio services are initialized.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CapitalFuturesTradingEnabledKey,
		Description = LocalizedStrings.CapitalFuturesTradingEnabledDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	[BasicSetting]
	public bool IsTradingEnabled { get; set; } = true;

	/// <summary>Optional official SDK log directory.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LogDirectoryKey,
		Description = LocalizedStrings.CapitalFuturesLogPathDescKey,
		GroupName = LocalizedStrings.LoggingKey, Order = 6)]
	public string LogPath { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(SdkPath), SdkPath)
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(Account), Account)
			.Set(nameof(Environment), Environment)
			.Set(nameof(IsTradingEnabled), IsTradingEnabled)
			.Set(nameof(LogPath), LogPath);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		SdkPath = storage.GetValue<string>(nameof(SdkPath));
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Account = storage.GetValue<string>(nameof(Account));
		Environment = storage.GetValue(nameof(Environment), Environment);
		IsTradingEnabled = storage.GetValue(nameof(IsTradingEnabled), IsTradingEnabled);
		LogPath = storage.GetValue<string>(nameof(LogPath));
	}
}
