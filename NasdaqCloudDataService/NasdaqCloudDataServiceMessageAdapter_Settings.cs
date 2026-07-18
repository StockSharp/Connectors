namespace StockSharp.NasdaqCloudDataService;

/// <summary>The message adapter for Nasdaq Cloud Data Service REST API.</summary>
[MediaIcon(Media.MediaNames.nasdaq)]
[Doc("topics/api/connectors/stock_market/nasdaq_cloud_data_service.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.NasdaqCloudDataServiceKey,
	Description = LocalizedStrings.MarketDataConnectorKey, GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Options |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Paid)]
public partial class NasdaqCloudDataServiceMessageAdapter : MessageAdapter, ILoginPasswordAdapter
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Customer-specific API base address supplied during onboarding.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public Uri Address { get; set; }

	/// <summary>Equity market data source.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SourceKey,
		Description = LocalizedStrings.SourceKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 3)]
	[BasicSetting]
	public NasdaqCloudSources Source { get; set; } = NasdaqCloudSources.Nasdaq;

	/// <summary>Real-time or delayed market data.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ModeKey,
		Description = LocalizedStrings.ModeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 4)]
	[BasicSetting]
	public NasdaqCloudOffsets Offset { get; set; } = NasdaqCloudOffsets.Delayed;

	/// <summary>Request optional Nasdaq Options Greeks and Implied Volatility data.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.GreeksKey,
		Description = LocalizedStrings.GreeksKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 5)]
	public bool IsOptionGreeksEnabled { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(Address), Address)
			.Set(nameof(Source), Source)
			.Set(nameof(Offset), Offset)
			.Set(nameof(IsOptionGreeksEnabled), IsOptionGreeksEnabled);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Address = storage.GetValue<Uri>(nameof(Address));
		Source = storage.GetValue(nameof(Source), Source);
		Offset = storage.GetValue(nameof(Offset), Offset);
		IsOptionGreeksEnabled = storage.GetValue(nameof(IsOptionGreeksEnabled), IsOptionGreeksEnabled);
	}
}
