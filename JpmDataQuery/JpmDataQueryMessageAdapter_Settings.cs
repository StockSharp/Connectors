namespace StockSharp.JpmDataQuery;

/// <summary>The message adapter for the J.P. Morgan DataQuery JSON Data API.</summary>
[MediaIcon(Media.MediaNames.jpmorgan)]
[Doc("topics/api/connectors/stock_market/jpm_dataquery.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.JpmDataQueryKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX |
	MessageAdapterCategories.Commodities | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Paid)]
public partial class JpmDataQueryMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api-dataquery.jpmchase.com/research/dataquery-authe/api/v2/";
	private const string _defaultOAuthEndpoint = "https://authe.jpmorgan.com/as/token.oauth2";

	private const string _defaultAttribute = "MIDPRC";

	/// <summary>OAuth application client identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.ClientCodeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>OAuth application client secret.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Entitled DataQuery group identifier used for instrument discovery.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.GroupIdKey,
		Description = LocalizedStrings.GroupIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 2)]
	[BasicSetting]
	public string GroupId { get; set; }

	/// <summary>DataQuery time-series attribute identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FieldKey,
		Description = LocalizedStrings.FieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 3)]
	[BasicSetting]
	public string Attribute { get; set; } = _defaultAttribute;

	/// <summary>StockSharp Level1 field receiving values of <see cref="Attribute"/>.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDataKey,
		Description = LocalizedStrings.FieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 4)]
	public JpmDataQueryValueFields ValueField { get; set; } = JpmDataQueryValueFields.SpreadMiddle;

	/// <summary>Optional StockSharp security type assigned to discovered instruments.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecurityTypeKey,
		Description = LocalizedStrings.SecurityTypeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 5)]
	public SecurityTypes? SecurityType { get; set; }

	/// <summary>REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>OAuth token endpoint.</summary>
	[Display(
		Name = "OAuth endpoint",
		Description = "OAuth token endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string OAuthEndpoint { get; set; } = _defaultOAuthEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(GroupId), GroupId)
			.Set(nameof(Attribute), Attribute)
			.Set(nameof(ValueField), ValueField)
			.Set(nameof(SecurityType), SecurityType)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(OAuthEndpoint), OAuthEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		GroupId = storage.GetValue<string>(nameof(GroupId));
		Attribute = storage.GetValue(nameof(Attribute), _defaultAttribute);
		ValueField = storage.GetValue(nameof(ValueField), ValueField);
		SecurityType = storage.GetValue<SecurityTypes?>(nameof(SecurityType));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		OAuthEndpoint = storage.GetValue(nameof(OAuthEndpoint), OAuthEndpoint);
	}
}
