namespace StockSharp.SnapTrade;

/// <summary>The message adapter for the official SnapTrade API.</summary>
[MediaIcon(Media.MediaNames.snaptrade)]
[Doc("topics/api/connectors/stock_market/snaptrade.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SnapTradeKey,
	Description = LocalizedStrings.StockConnectorKey, GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Stock)]
[OrderCondition(typeof(SnapTradeOrderCondition))]
public partial class SnapTradeMessageAdapter : MessageAdapter
{
	private TimeSpan _pollingInterval = TimeSpan.FromMinutes(1);

	/// <summary>Partner or Personal API client identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SnapTradeClientIdKey,
		Description = LocalizedStrings.SnapTradeClientIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string ClientId { get; set; }

	/// <summary>Secret used to sign SnapTrade API requests.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SnapTradeConsumerKeyKey,
		Description = LocalizedStrings.SnapTradeConsumerKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString ConsumerKey { get; set; }

	/// <summary>Commercial SnapTrade user identifier. Empty for a Personal API key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SnapTradeUserIdKey,
		Description = LocalizedStrings.SnapTradeUserIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public string UserId { get; set; }

	/// <summary>Commercial SnapTrade user secret. Empty for a Personal API key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SnapTradeUserSecretKey,
		Description = LocalizedStrings.SnapTradeUserSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public SecureString UserSecret { get; set; }

	/// <summary>Brokerage-account identifier. Optional only when one usable account is available.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SnapTradeAccountKey,
		Description = LocalizedStrings.SnapTradeAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <summary>Minimum spacing between rate-aware polling jobs.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SnapTradePollingIntervalKey,
		Description = LocalizedStrings.SnapTradePollingIntervalDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value < TimeSpan.FromSeconds(15)
			? TimeSpan.FromSeconds(15)
			: value;
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(ConsumerKey), ConsumerKey)
			.Set(nameof(UserId), UserId)
			.Set(nameof(UserSecret), UserSecret)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ClientId = storage.GetValue<string>(nameof(ClientId));
		ConsumerKey = storage.GetValue<SecureString>(nameof(ConsumerKey));
		UserId = storage.GetValue<string>(nameof(UserId));
		UserSecret = storage.GetValue<SecureString>(nameof(UserSecret));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
	}
}
