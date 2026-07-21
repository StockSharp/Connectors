namespace StockSharp.Copper;

public partial class CopperMessageAdapter
{
	private CopperEnvironments _environment = CopperEnvironments.Production;
	private string _apiEndpoint =
		CopperEnvironments.Production.GetApiEndpoint();

	/// <summary>Copper Platform environment.</summary>
	[Display(Name = "Environment",
		Description = "Copper Platform environment.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public CopperEnvironments Environment
	{
		get => _environment;
		set
		{
			var previous = _environment.GetApiEndpoint();
			if (_apiEndpoint.EqualsIgnoreCase(previous))
				_apiEndpoint = value.GetApiEndpoint();
			_environment = value;
		}
	}

	/// <summary>Copper API key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string ApiKey { get; set; }

	/// <summary>Copper HMAC API secret.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString ApiSecret { get; set; }

	/// <summary>Copper Platform REST root ending in /platform.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 3)]
	[BasicSetting]
	public string ApiEndpoint
	{
		get => _apiEndpoint;
		set => _apiEndpoint = value;
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>Private-state reconciliation interval.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(5) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Polling interval must be between five seconds and five minutes.");
	}

	private int _pageSize = 1000;

	/// <summary>Maximum objects requested per REST page.</summary>
	[Display(Name = "Page size",
		Description = "Maximum objects requested per Copper REST page.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public int PageSize
	{
		get => _pageSize;
		set => _pageSize = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Page size must be between one and 1000.");
	}

	private int _maximumItems = 10000;

	/// <summary>Maximum portfolios or wallets loaded from Copper.</summary>
	[Display(Name = "Maximum items",
		Description = "Maximum portfolios or wallets loaded from Copper.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public int MaximumItems
	{
		get => _maximumItems;
		set => _maximumItems = value is >= 1 and <= 100000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Maximum item count must be between one and 100000.");
	}

	private int _historyLimit = 1000;

	/// <summary>Maximum orders loaded for a history subscription.</summary>
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
				"Copper history limit must be between one and 10000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Environment), Environment)
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(ApiSecret), ApiSecret)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(PageSize), PageSize)
			.Set(nameof(MaximumItems), MaximumItems)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Environment = storage.GetValue(nameof(Environment), Environment);
		ApiKey = storage.GetValue<string>(nameof(ApiKey));
		ApiSecret = storage.GetValue<SecureString>(nameof(ApiSecret));
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		PageSize = storage.GetValue(nameof(PageSize), PageSize);
		MaximumItems = storage.GetValue(nameof(MaximumItems), MaximumItems);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new CopperMessageAdapter(TransactionIdGenerator)
		{
			Environment = Environment,
			ApiKey = ApiKey,
			ApiSecret = ApiSecret,
			ApiEndpoint = ApiEndpoint,
			PollingInterval = PollingInterval,
			PageSize = PageSize,
			MaximumItems = MaximumItems,
			HistoryLimit = HistoryLimit,
		};

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": {Environment}";
}
