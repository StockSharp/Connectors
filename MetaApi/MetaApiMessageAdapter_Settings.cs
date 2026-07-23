namespace StockSharp.MetaApi;

/// <summary>The message adapter for MetaApi REST and real-time streaming APIs.</summary>
[MediaIcon(Media.MediaNames.metaapi)]
[Doc("topics/api/connectors/forex/metaapi.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MetaApiKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.FX |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(MetaApiOrderCondition))]
public partial class MetaApiMessageAdapter : MessageAdapter, ITokenAdapter
{
	private const string _defaultDomain = "agiliumtrade.agiliumtrade.ai";
	private TimeSpan _synchronizationTimeout = TimeSpan.FromMinutes(2);

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.AccessTokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>MetaApi connected trading account identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <summary>Optional account region. API access tokens resolve it automatically.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RegionKey,
		Description = LocalizedStrings.RegionKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string Region { get; set; }

	/// <summary>MetaApi API domain.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DomainAddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string Domain { get; set; } = _defaultDomain;

	/// <summary>Maximum time to wait for server-side terminal synchronization.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeOutKey,
		Description = LocalizedStrings.TimeOutKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public TimeSpan SynchronizationTimeout
	{
		get => _synchronizationTimeout;
		set => _synchronizationTimeout = value < TimeSpan.FromSeconds(10)
			? TimeSpan.FromSeconds(10) : value;
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(Region), Region)
			.Set(nameof(Domain), Domain)
			.Set(nameof(SynchronizationTimeout), SynchronizationTimeout);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		Region = storage.GetValue<string>(nameof(Region));
		Domain = storage.GetValue(nameof(Domain), Domain.IsEmpty(_defaultDomain));
		SynchronizationTimeout = storage.GetValue(nameof(SynchronizationTimeout),
			SynchronizationTimeout);
	}
}
