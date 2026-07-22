namespace StockSharp.Pyth;

public partial class PythMessageAdapter
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Pyth Pro symbology and history API v1 root.</summary>
	[Display(Name = "History endpoint", GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string HistoryEndpoint { get; set; } =
		"https://pyth.dourolabs.app/v1/";

	/// <summary>Pyth Pro latest-price router API v1 root.</summary>
	[Display(Name = "Router endpoint", GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string RouterEndpoint { get; set; } =
		"https://pyth-lazer.dourolabs.app/v1/";

	/// <summary>Primary Pyth Pro WebSocket endpoint.</summary>
	[Display(Name = "WebSocket 1", GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string WebSocketEndpoint1 { get; set; } =
		"wss://pyth-lazer-0.dourolabs.app/v1/stream";

	/// <summary>Secondary Pyth Pro WebSocket endpoint.</summary>
	[Display(Name = "WebSocket 2", GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	[BasicSetting]
	public string WebSocketEndpoint2 { get; set; } =
		"wss://pyth-lazer-1.dourolabs.app/v1/stream";

	/// <summary>Tertiary Pyth Pro WebSocket endpoint.</summary>
	[Display(Name = "WebSocket 3", GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	[BasicSetting]
	public string WebSocketEndpoint3 { get; set; } =
		"wss://pyth-lazer-2.dourolabs.app/v1/stream";

	/// <summary>Preferred Pyth delivery channel.</summary>
	[Display(Name = "Channel",
		Description = "Preferred channel; feeds automatically use a slower minimum channel when required.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public PythChannels Channel { get; set; } =
		PythChannels.FixedRate200Milliseconds;

	/// <summary>Whether the catalogue is limited to token entitlements.</summary>
	[Display(Name = "Entitled only",
		Description = "Load only feeds available to the configured API token.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	public bool IsEntitledOnly { get; set; } = true;

	/// <summary>Whether inactive and coming-soon feeds are included.</summary>
	[Display(Name = "Include inactive",
		Description = "Include inactive and coming-soon feeds in security lookup.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public bool IsIncludeInactive { get; set; }

	private TimeSpan _requestInterval = TimeSpan.FromMilliseconds(100);

	/// <summary>Minimum delay between REST requests.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
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

	/// <summary>Maximum number of instruments returned by a lookup.</summary>
	[Display(Name = "Maximum items", GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int MaximumItems
	{
		get => _maximumItems;
		set => _maximumItems = value is >= 1 and <= 1000000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Maximum item count must be between one and 1000000.");
	}

	private int _historyLimit = 100000;

	/// <summary>Maximum number of historical records per subscription.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 1000000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History limit must be between one and 1000000.");
	}

	private TimeSpan _historyLookback = TimeSpan.FromDays(365);

	/// <summary>Default range when a request has no start time.</summary>
	[Display(Name = "History lookback",
		Description = "Default range used when history has no start time.",
		GroupName = LocalizedStrings.HistoryKey, Order = 1)]
	public TimeSpan HistoryLookback
	{
		get => _historyLookback;
		set => _historyLookback = value >= TimeSpan.FromMinutes(1) &&
			value <= TimeSpan.FromDays(3650)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"History lookback must be between one minute and ten years.");
	}

	private int _maximumBarsPerRequest = 10000;

	/// <summary>Maximum bars requested from one history call.</summary>
	[Display(Name = "Bars per request",
		Description = "Maximum number of time slots in one Pyth history request.",
		GroupName = LocalizedStrings.HistoryKey, Order = 2)]
	public int MaximumBarsPerRequest
	{
		get => _maximumBarsPerRequest;
		set => _maximumBarsPerRequest = value is >= 100 and <= 100000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Bars per request must be between 100 and 100000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(HistoryEndpoint), HistoryEndpoint)
			.Set(nameof(RouterEndpoint), RouterEndpoint)
			.Set(nameof(WebSocketEndpoint1), WebSocketEndpoint1)
			.Set(nameof(WebSocketEndpoint2), WebSocketEndpoint2)
			.Set(nameof(WebSocketEndpoint3), WebSocketEndpoint3)
			.Set(nameof(Channel), Channel)
			.Set(nameof(IsEntitledOnly), IsEntitledOnly)
			.Set(nameof(IsIncludeInactive), IsIncludeInactive)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(MaximumItems), MaximumItems)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(HistoryLookback), HistoryLookback)
			.Set(nameof(MaximumBarsPerRequest), MaximumBarsPerRequest);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		HistoryEndpoint = storage.GetValue(nameof(HistoryEndpoint), HistoryEndpoint);
		RouterEndpoint = storage.GetValue(nameof(RouterEndpoint), RouterEndpoint);
		WebSocketEndpoint1 = storage.GetValue(nameof(WebSocketEndpoint1),
			WebSocketEndpoint1);
		WebSocketEndpoint2 = storage.GetValue(nameof(WebSocketEndpoint2),
			WebSocketEndpoint2);
		WebSocketEndpoint3 = storage.GetValue(nameof(WebSocketEndpoint3),
			WebSocketEndpoint3);
		Channel = storage.GetValue(nameof(Channel), Channel);
		IsEntitledOnly = storage.GetValue(nameof(IsEntitledOnly), IsEntitledOnly);
		IsIncludeInactive = storage.GetValue(nameof(IsIncludeInactive),
			IsIncludeInactive);
		RequestInterval = storage.GetValue(nameof(RequestInterval), RequestInterval);
		MaximumItems = storage.GetValue(nameof(MaximumItems), MaximumItems);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		HistoryLookback = storage.GetValue(nameof(HistoryLookback), HistoryLookback);
		MaximumBarsPerRequest = storage.GetValue(nameof(MaximumBarsPerRequest),
			MaximumBarsPerRequest);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new PythMessageAdapter(TransactionIdGenerator)
		{
			Token = Token,
			HistoryEndpoint = HistoryEndpoint,
			RouterEndpoint = RouterEndpoint,
			WebSocketEndpoint1 = WebSocketEndpoint1,
			WebSocketEndpoint2 = WebSocketEndpoint2,
			WebSocketEndpoint3 = WebSocketEndpoint3,
			Channel = Channel,
			IsEntitledOnly = IsEntitledOnly,
			IsIncludeInactive = IsIncludeInactive,
			RequestInterval = RequestInterval,
			MaximumItems = MaximumItems,
			HistoryLimit = HistoryLimit,
			HistoryLookback = HistoryLookback,
			MaximumBarsPerRequest = MaximumBarsPerRequest,
		};
}
