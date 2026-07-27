namespace StockSharp.DowJones;

/// <summary>The message adapter for Dow Jones Newswires APIs.</summary>
[MediaIcon(Media.MediaNames.dowjones)]
[Doc("topics/api/connectors/stock_market/dow_jones.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DowJonesKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Paid |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.News)]
public partial class DowJonesMessageAdapter : MessageAdapter, ITokenAdapter,
	ILoginPasswordAdapter, IAddressAdapter<Uri>
{
	/// <summary>Authentication scheme.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AuthorizationKey,
		Description = LocalizedStrings.AuthorizationKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public DowJonesAuthenticationModes AuthenticationMode { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>OAuth client identifier for service-account authentication.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClientIdKey,
		Description = LocalizedStrings.DowJonesOAuthClientIdentifierDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string ClientId { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Dow Jones API base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api.dowjones.com/");

	/// <summary>Dow Jones OAuth token address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OAuthAddressKey,
		Description = LocalizedStrings.DowJonesOAuthTokenAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public Uri OAuthAddress { get; set; } =
		new("https://accounts.dowjones.com/oauth2/v1/token");

	/// <summary>Optional Unified Query Language filter applied to every request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NewsQueryKey,
		Description = LocalizedStrings.OptionalUnifiedQueryLanguageFilterAppliedToEveryRequestDescKey,
		GroupName = LocalizedStrings.NewsKey,
		Order = 7)]
	public string NewsQuery { get; set; }

	/// <summary>Retrieve full licensed article text through the Content API.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FullArticleTextKey,
		Description = LocalizedStrings.RetrieveFullLicensedArticleTextThroughTheContentApiDescKey,
		GroupName = LocalizedStrings.NewsKey,
		Order = 8)]
	public bool IsFullTextEnabled { get; set; } = true;

	/// <summary>Interval between Real-Time API polls.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PollingIntervalKey,
		Description = LocalizedStrings.IntervalBetweenRealTimeApiPollsForLiveNewsDescKey,
		GroupName = LocalizedStrings.NewsKey,
		Order = 9)]
	public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>Maximum records requested from one API page.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PageLimitKey,
		Description = LocalizedStrings.MaximumRecordsRequestedFromOneRealTimeApiPageDescKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 10)]
	public int PageLimit { get; set; } = 100;

	/// <summary>Maximum news records emitted by one history request or live poll.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NewsLimitKey,
		Description = LocalizedStrings.MaximumRecordsEmittedByOneHistoryRequestOrLivePollDescKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 11)]
	public int MaxNewsItems { get; set; } = 10000;

	/// <summary>Lookback used for history without an explicit start time.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DefaultHistoryLookbackKey,
		Description = LocalizedStrings.LookbackUsedForHistoryWithoutAnExplicitStartTimeDescKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 12)]
	public TimeSpan DefaultHistoryLookback { get; set; } = TimeSpan.FromDays(1);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AuthenticationMode), AuthenticationMode)
			.Set(nameof(Token), Token)
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(Address), Address)
			.Set(nameof(OAuthAddress), OAuthAddress)
			.Set(nameof(NewsQuery), NewsQuery)
			.Set(nameof(IsFullTextEnabled), IsFullTextEnabled)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(PageLimit), PageLimit)
			.Set(nameof(MaxNewsItems), MaxNewsItems)
			.Set(nameof(DefaultHistoryLookback), DefaultHistoryLookback);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AuthenticationMode = storage.GetValue(nameof(AuthenticationMode), AuthenticationMode);
		Token = storage.GetValue<SecureString>(nameof(Token));
		ClientId = storage.GetValue<string>(nameof(ClientId));
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Address = storage.GetValue(nameof(Address), Address);
		OAuthAddress = storage.GetValue(nameof(OAuthAddress), OAuthAddress);
		NewsQuery = storage.GetValue<string>(nameof(NewsQuery));
		IsFullTextEnabled = storage.GetValue(nameof(IsFullTextEnabled), IsFullTextEnabled);
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
		PageLimit = storage.GetValue(nameof(PageLimit), PageLimit);
		MaxNewsItems = storage.GetValue(nameof(MaxNewsItems), MaxNewsItems);
		DefaultHistoryLookback = storage.GetValue(nameof(DefaultHistoryLookback),
			DefaultHistoryLookback);
	}
}
