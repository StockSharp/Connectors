namespace StockSharp.Birdeye;

/// <summary>
/// The message adapter for Birdeye Data Services.
/// </summary>
[MediaIcon(Media.MediaNames.birdeye)]
[Doc("topics/api/connectors/crypto_exchanges/birdeye.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BirdeyeKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles)]
public partial class BirdeyeMessageAdapter :
	MessageAdapter,
	ITokenAdapter
{
	private const string _defaultRestEndpoint =
		"https://public-api.birdeye.so";
	private const string _defaultWebSocketEndpoint =
		"wss://public-api.birdeye.so/socket";
	private const string _defaultWebSocketOrigin =
		"https://birdeye.so";
	private TimeSpan _requestInterval = TimeSpan.FromSeconds(1);
	private TimeSpan _pollingInterval = TimeSpan.FromMinutes(1);
	private int _maximumItems = 500;
	private int _historyLimit = 5000;
	private decimal _minimumLiquidity = 100;

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
	/// Birdeye REST API root.
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
	/// Birdeye WebSocket API root, without the chain suffix.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketEndpointKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } =
		_defaultWebSocketEndpoint;

	/// <summary>
	/// Origin header required by the Birdeye WebSocket.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OriginKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string WebSocketOrigin { get; set; } =
		_defaultWebSocketOrigin;

	/// <summary>
	/// Birdeye chain identifier.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ChainIdKey,
		Description = LocalizedStrings.ChainIdKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 0)]
	[BasicSetting]
	public string Chain { get; set; } = "solana";

	/// <summary>
	/// Optional token contract address for targeted lookup.
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
	/// Use the paid real-time WebSocket service.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StreamingKey,
		Description = LocalizedStrings.StreamingKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 2)]
	[BasicSetting]
	public bool StreamingEnabled { get; set; }

	/// <summary>
	/// Return prices in USD instead of the chain-native currency.
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
	/// Minimum liquidity for token-list lookup.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LiquidityKey,
		Description = LocalizedStrings.LiquidityKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 4)]
	public decimal MinimumLiquidity
	{
		get => _minimumLiquidity;
		set => _minimumLiquidity = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(
				nameof(value), value,
				"Minimum liquidity cannot be negative.");
	}

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
	/// Interval for REST Level1 refresh when streaming is disabled.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalDataUpdatesKey,
		Description = LocalizedStrings.IntervalDataUpdatesKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 5)]
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
	/// Maximum tokens returned by one lookup.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumItemsKey,
		Description = LocalizedStrings.MaximumItemsKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 6)]
	public int MaximumItems
	{
		get => _maximumItems;
		set => _maximumItems = value is >= 1 and <= 10000
			? value
			: throw new ArgumentOutOfRangeException(
				nameof(value), value,
				"Maximum item count must be between 1 and 10000.");
	}

	/// <summary>
	/// Maximum candles returned by one history request.
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
		set => _historyLimit = value is >= 1 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(
				nameof(value), value,
				"Birdeye accepts between 1 and 5000 candles.");
	}

	/// <summary>
	/// Supported Birdeye time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> BirdeyeExtensions.TimeFrames;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(WebSocketOrigin), WebSocketOrigin)
			.Set(nameof(Chain), Chain)
			.Set(nameof(TokenAddress), TokenAddress)
			.Set(nameof(StreamingEnabled), StreamingEnabled)
			.Set(nameof(PriceInUsd), PriceInUsd)
			.Set(nameof(MinimumLiquidity), MinimumLiquidity)
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
			storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint);
		WebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(WebSocketEndpoint), WebSocketEndpoint),
			_defaultWebSocketEndpoint);
		WebSocketOrigin = NormalizeEndpoint(
			storage.GetValue(
				nameof(WebSocketOrigin), WebSocketOrigin),
			_defaultWebSocketOrigin);
		Chain = BirdeyeExtensions.NormalizeChain(
			storage.GetValue(nameof(Chain), Chain));
		TokenAddress =
			storage.GetValue<string>(nameof(TokenAddress))?
				.Trim();
		StreamingEnabled = storage.GetValue(
			nameof(StreamingEnabled), StreamingEnabled);
		PriceInUsd = storage.GetValue(
			nameof(PriceInUsd), PriceInUsd);
		MinimumLiquidity = storage.GetValue(
			nameof(MinimumLiquidity), MinimumLiquidity);
		RequestInterval = storage.GetValue(
			nameof(RequestInterval), RequestInterval);
		PollingInterval = storage.GetValue(
			nameof(PollingInterval), PollingInterval);
		MaximumItems = storage.GetValue(
			nameof(MaximumItems), MaximumItems);
		HistoryLimit = storage.GetValue(
			nameof(HistoryLimit), HistoryLimit);
	}

	private static string NormalizeEndpoint(
		string endpoint,
		string defaultEndpoint)
	{
		endpoint = endpoint.IsEmpty()
			? defaultEndpoint
			: endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint =
				$"{(defaultEndpoint.StartsWith("wss")
					? "wss"
					: "https")}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() +
			$": Chain={Chain}, Streaming={StreamingEnabled}";
}
