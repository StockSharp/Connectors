namespace StockSharp.Intrinio;

/// <summary>Intrinio real-time equities feeds.</summary>
[DataContract]
[Serializable]
public enum IntrinioEquityProviders
{
	/// <summary>Legacy Intrinio real-time IEX feed.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RealTimeIexLegacyKey)]
	Realtime,

	/// <summary>15-minute delayed consolidated SIP feed.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DelayedSipKey)]
	DelayedSip,

	/// <summary>Nasdaq Basic feed.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NasdaqBasicKey)]
	NasdaqBasic,

	/// <summary>Cboe One feed.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CboeOneKey)]
	CboeOne,

	/// <summary>IEX feed.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IEXKey)]
	Iex,

	/// <summary>Intrinio Equities Edge feed.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EquitiesEdgeKey)]
	EquitiesEdge,
}

/// <summary>Intrinio real-time options feeds.</summary>
[DataContract]
[Serializable]
public enum IntrinioOptionProviders
{
	/// <summary>OPRA feed.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpraKey)]
	Opra,

	/// <summary>Intrinio Options Edge feed.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OptionsEdgeKey)]
	OptionsEdge,
}

/// <summary>The message adapter for Intrinio REST and real-time WebSocket APIs.</summary>
[MediaIcon(Media.MediaNames.intrinio)]
[Doc("topics/api/connectors/stock_market/intrinio.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.IntrinioKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.History | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Options | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.News | MessageAdapterCategories.Paid)]
public partial class IntrinioMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
{
	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Intrinio REST API base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api-v2.intrinio.com/");

	/// <summary>Equities real-time feed.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SourceKey,
		Description = LocalizedStrings.SourceKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 2)]
	[BasicSetting]
	public IntrinioEquityProviders EquityProvider { get; set; } = IntrinioEquityProviders.DelayedSip;

	/// <summary>Options real-time feed.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SourceKey,
		Description = LocalizedStrings.OptionsKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 3)]
	[BasicSetting]
	public IntrinioOptionProviders OptionProvider { get; set; } = IntrinioOptionProviders.Opra;

	/// <summary>Force the options REST and WebSocket feeds into 15-minute delayed mode.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DelayKey,
		Description = LocalizedStrings.DelayKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 4)]
	[BasicSetting]
	public bool IsDelayedOptions { get; set; }

	/// <summary>Use split- and dividend-adjusted equity end-of-day prices.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AdjustedPricesKey,
		Description = LocalizedStrings.UseAdjustedEquityEndOfDayPricesDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 5)]
	public bool IsAdjusted { get; set; }

	/// <summary>Number of official SDK decoder threads for equities.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EquityThreadsKey,
		Description = LocalizedStrings.EquityDecoderWorkerCountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public int EquityThreads { get; set; } = 4;

	/// <summary>Number of official SDK decoder threads for options.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OptionThreadsKey,
		Description = LocalizedStrings.OptionDecoderWorkerCountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public int OptionThreads { get; set; } = 4;

	/// <summary>Official SDK equities input buffer size.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EquityBufferKey,
		Description = LocalizedStrings.EquitySdkInputBufferSizeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public int EquityBufferSize { get; set; } = 4096;

	/// <summary>Official SDK options input buffer size.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OptionBufferKey,
		Description = LocalizedStrings.OptionSdkInputBufferSizeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public int OptionBufferSize { get; set; } = 4096;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(EquityProvider), EquityProvider)
			.Set(nameof(OptionProvider), OptionProvider)
			.Set(nameof(IsDelayedOptions), IsDelayedOptions)
			.Set(nameof(IsAdjusted), IsAdjusted)
			.Set(nameof(EquityThreads), EquityThreads)
			.Set(nameof(OptionThreads), OptionThreads)
			.Set(nameof(EquityBufferSize), EquityBufferSize)
			.Set(nameof(OptionBufferSize), OptionBufferSize);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		EquityProvider = storage.GetValue(nameof(EquityProvider), EquityProvider);
		OptionProvider = storage.GetValue(nameof(OptionProvider), OptionProvider);
		IsDelayedOptions = storage.GetValue(nameof(IsDelayedOptions), IsDelayedOptions);
		IsAdjusted = storage.GetValue(nameof(IsAdjusted), IsAdjusted);
		EquityThreads = storage.GetValue(nameof(EquityThreads), EquityThreads);
		OptionThreads = storage.GetValue(nameof(OptionThreads), OptionThreads);
		EquityBufferSize = storage.GetValue(nameof(EquityBufferSize), EquityBufferSize);
		OptionBufferSize = storage.GetValue(nameof(OptionBufferSize), OptionBufferSize);
	}
}
