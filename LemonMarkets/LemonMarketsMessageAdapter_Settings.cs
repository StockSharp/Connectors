namespace StockSharp.LemonMarkets;

/// <summary>The message adapter for the official lemon.markets Brokerage API.</summary>
[MediaIcon(Media.MediaNames.lemonmarkets)]
[Doc("topics/api/connectors/stock_market/lemon_markets.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LemonMarketsKey,
	Description = LocalizedStrings.StockConnectorKey, GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(MessageAdapterCategories.Europe | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Stock)]
[OrderCondition(typeof(LemonMarketsOrderCondition))]
public partial class LemonMarketsMessageAdapter : MessageAdapter, IDemoAdapter
{
	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>Brokerage API key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LemonMarketsApiKeyKey,
		Description = LocalizedStrings.LemonMarketsApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString ApiKey { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <summary>Customer-account identifier. Optional only when the API key exposes one account.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LemonMarketsAccountKey,
		Description = LocalizedStrings.LemonMarketsAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <summary>Default securities-account identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LemonMarketsSecuritiesAccountKey,
		Description = LocalizedStrings.LemonMarketsSecuritiesAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public string SecuritiesAccountId { get; set; }

	/// <summary>Principal recorded in lemon.markets data-protection logs.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LemonMarketsPrivacyPrincipalKey,
		Description = LocalizedStrings.LemonMarketsPrivacyPrincipalDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public string DataPrivacyPrincipal { get; set; }

	/// <summary>Justification recorded in lemon.markets data-protection logs.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LemonMarketsPrivacyJustificationKey,
		Description = LocalizedStrings.LemonMarketsPrivacyJustificationDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	[BasicSetting]
	public string DataPrivacyJustification { get; set; } = "app_usage-stocksharp";

	/// <summary>Optional person identifier recorded as the actor of order actions.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LemonMarketsPersonIdKey,
		Description = LocalizedStrings.LemonMarketsPersonIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public string PersonId { get; set; }

	/// <summary>Default fixed partner fee in EUR.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LemonMarketsDefaultFeeAmountKey,
		Description = LocalizedStrings.LemonMarketsDefaultFeeAmountDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public decimal DefaultFeeAmount { get; set; }

	/// <summary>Default consent for orders requiring an appropriateness acknowledgement.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LemonMarketsAppropriatenessConsentKey,
		Description = LocalizedStrings.LemonMarketsAppropriatenessConsentDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
	public bool IsAppropriatenessConsentAccepted { get; set; }

	/// <summary>Polling interval for quotes, events, balances, and positions.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LemonMarketsPollingIntervalKey,
		Description = LocalizedStrings.LemonMarketsPollingIntervalDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 9)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value < TimeSpan.FromSeconds(5)
			? TimeSpan.FromSeconds(5)
			: value;
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(SecuritiesAccountId), SecuritiesAccountId)
			.Set(nameof(DataPrivacyPrincipal), DataPrivacyPrincipal)
			.Set(nameof(DataPrivacyJustification), DataPrivacyJustification)
			.Set(nameof(PersonId), PersonId)
			.Set(nameof(DefaultFeeAmount), DefaultFeeAmount)
			.Set(nameof(IsAppropriatenessConsentAccepted), IsAppropriatenessConsentAccepted)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		AccountId = storage.GetValue<string>(nameof(AccountId));
		SecuritiesAccountId = storage.GetValue<string>(nameof(SecuritiesAccountId));
		DataPrivacyPrincipal = storage.GetValue<string>(nameof(DataPrivacyPrincipal));
		DataPrivacyJustification = storage.GetValue(nameof(DataPrivacyJustification),
			DataPrivacyJustification);
		PersonId = storage.GetValue<string>(nameof(PersonId));
		DefaultFeeAmount = storage.GetValue(nameof(DefaultFeeAmount), DefaultFeeAmount);
		IsAppropriatenessConsentAccepted = storage.GetValue(
			nameof(IsAppropriatenessConsentAccepted), IsAppropriatenessConsentAccepted);
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
	}
}
