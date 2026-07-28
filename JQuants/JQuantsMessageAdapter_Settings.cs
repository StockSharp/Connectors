namespace StockSharp.JQuants;

/// <summary>The message adapter for J-Quants API V2.</summary>
[MediaIcon(Media.MediaNames.jquants)]
[Doc("topics/api/connectors/stock_market/jquants.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.JQuantsKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.JapanKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Asia |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options)]
public partial class JQuantsMessageAdapter : MessageAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.jquants.com/v2";

	/// <summary>J-Quants V2 API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ApiKeyKey,
		Description = LocalizedStrings.JQuantsApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>J-Quants V2 REST root endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public string RestEndpoint { get; set; } =
		_defaultRestEndpoint;

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
					value, "J-Quants request interval must be " +
						"between zero and one minute.");
	}

	private int _maximumPages = 1000;

	/// <summary>Maximum number of pagination pages per request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumItemsKey,
		Description = LocalizedStrings.MaximumItemsKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public int MaximumPages
	{
		get => _maximumPages;
		set => _maximumPages = value is >= 1 and <= 10000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value),
				value, "Maximum page count must be between 1 and " +
					"10000.");
	}

	/// <summary>Supported J-Quants candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		JQuantsExtensions.TimeFrames;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(MaximumPages), MaximumPages);
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
		MaximumPages = storage.GetValue(nameof(MaximumPages), 1000);
	}
}
