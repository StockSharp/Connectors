namespace StockSharp.Glassnode;

public partial class GlassnodeMessageAdapter
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

	/// <summary>Glassnode REST API v1 root.</summary>
	[Display(
		Name = "REST endpoint",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string ApiEndpoint { get; set; } = "https://api.glassnode.com/v1/";

	private TimeSpan _priceTimeFrame = TimeSpan.FromMinutes(10);

	/// <summary>Interval used for Level 1 closing-price history.</summary>
	[Display(
		Name = "Price interval",
		Description = "Glassnode interval used for Level 1 close values.",
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 0)]
	[BasicSetting]
	public TimeSpan PriceTimeFrame
	{
		get => _priceTimeFrame;
		set
		{
			_ = value.ToInterval();
			_priceTimeFrame = value;
		}
	}

	private TimeSpan _requestInterval = TimeSpan.FromMilliseconds(100);

	/// <summary>Minimum delay between REST requests.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
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

	/// <summary>Maximum number of assets returned by a lookup.</summary>
	[Display(
		Name = "Maximum items",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
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

	private TimeSpan _historyLookback = TimeSpan.FromDays(365);

	/// <summary>Default range when a request has no start time.</summary>
	[Display(
		Name = "History lookback",
		Description = "Default range used when history has no start time.",
		GroupName = LocalizedStrings.HistoryKey,
		Order = 1)]
	public TimeSpan HistoryLookback
	{
		get => _historyLookback;
		set => _historyLookback = value >= TimeSpan.FromMinutes(10) &&
			value <= TimeSpan.FromDays(3650)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"History lookback must be between ten minutes and ten years.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(PriceTimeFrame), PriceTimeFrame)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(MaximumItems), MaximumItems)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(HistoryLookback), HistoryLookback);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		PriceTimeFrame = storage.GetValue(nameof(PriceTimeFrame), PriceTimeFrame);
		RequestInterval = storage.GetValue(nameof(RequestInterval), RequestInterval);
		MaximumItems = storage.GetValue(nameof(MaximumItems), MaximumItems);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		HistoryLookback = storage.GetValue(nameof(HistoryLookback), HistoryLookback);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new GlassnodeMessageAdapter(TransactionIdGenerator)
		{
			Token = Token,
			ApiEndpoint = ApiEndpoint,
			PriceTimeFrame = PriceTimeFrame,
			RequestInterval = RequestInterval,
			MaximumItems = MaximumItems,
			HistoryLimit = HistoryLimit,
			HistoryLookback = HistoryLookback,
		};
}
