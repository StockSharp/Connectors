namespace StockSharp.Coinalyze;

/// <summary>
/// The message adapter for Coinalyze market data.
/// </summary>
[MediaIcon(Media.MediaNames.coinalyze)]
[Doc("topics/api/connectors/crypto_exchanges/coinalyze.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinalyzeKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Crypto |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.Futures)]
public partial class CoinalyzeMessageAdapter :
	MessageAdapter,
	ITokenAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.coinalyze.net/v1";
	private TimeSpan _requestInterval =
		TimeSpan.FromMilliseconds(1500);
	private int _maximumItems = 10000;
	private int _historyLimit = 2000;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ApiKeyKey,
		Description = LocalizedStrings.ApiKeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// Coinalyze REST API root.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } =
		_defaultRestEndpoint;

	/// <summary>
	/// Market family exposed by this adapter instance.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecurityTypeKey,
		Description = LocalizedStrings.SecurityTypeKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 0)]
	[BasicSetting]
	public CoinalyzeMarketTypes MarketType { get; set; }

	/// <summary>
	/// Metric returned by candle subscriptions.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DataTypeKey,
		Description = LocalizedStrings.DataTypeKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 1)]
	[BasicSetting]
	public CoinalyzeCandleMetrics CandleMetric { get; set; }

	/// <summary>
	/// Optional exchange code filter.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExchangeKey,
		Description = LocalizedStrings.ExchangeKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 2)]
	[BasicSetting]
	public string Exchange { get; set; }

	/// <summary>
	/// Convert open-interest and liquidation values to USD.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ConvertKey,
		Description = LocalizedStrings.ConvertKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 3)]
	[BasicSetting]
	public bool ConvertToUsd { get; set; } = true;

	/// <summary>
	/// Minimum delay between REST requests.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public TimeSpan RequestInterval
	{
		get => _requestInterval;
		set => _requestInterval = value >= TimeSpan.Zero &&
			value <= TimeSpan.FromMinutes(1)
				? value
				: throw new ArgumentOutOfRangeException(
					nameof(value), value,
					"Request interval must be between zero and one minute.");
	}

	/// <summary>
	/// Maximum instruments returned by one lookup.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumItemsKey,
		Description = LocalizedStrings.MaximumItemsKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 4)]
	public int MaximumItems
	{
		get => _maximumItems;
		set => _maximumItems = value is >= 1 and <= 100000
			? value
			: throw new ArgumentOutOfRangeException(
				nameof(value), value,
				"Maximum item count must be between 1 and 100000.");
	}

	/// <summary>
	/// Maximum rows emitted by one history subscription.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 2000
			? value
			: throw new ArgumentOutOfRangeException(
				nameof(value), value,
				"History limit must be between 1 and 2000.");
	}

	/// <summary>
	/// Supported Coinalyze time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> CoinalyzeExtensions.TimeFrames;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(MarketType), MarketType)
			.Set(nameof(CandleMetric), CandleMetric)
			.Set(nameof(Exchange), Exchange)
			.Set(nameof(ConvertToUsd), ConvertToUsd)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(MaximumItems), MaximumItems)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		RestEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(RestEndpoint), RestEndpoint));
		MarketType = storage.GetValue(
			nameof(MarketType), MarketType);
		CandleMetric = storage.GetValue(
			nameof(CandleMetric), CandleMetric);
		Exchange = storage.GetValue<string>(nameof(Exchange))?
			.Trim();
		ConvertToUsd = storage.GetValue(
			nameof(ConvertToUsd), ConvertToUsd);
		RequestInterval = storage.GetValue(
			nameof(RequestInterval), RequestInterval);
		MaximumItems = storage.GetValue(
			nameof(MaximumItems), MaximumItems);
		HistoryLimit = storage.GetValue(
			nameof(HistoryLimit), HistoryLimit);
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.IsEmpty()
			? _defaultRestEndpoint
			: endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() +
			$": {MarketType}, {CandleMetric}, {Exchange}";
}
