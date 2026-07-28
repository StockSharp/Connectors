namespace StockSharp.DexScreener;

/// <summary>
/// The message adapter for DEX Screener market data.
/// </summary>
[MediaIcon(Media.MediaNames.dexscreener)]
[Doc("topics/api/connectors/crypto_exchanges/dex_screener.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DexScreenerKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.Level1)]
public partial class DexScreenerMessageAdapter : MessageAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.dexscreener.com";
	private TimeSpan _requestInterval =
		TimeSpan.FromMilliseconds(200);
	private TimeSpan _pollingInterval =
		TimeSpan.FromSeconds(30);
	private int _maximumItems = 100;

	/// <summary>
	/// DEX Screener REST API root.
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
	/// Optional DEX Screener chain identifier.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ChainIdKey,
		Description = LocalizedStrings.ChainIdKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 0)]
	[BasicSetting]
	public string ChainId { get; set; }

	/// <summary>
	/// Optional token address used to list all its pools.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 1)]
	[BasicSetting]
	public string TokenAddress { get; set; }

	/// <summary>
	/// Default pair search query.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SearchKey,
		Description = LocalizedStrings.SearchKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 2)]
	[BasicSetting]
	public string SearchQuery { get; set; } = "USDC";

	/// <summary>
	/// Prefer the USD price when DEX Screener supplies it.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CurrencyKey,
		Description = LocalizedStrings.CurrencyKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 3)]
	[BasicSetting]
	public bool PriceInUsd { get; set; } = true;

	/// <summary>
	/// Minimum delay between REST requests.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
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
	/// Maximum pools returned by one lookup.
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
		set => _maximumItems = value is >= 1 and <= 10000
			? value
			: throw new ArgumentOutOfRangeException(
				nameof(value), value,
				"Maximum item count must be between 1 and 10000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(ChainId), ChainId)
			.Set(nameof(TokenAddress), TokenAddress)
			.Set(nameof(SearchQuery), SearchQuery)
			.Set(nameof(PriceInUsd), PriceInUsd)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(MaximumItems), MaximumItems);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		RestEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(RestEndpoint), RestEndpoint));
		ChainId = storage.GetValue<string>(nameof(ChainId))?
			.Trim();
		TokenAddress =
			storage.GetValue<string>(nameof(TokenAddress))?
				.Trim();
		SearchQuery = storage.GetValue(
			nameof(SearchQuery), SearchQuery)?.Trim();
		PriceInUsd = storage.GetValue(
			nameof(PriceInUsd), PriceInUsd);
		RequestInterval = storage.GetValue(
			nameof(RequestInterval), RequestInterval);
		PollingInterval = storage.GetValue(
			nameof(PollingInterval), PollingInterval);
		MaximumItems = storage.GetValue(
			nameof(MaximumItems), MaximumItems);
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
			$": Chain={ChainId}, Token={TokenAddress}";
}
