namespace StockSharp.SimFin;

/// <summary>Message adapter for the SimFin Web API v3.</summary>
[MediaIcon(Media.MediaNames.simfin)]
[Doc("topics/api/connectors/stock_market/simfin.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SimFinKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.US |
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles)]
public partial class SimFinMessageAdapter : MessageAdapter
{
	private static readonly Uri _defaultRestEndpoint =
		new("https://prod.simfin.com/api/v3/");

	/// <summary>SimFin API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ApiKeyKey,
		Description = LocalizedStrings.SimFinApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>SimFin Web API v3 root endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.SimFinRestEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public Uri RestEndpoint { get; set; } = _defaultRestEndpoint;

	private TimeSpan _requestInterval =
		TimeSpan.FromMilliseconds(500);

	/// <summary>Minimum interval between API calls.</summary>
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
			value >= TimeSpan.Zero &&
			value <= TimeSpan.FromMinutes(1)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value),
					value, "SimFin request interval must be between " +
						"zero and one minute.");
	}

	/// <summary>Comma-separated statement types.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StatementTypesKey,
		Description = LocalizedStrings.SimFinStatementTypesDescKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 0)]
	public string StatementTypes { get; set; } =
		"pl,bs,cf,derived";

	/// <summary>Comma-separated fiscal periods.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FiscalPeriodKey,
		Description = LocalizedStrings.SimFinPeriodDescKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 1)]
	public string Period { get; set; } = "fy";

	/// <summary>Request issuer-reported values.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AsReportedKey,
		Description = LocalizedStrings.SimFinAsReportedDescKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 2)]
	public bool AsReported { get; set; }

	/// <summary>Include ratios with daily prices.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IncludeRatiosKey,
		Description = LocalizedStrings.SimFinRatiosDescKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 3)]
	public bool IncludeRatios { get; set; }

	private int _maximumRecords = 100000;

	/// <summary>Maximum records emitted by one subscription.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumItemsKey,
		Description = LocalizedStrings.MaximumItemsKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 0)]
	public int MaximumRecords
	{
		get => _maximumRecords;
		set => _maximumRecords = value is >= 1 and <= 1000000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value),
				value, "Maximum record count must be between 1 and " +
					"1000000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(StatementTypes), StatementTypes)
			.Set(nameof(Period), Period)
			.Set(nameof(AsReported), AsReported)
			.Set(nameof(IncludeRatios), IncludeRatios)
			.Set(nameof(MaximumRecords), MaximumRecords);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint),
			_defaultRestEndpoint);
		RequestInterval = storage.GetValue(nameof(RequestInterval),
			TimeSpan.FromMilliseconds(500));
		StatementTypes = storage.GetValue(nameof(StatementTypes),
			"pl,bs,cf,derived");
		Period = storage.GetValue(nameof(Period), "fy");
		AsReported = storage.GetValue(nameof(AsReported), false);
		IncludeRatios = storage.GetValue(nameof(IncludeRatios), false);
		MaximumRecords = storage.GetValue(nameof(MaximumRecords),
			100000);
	}
}
