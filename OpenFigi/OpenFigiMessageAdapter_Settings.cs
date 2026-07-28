namespace StockSharp.OpenFigi;

/// <summary>
/// Message adapter for the OpenFIGI API v3.
/// </summary>
[MediaIcon(Media.MediaNames.openfigi)]
[Doc("topics/api/connectors/stock_market/openfigi.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OpenFigiKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Free)]
public partial class OpenFigiMessageAdapter : MessageAdapter
{
	private static readonly Uri _defaultRestEndpoint =
		new("https://api.openfigi.com/v3/");

	/// <summary>Optional OpenFIGI API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ApiKeyKey,
		Description = LocalizedStrings.OpenFigiApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>OpenFIGI API v3 root endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.OpenFigiRestEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public Uri RestEndpoint { get; set; } = _defaultRestEndpoint;

	private TimeSpan _requestInterval = TimeSpan.FromSeconds(12);

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
					value, "OpenFIGI request interval must be " +
						"between zero and one minute.");
	}

	private int _maximumPages = 150;

	/// <summary>Maximum search or filter pages per lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumPagesKey,
		Description = LocalizedStrings.OpenFigiMaximumPagesDescKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 0)]
	public int MaximumPages
	{
		get => _maximumPages;
		set => _maximumPages = value is >= 1 and <= 150
			? value
			: throw new ArgumentOutOfRangeException(nameof(value),
				value, "Maximum page count must be between 1 and 150.");
	}

	private int _maximumResults = 15000;

	/// <summary>Maximum instruments read by one lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumItemsKey,
		Description = LocalizedStrings.OpenFigiMaximumResultsDescKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 1)]
	public int MaximumResults
	{
		get => _maximumResults;
		set => _maximumResults = value is >= 1 and <= 15000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value),
				value, "Maximum result count must be between 1 and " +
					"15000.");
	}

	/// <summary>Optional OpenFIGI exchange code.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExchangeCodeKey,
		Description = LocalizedStrings.OpenFigiExchangeCodeDescKey,
		GroupName = LocalizedStrings.FiltersKey,
		Order = 0)]
	public string ExchangeCode { get; set; }

	/// <summary>Optional ISO 10383 MIC.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MicCodeKey,
		Description = LocalizedStrings.OpenFigiMicCodeDescKey,
		GroupName = LocalizedStrings.FiltersKey,
		Order = 1)]
	public string MicCode { get; set; }

	/// <summary>Optional currency filter.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CurrencyKey,
		Description = LocalizedStrings.CurrencyDescKey,
		GroupName = LocalizedStrings.FiltersKey,
		Order = 2)]
	public string Currency { get; set; }

	/// <summary>Optional OpenFIGI market sector.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketSectorKey,
		Description = LocalizedStrings.OpenFigiMarketSectorDescKey,
		GroupName = LocalizedStrings.FiltersKey,
		Order = 3)]
	public string MarketSector { get; set; }

	/// <summary>Optional OpenFIGI securityType2 value.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecurityTypeKey,
		Description = LocalizedStrings.OpenFigiSecurityTypeDescKey,
		GroupName = LocalizedStrings.FiltersKey,
		Order = 4)]
	public string SecurityType2 { get; set; }

	/// <summary>Include unlisted equities.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IncludeUnlistedKey,
		Description = LocalizedStrings.OpenFigiIncludeUnlistedDescKey,
		GroupName = LocalizedStrings.FiltersKey,
		Order = 5)]
	public bool IncludeUnlistedEquities { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(MaximumPages), MaximumPages)
			.Set(nameof(MaximumResults), MaximumResults)
			.Set(nameof(ExchangeCode), ExchangeCode)
			.Set(nameof(MicCode), MicCode)
			.Set(nameof(Currency), Currency)
			.Set(nameof(MarketSector), MarketSector)
			.Set(nameof(SecurityType2), SecurityType2)
			.Set(nameof(IncludeUnlistedEquities),
				IncludeUnlistedEquities);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint),
			_defaultRestEndpoint);
		RequestInterval = storage.GetValue(nameof(RequestInterval),
			TimeSpan.FromSeconds(12));
		MaximumPages = storage.GetValue(nameof(MaximumPages), 150);
		MaximumResults = storage.GetValue(nameof(MaximumResults), 15000);
		ExchangeCode = storage.GetValue<string>(nameof(ExchangeCode));
		MicCode = storage.GetValue<string>(nameof(MicCode));
		Currency = storage.GetValue<string>(nameof(Currency));
		MarketSector = storage.GetValue<string>(nameof(MarketSector));
		SecurityType2 = storage.GetValue<string>(nameof(SecurityType2));
		IncludeUnlistedEquities = storage.GetValue(
			nameof(IncludeUnlistedEquities), false);
	}
}
