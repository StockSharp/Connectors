namespace StockSharp.SecEdgar;

/// <summary>Message adapter for the official SEC EDGAR APIs.</summary>
[MediaIcon(Media.MediaNames.sec_edgar)]
[Doc("topics/api/connectors/stock_market/sec_edgar.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SecEdgarKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.US |
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.History |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.News)]
public partial class SecEdgarMessageAdapter : MessageAdapter
{
	private static readonly Uri _defaultDataEndpoint =
		new("https://data.sec.gov/");
	private static readonly Uri _defaultWebsiteEndpoint =
		new("https://www.sec.gov/");

	/// <summary>SEC JSON data API root endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.SecEdgarDataEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public Uri DataEndpoint { get; set; } = _defaultDataEndpoint;

	/// <summary>SEC public website root endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PublicAddressKey,
		Description = LocalizedStrings.SecEdgarWebsiteEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public Uri WebsiteEndpoint { get; set; } =
		_defaultWebsiteEndpoint;

	/// <summary>Identifying User-Agent required by SEC policy.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UserAgentKey,
		Description = LocalizedStrings.SecEdgarUserAgentDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string UserAgent { get; set; } =
		"StockSharp support@stocksharp.com";

	private TimeSpan _requestInterval =
		TimeSpan.FromMilliseconds(125);

	/// <summary>Minimum delay between SEC requests.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public TimeSpan RequestInterval
	{
		get => _requestInterval;
		set => _requestInterval =
			value >= TimeSpan.FromMilliseconds(100) &&
			value <= TimeSpan.FromSeconds(10)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value),
					value, "SEC request interval must be between " +
						"100 milliseconds and 10 seconds.");
	}

	/// <summary>Comma-separated filing form filter.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FormsKey,
		Description = LocalizedStrings.CommaSeparatedEdgarFormTypesUsedForFilingAndNewsRequestsDescKey,
		GroupName = LocalizedStrings.FiltersKey,
		Order = 0)]
	public string Forms { get; set; } =
		"10-K,10-Q,8-K,20-F,40-F,6-K";

	private int _maximumHistoricalFiles = 20;

	/// <summary>Maximum historical submission files per request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumFilesKey,
		Description = LocalizedStrings.SecEdgarMaximumFilesDescKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 0)]
	public int MaximumHistoricalFiles
	{
		get => _maximumHistoricalFiles;
		set => _maximumHistoricalFiles = value is >= 0 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value),
				value, "Maximum historical file count must be " +
					"between 0 and 1000.");
	}

	private int _maximumFacts = 10000;

	/// <summary>Maximum facts emitted per subscription.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumItemsKey,
		Description = LocalizedStrings.SecEdgarMaximumFactsDescKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 1)]
	public int MaximumFacts
	{
		get => _maximumFacts;
		set => _maximumFacts = value is >= 1 and <= 1000000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value),
				value, "Maximum fact count must be between 1 and " +
					"1000000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(DataEndpoint), DataEndpoint)
			.Set(nameof(WebsiteEndpoint), WebsiteEndpoint)
			.Set(nameof(UserAgent), UserAgent)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(Forms), Forms)
			.Set(nameof(MaximumHistoricalFiles),
				MaximumHistoricalFiles)
			.Set(nameof(MaximumFacts), MaximumFacts);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		DataEndpoint = storage.GetValue(nameof(DataEndpoint),
			_defaultDataEndpoint);
		WebsiteEndpoint = storage.GetValue(nameof(WebsiteEndpoint),
			_defaultWebsiteEndpoint);
		UserAgent = storage.GetValue(nameof(UserAgent),
			"StockSharp support@stocksharp.com");
		RequestInterval = storage.GetValue(nameof(RequestInterval),
			TimeSpan.FromMilliseconds(125));
		Forms = storage.GetValue(nameof(Forms),
			"10-K,10-Q,8-K,20-F,40-F,6-K");
		MaximumHistoricalFiles = storage.GetValue(
			nameof(MaximumHistoricalFiles), 20);
		MaximumFacts = storage.GetValue(nameof(MaximumFacts), 10000);
	}
}
