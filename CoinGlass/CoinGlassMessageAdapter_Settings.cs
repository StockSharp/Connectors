namespace StockSharp.CoinGlass;

/// <summary>
/// The message adapter for CoinGlass market analytics.
/// </summary>
[MediaIcon(Media.MediaNames.coinglass)]
[Doc("topics/api/connectors/crypto_exchanges/coinglass.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinGlassKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options |
	MessageAdapterCategories.Stock)]
public partial class CoinGlassMessageAdapter :
	MessageAdapter,
	ITokenAdapter
{
	private const string _defaultRestEndpoint =
		"https://open-api-v4.coinglass.com";
	private TimeSpan _requestInterval =
		TimeSpan.FromMilliseconds(250);
	private TimeSpan _pollingInterval =
		TimeSpan.FromMinutes(1);
	private int _maximumItems = 5000;
	private int _historyLimit = 1000;

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
	/// CoinGlass REST API root.
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
	public CoinGlassMarketTypes MarketType { get; set; }

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
	public CoinGlassCandleMetrics CandleMetric { get; set; }

	/// <summary>
	/// Exchange used for pair-level requests.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExchangeKey,
		Description = LocalizedStrings.ExchangeKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 2)]
	[BasicSetting]
	public string Exchange { get; set; } = "Binance";

	/// <summary>
	/// Coin used by options lookup and as the default lookup filter.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SymbolsKey,
		Description = LocalizedStrings.SymbolsKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 3)]
	[BasicSetting]
	public string Symbol { get; set; } = "BTC";

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
	/// Interval for refreshing Level1 subscriptions.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalDataUpdatesKey,
		Description = LocalizedStrings.IntervalDataUpdatesKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 4)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value > TimeSpan.Zero
			? value
			: throw new ArgumentOutOfRangeException(
				nameof(value), value,
				"Polling interval must be positive.");
	}

	/// <summary>
	/// Maximum instruments returned by one lookup.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumItemsKey,
		Description = LocalizedStrings.MaximumItemsKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 5)]
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
	/// Maximum rows requested from a history endpoint.
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
		set => _historyLimit = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(
				nameof(value), value,
				"CoinGlass accepts between 1 and 1000 history rows.");
	}

	/// <summary>
	/// Supported CoinGlass time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> CoinGlassExtensions.TimeFrames;

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
			.Set(nameof(Symbol), Symbol)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(PollingInterval), PollingInterval)
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
		Exchange = NormalizeRequired(
			storage.GetValue(nameof(Exchange), Exchange),
			nameof(Exchange));
		Symbol = NormalizeSymbol(
			storage.GetValue(nameof(Symbol), Symbol));
		RequestInterval = storage.GetValue(
			nameof(RequestInterval), RequestInterval);
		PollingInterval = storage.GetValue(
			nameof(PollingInterval), PollingInterval);
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

	private static string NormalizeRequired(
		string value,
		string parameterName)
		=> value.ThrowIfEmpty(parameterName).Trim();

	private static string NormalizeSymbol(string value)
		=> NormalizeRequired(value, nameof(value)).ToUpperInvariant();

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() +
			$": {MarketType}, {Exchange}, {CandleMetric}";
}
