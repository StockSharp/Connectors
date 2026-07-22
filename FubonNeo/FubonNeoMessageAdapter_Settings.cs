namespace StockSharp.FubonNeo;

/// <summary>Official Fubon market-data modes.</summary>
[DataContract]
[Serializable]
public enum FubonNeoRealtimeModes
{
	/// <summary>Lowest-latency trades and books.</summary>
	[EnumMember]
	Speed,

	/// <summary>Full feed including aggregate and candle channels.</summary>
	[EnumMember]
	Normal,
}

/// <summary>The message adapter for the official Fubon Neo C# SDK.</summary>
[MediaIcon(Media.MediaNames.fubon_neo)]
[Doc("topics/api/connectors/stock_market/fubon_neo.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FubonNeoKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.TaiwanStockExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.History | MessageAdapterCategories.Candles | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
[OrderCondition(typeof(FubonNeoOrderCondition))]
public partial class FubonNeoMessageAdapter : MessageAdapter
{
	/// <summary>Path to the official SDK assembly or extracted nupkg directory.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PathKey,
		Description = LocalizedStrings.FubonNeoSdkPathDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string SdkPath { get; set; }

	/// <summary>Fubon personal identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FubonNeoPersonalIdKey,
		Description = LocalizedStrings.FubonNeoPersonalIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string PersonalId { get; set; }

	/// <summary>Fubon trading password.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Fubon Neo API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.FubonNeoApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString ApiKey { get; set; }

	/// <summary>Use API-key authentication.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FubonNeoApiKeyLoginKey,
		Description = LocalizedStrings.FubonNeoApiKeyLoginDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public bool IsApiKeyLogin { get; set; }

	/// <summary>Electronic trading certificate path.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CertificateKey,
		Description = LocalizedStrings.FubonNeoCertPathDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string CertificatePath { get; set; }

	/// <summary>Electronic trading certificate password.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.FubonNeoCertPasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public SecureString CertificatePassword { get; set; }

	/// <summary>Optional service URL supplied by Fubon for non-production environments.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.FubonNeoEnvironmentUrlDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public string EnvironmentUrl { get; set; }

	/// <summary>Market-data mode.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ModeKey,
		Description = LocalizedStrings.FubonNeoRealtimeModeDescKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 8)]
	public FubonNeoRealtimeModes RealtimeMode { get; set; } = FubonNeoRealtimeModes.Normal;

	/// <summary>Maximum number of WebSocket reconnect attempts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FubonNeoReconnectAttemptsKey,
		Description = LocalizedStrings.FubonNeoReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(SdkPath), SdkPath)
			.Set(nameof(PersonalId), PersonalId)
			.Set(nameof(Password), Password)
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(IsApiKeyLogin), IsApiKeyLogin)
			.Set(nameof(CertificatePath), CertificatePath)
			.Set(nameof(CertificatePassword), CertificatePassword)
			.Set(nameof(EnvironmentUrl), EnvironmentUrl)
			.Set(nameof(RealtimeMode), RealtimeMode)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		SdkPath = storage.GetValue<string>(nameof(SdkPath));
		PersonalId = storage.GetValue<string>(nameof(PersonalId));
		Password = storage.GetValue<SecureString>(nameof(Password));
		ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
		IsApiKeyLogin = storage.GetValue(nameof(IsApiKeyLogin), IsApiKeyLogin);
		CertificatePath = storage.GetValue<string>(nameof(CertificatePath));
		CertificatePassword = storage.GetValue<SecureString>(nameof(CertificatePassword));
		EnvironmentUrl = storage.GetValue<string>(nameof(EnvironmentUrl));
		RealtimeMode = storage.GetValue(nameof(RealtimeMode), RealtimeMode);
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
	}
}
