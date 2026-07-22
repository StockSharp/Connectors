namespace StockSharp.CoinMetrics;

public partial class CoinMetricsMessageAdapter
{
	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Coin Metrics HTTP API v4 root.</summary>
	[Display(
		Name = "REST endpoint",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string ApiEndpoint { get; set; } =
		"https://community-api.coinmetrics.io/v4/";

	/// <summary>Coin Metrics WebSocket API v4 root.</summary>
	[Display(
		Name = "WebSocket endpoint",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string SocketEndpoint { get; set; } =
		"wss://api.coinmetrics.io/v4/";

	/// <summary>Optional exchange identifier filter.</summary>
	[Display(
		Name = "Exchange filter",
		Description = "Optional Coin Metrics exchange identifier filter.",
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 0)]
	public string ExchangeFilter { get; set; }

	/// <summary>Whether offline markets are included in security lookup.</summary>
	[Display(
		Name = "Include inactive",
		Description = "Include offline Coin Metrics markets.",
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 1)]
	public bool IsInactiveIncluded { get; set; }

	/// <summary>Whether experimental markets are included.</summary>
	[Display(
		Name = "Include experimental",
		Description = "Include markets marked experimental by Coin Metrics.",
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 2)]
	public bool IsExperimentalIncluded { get; set; }

	private TimeSpan _requestInterval = TimeSpan.FromMilliseconds(650);

	/// <summary>Minimum delay between REST requests.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public TimeSpan RequestInterval
	{
		get => _requestInterval;
		set => _requestInterval = value >= TimeSpan.Zero &&
			value <= TimeSpan.FromMinutes(1)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Request interval must be between zero and one minute.");
	}

	private int _maximumItems = 25000;

	/// <summary>Maximum number of markets returned by one lookup.</summary>
	[Display(
		Name = "Maximum items",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int MaximumItems
	{
		get => _maximumItems;
		set => _maximumItems = value is >= 1 and <= 100000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Maximum item count must be between one and 100000.");
	}

	private int _historyLimit = 10000;

	/// <summary>Maximum number of historical records per subscription.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 100000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History limit must be between one and 100000.");
	}

	private int _marketDepth = 100;

	/// <summary>Default and maximum order-book depth.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDepthKey,
		Description = LocalizedStrings.MarketDepthKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 3)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 30000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Coin Metrics order-book depth must be between one and 30000.");
	}

	private TimeSpan _historyLookback = TimeSpan.FromDays(1);

	/// <summary>Default range when a historical request has no start time.</summary>
	[Display(
		Name = "History lookback",
		Description = "Default range used when history has no start time.",
		GroupName = LocalizedStrings.HistoryKey,
		Order = 1)]
	public TimeSpan HistoryLookback
	{
		get => _historyLookback;
		set => _historyLookback = value >= TimeSpan.FromMinutes(1) &&
			value <= TimeSpan.FromDays(3650)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"History lookback must be between one minute and ten years.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(SocketEndpoint), SocketEndpoint)
			.Set(nameof(ExchangeFilter), ExchangeFilter)
			.Set(nameof(IsInactiveIncluded), IsInactiveIncluded)
			.Set(nameof(IsExperimentalIncluded), IsExperimentalIncluded)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(MaximumItems), MaximumItems)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(HistoryLookback), HistoryLookback);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		SocketEndpoint = storage.GetValue(nameof(SocketEndpoint), SocketEndpoint);
		ExchangeFilter = storage.GetValue(nameof(ExchangeFilter), ExchangeFilter);
		IsInactiveIncluded = storage.GetValue(nameof(IsInactiveIncluded),
			IsInactiveIncluded);
		IsExperimentalIncluded = storage.GetValue(nameof(IsExperimentalIncluded),
			IsExperimentalIncluded);
		RequestInterval = storage.GetValue(nameof(RequestInterval), RequestInterval);
		MaximumItems = storage.GetValue(nameof(MaximumItems), MaximumItems);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
		HistoryLookback = storage.GetValue(nameof(HistoryLookback), HistoryLookback);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new CoinMetricsMessageAdapter(TransactionIdGenerator)
		{
			Token = Token,
			ApiEndpoint = ApiEndpoint,
			SocketEndpoint = SocketEndpoint,
			ExchangeFilter = ExchangeFilter,
			IsInactiveIncluded = IsInactiveIncluded,
			IsExperimentalIncluded = IsExperimentalIncluded,
			RequestInterval = RequestInterval,
			MaximumItems = MaximumItems,
			HistoryLimit = HistoryLimit,
			MarketDepth = MarketDepth,
			HistoryLookback = HistoryLookback,
		};
}
