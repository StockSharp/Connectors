namespace StockSharp.JpmDataQuery;

/// <summary>The message adapter for the J.P. Morgan DataQuery JSON Data API.</summary>
[MediaIcon(Media.MediaNames.jpmorgan)]
[Doc("topics/api/connectors/stock_market/jpm_dataquery.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.JpmDataQueryKey,
	Description = LocalizedStrings.MarketDataConnectorKey, GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX |
	MessageAdapterCategories.Commodities | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Paid)]
public partial class JpmDataQueryMessageAdapter : MessageAdapter
{
	private const string _defaultAttribute = "MIDPRC";

	/// <summary>OAuth application client identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ClientIdKey,
		Description = LocalizedStrings.ClientIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string ClientId { get; set; }

	/// <summary>OAuth application client secret.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString ClientSecret { get; set; }

	/// <summary>Entitled DataQuery group identifier used for instrument discovery.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.GroupIdKey,
		Description = LocalizedStrings.GroupIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 2)]
	[BasicSetting]
	public string GroupId { get; set; }

	/// <summary>DataQuery time-series attribute identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FieldKey,
		Description = LocalizedStrings.FieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 3)]
	[BasicSetting]
	public string Attribute { get; set; } = _defaultAttribute;

	/// <summary>StockSharp Level1 field receiving values of <see cref="Attribute"/>.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarketDataKey,
		Description = LocalizedStrings.FieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 4)]
	public JpmDataQueryValueFields ValueField { get; set; } = JpmDataQueryValueFields.SpreadMiddle;

	/// <summary>Optional StockSharp security type assigned to discovered instruments.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecurityTypeKey,
		Description = LocalizedStrings.SecurityTypeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 5)]
	public SecurityTypes? SecurityType { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(ClientSecret), ClientSecret)
			.Set(nameof(GroupId), GroupId)
			.Set(nameof(Attribute), Attribute)
			.Set(nameof(ValueField), ValueField)
			.Set(nameof(SecurityType), SecurityType);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ClientId = storage.GetValue<string>(nameof(ClientId));
		ClientSecret = storage.GetValue<SecureString>(nameof(ClientSecret));
		GroupId = storage.GetValue<string>(nameof(GroupId));
		Attribute = storage.GetValue(nameof(Attribute), _defaultAttribute);
		ValueField = storage.GetValue(nameof(ValueField), ValueField);
		SecurityType = storage.GetValue<SecurityTypes?>(nameof(SecurityType));
	}
}
