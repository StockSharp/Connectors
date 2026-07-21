namespace StockSharp.CoinMarketCap;

public partial class CoinMarketCapMessageAdapter
{
	private CoinMarketCapAccessModes _accessMode =
		CoinMarketCapAccessModes.Keyless;
	private string _apiEndpoint =
		CoinMarketCapAccessModes.Keyless.GetApiEndpoint();

	/// <summary>CoinMarketCap API access mode.</summary>
	[Display(Name = "Access mode", GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public CoinMarketCapAccessModes AccessMode
	{
		get => _accessMode;
		set
		{
			var previousEndpoint = _accessMode.GetApiEndpoint();
			if (_apiEndpoint.EqualsIgnoreCase(previousEndpoint))
				_apiEndpoint = value.GetApiEndpoint();
			_accessMode = value;
		}
	}

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>CoinMarketCap REST API root.</summary>
	[Display(Name = "REST endpoint", GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string ApiEndpoint
	{
		get => _apiEndpoint;
		set => _apiEndpoint = value;
	}

	/// <summary>CoinMarketCap WebSocket endpoint.</summary>
	[Display(Name = "WebSocket endpoint",
		GroupName = LocalizedStrings.AddressesKey, Order = 3)]
	[BasicSetting]
	public string SocketEndpoint { get; set; } =
		"wss://pro-stream.coinmarketcap.com/v1";

	/// <summary>Default conversion currency.</summary>
	[Display(Name = "Quote currency",
		Description = "Currency symbol used by REST quote conversion.",
		GroupName = LocalizedStrings.MarketDataKey, Order = 0)]
	[BasicSetting]
	public string QuoteCurrency { get; set; } = "USD";

	/// <summary>Use the paid CoinMarketCap WebSocket for live prices.</summary>
	[Display(Name = "Streaming",
		Description = "Use the CoinMarketCap WebSocket beta (Startup plan or above).",
		GroupName = LocalizedStrings.MarketDataKey, Order = 1)]
	[BasicSetting]
	public bool IsStreamingEnabled { get; set; } = true;

	private TimeSpan _requestInterval = TimeSpan.FromSeconds(2);

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

	/// <summary>Maximum number of securities cached and returned.</summary>
	[Display(Name = "Maximum items", GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int MaximumItems
	{
		get => _maximumItems;
		set => _maximumItems = value is >= 1 and <= 100000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Maximum item count must be between one and 100000.");
	}

	private int _historyLimit = 10000;

	/// <summary>Maximum number of historical candles per request.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 10000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History limit must be between one and 10000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AccessMode), AccessMode)
			.Set(nameof(Token), Token)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(SocketEndpoint), SocketEndpoint)
			.Set(nameof(QuoteCurrency), QuoteCurrency)
			.Set(nameof(IsStreamingEnabled), IsStreamingEnabled)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(MaximumItems), MaximumItems)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AccessMode = storage.GetValue(nameof(AccessMode), AccessMode);
		Token = storage.GetValue<SecureString>(nameof(Token));
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		SocketEndpoint = storage.GetValue(nameof(SocketEndpoint), SocketEndpoint);
		QuoteCurrency = storage.GetValue(nameof(QuoteCurrency), QuoteCurrency);
		IsStreamingEnabled = storage.GetValue(nameof(IsStreamingEnabled),
			IsStreamingEnabled);
		RequestInterval = storage.GetValue(nameof(RequestInterval), RequestInterval);
		MaximumItems = storage.GetValue(nameof(MaximumItems), MaximumItems);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new CoinMarketCapMessageAdapter(TransactionIdGenerator)
		{
			AccessMode = AccessMode,
			Token = Token,
			ApiEndpoint = ApiEndpoint,
			SocketEndpoint = SocketEndpoint,
			QuoteCurrency = QuoteCurrency,
			IsStreamingEnabled = IsStreamingEnabled,
			RequestInterval = RequestInterval,
			MaximumItems = MaximumItems,
			HistoryLimit = HistoryLimit,
		};

	/// <inheritdoc />
	public override string ToString() => base.ToString() + $": {AccessMode}";
}
