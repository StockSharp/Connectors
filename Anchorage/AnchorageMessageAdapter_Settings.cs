namespace StockSharp.Anchorage;

public partial class AnchorageMessageAdapter
{
	private AnchorageEnvironments _environment = AnchorageEnvironments.Production;
	private string _apiEndpoint =
		AnchorageEnvironments.Production.GetApiEndpoint();
	private string _socketEndpoint =
		AnchorageEnvironments.Production.GetSocketEndpoint();

	/// <summary>Anchorage API environment.</summary>
	[Display(Name = "Environment", GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public AnchorageEnvironments Environment
	{
		get => _environment;
		set
		{
			var previousApi = _environment.GetApiEndpoint();
			var previousSocket = _environment.GetSocketEndpoint();
			if (_apiEndpoint.EqualsIgnoreCase(previousApi))
				_apiEndpoint = value.GetApiEndpoint();
			if (_socketEndpoint.EqualsIgnoreCase(previousSocket))
				_socketEndpoint = value.GetSocketEndpoint();
			_environment = value;
		}
	}

	/// <summary>Anchorage API access key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString ApiKey { get; set; }

	/// <summary>Hexadecimal 32-byte Ed25519 signing seed.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString SigningKey { get; set; }

	/// <summary>Anchorage REST API root ending in /v2.</summary>
	[Display(Name = "REST endpoint", GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	[BasicSetting]
	public string ApiEndpoint
	{
		get => _apiEndpoint;
		set => _apiEndpoint = value;
	}

	/// <summary>Anchorage trading WebSocket endpoint.</summary>
	[Display(Name = "WebSocket endpoint",
		GroupName = LocalizedStrings.AddressesKey, Order = 4)]
	[BasicSetting]
	public string SocketEndpoint
	{
		get => _socketEndpoint;
		set => _socketEndpoint = value;
	}

	/// <summary>Optional account ID or exact name for scoped market data.</summary>
	[Display(Name = "Market data account",
		Description = "Optional trading account ID or exact name for customer-specific market data.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public string MarketDataAccount { get; set; }

	/// <summary>Optional RIA subaccount ID for scoped market data.</summary>
	[Display(Name = "Market data subaccount",
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public string MarketDataSubaccount { get; set; }

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>Private-state reconciliation interval.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(5) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Polling interval must be between five seconds and five minutes.");
	}

	private TimeSpan _marketPollingInterval = TimeSpan.FromSeconds(2);

	/// <summary>REST market-data interval when no signing key is configured.</summary>
	[Display(Name = "Market polling interval",
		GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
	public TimeSpan MarketPollingInterval
	{
		get => _marketPollingInterval;
		set => _marketPollingInterval = value >= TimeSpan.FromSeconds(1) &&
			value <= TimeSpan.FromMinutes(1)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Market polling interval must be between one second and one minute.");
	}

	private int _pageSize = 100;

	/// <summary>REST page size.</summary>
	[Display(Name = "Page size", GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public int PageSize
	{
		get => _pageSize;
		set => _pageSize = value is >= 1 and <= 100
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Page size must be between one and 100.");
	}

	private int _maximumItems = 10000;

	/// <summary>Maximum reference-data items loaded.</summary>
	[Display(Name = "Maximum items",
		GroupName = LocalizedStrings.ConnectionKey, Order = 10)]
	public int MaximumItems
	{
		get => _maximumItems;
		set => _maximumItems = value is >= 1 and <= 100000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Maximum item count must be between one and 100000.");
	}

	private int _historyLimit = 1000;

	/// <summary>Maximum history items per subscription.</summary>
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

	private int _marketDepth = 50;

	/// <summary>Maximum order-book levels published.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDepthKey,
		Description = LocalizedStrings.MarketDepthKey,
		GroupName = LocalizedStrings.MarketDataKey, Order = 0)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Market depth must be between one and 1000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Environment), Environment)
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(SigningKey), SigningKey)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(SocketEndpoint), SocketEndpoint)
			.Set(nameof(MarketDataAccount), MarketDataAccount)
			.Set(nameof(MarketDataSubaccount), MarketDataSubaccount)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(MarketPollingInterval), MarketPollingInterval)
			.Set(nameof(PageSize), PageSize)
			.Set(nameof(MaximumItems), MaximumItems)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Environment = storage.GetValue(nameof(Environment), Environment);
		ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
		SigningKey = storage.GetValue<SecureString>(nameof(SigningKey));
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		SocketEndpoint = storage.GetValue(nameof(SocketEndpoint), SocketEndpoint);
		MarketDataAccount = storage.GetValue<string>(nameof(MarketDataAccount));
		MarketDataSubaccount = storage.GetValue<string>(
			nameof(MarketDataSubaccount));
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		MarketPollingInterval = storage.GetValue(nameof(MarketPollingInterval),
			MarketPollingInterval);
		PageSize = storage.GetValue(nameof(PageSize), PageSize);
		MaximumItems = storage.GetValue(nameof(MaximumItems), MaximumItems);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new AnchorageMessageAdapter(TransactionIdGenerator)
		{
			Environment = Environment,
			ApiKey = ApiKey,
			SigningKey = SigningKey,
			ApiEndpoint = ApiEndpoint,
			SocketEndpoint = SocketEndpoint,
			MarketDataAccount = MarketDataAccount,
			MarketDataSubaccount = MarketDataSubaccount,
			PollingInterval = PollingInterval,
			MarketPollingInterval = MarketPollingInterval,
			PageSize = PageSize,
			MaximumItems = MaximumItems,
			HistoryLimit = HistoryLimit,
			MarketDepth = MarketDepth,
		};

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": {Environment}";
}
