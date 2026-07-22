namespace StockSharp.Kaiko;

public partial class KaikoMessageAdapter
{
	private KaikoRegions _region = KaikoRegions.Us;
	private string _marketEndpoint = KaikoRegions.Us.GetMarketEndpoint();

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Kaiko REST API region.</summary>
	[Display(
		Name = "Region",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public KaikoRegions Region
	{
		get => _region;
		set
		{
			var previousEndpoint = _region.GetMarketEndpoint();
			if (_marketEndpoint.EqualsIgnoreCase(previousEndpoint))
				_marketEndpoint = value.GetMarketEndpoint();
			_region = value;
		}
	}

	/// <summary>Kaiko public reference data API root.</summary>
	[Display(
		Name = "Reference endpoint",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string ReferenceEndpoint { get; set; } =
		"https://reference-data-api.kaiko.io";

	/// <summary>Kaiko regional market data API root.</summary>
	[Display(
		Name = "Market endpoint",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	[BasicSetting]
	public string MarketEndpoint
	{
		get => _marketEndpoint;
		set => _marketEndpoint = value;
	}

	/// <summary>Kaiko production gRPC Stream endpoint.</summary>
	[Display(
		Name = "Stream endpoint",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	[BasicSetting]
	public string StreamEndpoint { get; set; } =
		"https://gateway-v0-grpc.kaiko.ovh";

	/// <summary>Optional exchange code used to narrow reference lookups.</summary>
	[Display(
		Name = "Exchange filter",
		Description = "Optional Kaiko exchange code, for example cbse.",
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 0)]
	public string ExchangeFilter { get; set; }

	/// <summary>Optional instrument class used to narrow reference lookups.</summary>
	[Display(
		Name = "Instrument class",
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 1)]
	public KaikoInstrumentClasses InstrumentClassFilter { get; set; }

	/// <summary>Use the Kaiko production gRPC Stream for live data.</summary>
	[Display(
		Name = "Streaming",
		Description = "Use Kaiko Stream for live trades, top of book, and OHLCV.",
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 2)]
	[BasicSetting]
	public bool IsStreamingEnabled { get; set; } = true;

	private TimeSpan _requestInterval = TimeSpan.FromMilliseconds(250);

	/// <summary>Minimum delay between REST requests.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
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

	/// <summary>Maximum number of reference instruments returned.</summary>
	[Display(
		Name = "Maximum items",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public int MaximumItems
	{
		get => _maximumItems;
		set => _maximumItems = value is >= 1 and <= 100000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Maximum item count must be between one and 100000.");
	}

	private int _historyLimit = 100000;

	/// <summary>Maximum historical rows returned per subscription.</summary>
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

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Region), Region)
			.Set(nameof(ReferenceEndpoint), ReferenceEndpoint)
			.Set(nameof(MarketEndpoint), MarketEndpoint)
			.Set(nameof(StreamEndpoint), StreamEndpoint)
			.Set(nameof(ExchangeFilter), ExchangeFilter)
			.Set(nameof(InstrumentClassFilter), InstrumentClassFilter)
			.Set(nameof(IsStreamingEnabled), IsStreamingEnabled)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(MaximumItems), MaximumItems)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Region = storage.GetValue(nameof(Region), Region);
		ReferenceEndpoint = storage.GetValue(nameof(ReferenceEndpoint),
			ReferenceEndpoint);
		MarketEndpoint = storage.GetValue(nameof(MarketEndpoint), MarketEndpoint);
		StreamEndpoint = storage.GetValue(nameof(StreamEndpoint), StreamEndpoint);
		ExchangeFilter = storage.GetValue<string>(nameof(ExchangeFilter));
		InstrumentClassFilter = storage.GetValue(nameof(InstrumentClassFilter),
			InstrumentClassFilter);
		IsStreamingEnabled = storage.GetValue(nameof(IsStreamingEnabled),
			IsStreamingEnabled);
		RequestInterval = storage.GetValue(nameof(RequestInterval), RequestInterval);
		MaximumItems = storage.GetValue(nameof(MaximumItems), MaximumItems);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new KaikoMessageAdapter(TransactionIdGenerator)
		{
			Token = Token,
			Region = Region,
			ReferenceEndpoint = ReferenceEndpoint,
			MarketEndpoint = MarketEndpoint,
			StreamEndpoint = StreamEndpoint,
			ExchangeFilter = ExchangeFilter,
			InstrumentClassFilter = InstrumentClassFilter,
			IsStreamingEnabled = IsStreamingEnabled,
			RequestInterval = RequestInterval,
			MaximumItems = MaximumItems,
			HistoryLimit = HistoryLimit,
		};

	/// <inheritdoc />
	public override string ToString() => base.ToString() + $": {Region}";
}
