namespace StockSharp.CoinPaprika;

/// <summary>
/// The message adapter for CoinPaprika market data.
/// </summary>
[MediaIcon(Media.MediaNames.coinpaprika)]
[Doc("topics/api/connectors/crypto_exchanges/coinpaprika.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinPaprikaKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles)]
public partial class CoinPaprikaMessageAdapter :
	MessageAdapter,
	ITokenAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.coinpaprika.com/v1";
	private TimeSpan _requestInterval =
		TimeSpan.FromMilliseconds(100);
	private TimeSpan _pollingInterval =
		TimeSpan.FromMinutes(5);
	private int _maximumItems = 2000;
	private int _historyLimit = 366;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// REST API root. Change it to the pro endpoint when using a
	/// paid plan.
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
	/// Currency used for prices and OHLCV.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.QuoteCurrencyKey,
		Description = LocalizedStrings.QuoteCurrencyKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 0)]
	[BasicSetting]
	public string QuoteCurrency { get; set; } = "USD";

	/// <summary>
	/// Optional CoinPaprika exchange identifier. When specified,
	/// security lookup returns that exchange's markets.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExchangeKey,
		Description = LocalizedStrings.ExchangeKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 1)]
	[BasicSetting]
	public string ExchangeId { get; set; }

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
					"Request interval must be between zero and " +
						"one minute.");
	}

	/// <summary>
	/// Interval for refreshing live Level1 subscriptions.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalDataUpdatesKey,
		Description = LocalizedStrings.IntervalDataUpdatesKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 2)]
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
	/// Maximum securities returned by one lookup.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumItemsKey,
		Description = LocalizedStrings.MaximumItemsKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 3)]
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
	/// Maximum OHLCV rows requested at once.
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
		set => _historyLimit = value is >= 1 and <= 366
			? value
			: throw new ArgumentOutOfRangeException(
				nameof(value), value,
				"CoinPaprika accepts between 1 and 366 OHLCV " +
					"rows per request.");
	}

	/// <summary>
	/// Supported OHLCV time frames. Intraday intervals require an
	/// eligible paid plan.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> CoinPaprikaExtensions.TimeFrames;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(QuoteCurrency), QuoteCurrency)
			.Set(nameof(ExchangeId), ExchangeId)
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
		QuoteCurrency = CoinPaprikaExtensions.NormalizeQuote(
			storage.GetValue(nameof(QuoteCurrency), QuoteCurrency));
		ExchangeId = storage.GetValue<string>(nameof(ExchangeId))?
			.Trim();
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
			endpoint =
				$"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() +
			$": Quote={QuoteCurrency}, Exchange={ExchangeId}";
}
