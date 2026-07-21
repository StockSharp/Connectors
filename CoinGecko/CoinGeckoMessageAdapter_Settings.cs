namespace StockSharp.CoinGecko;

public partial class CoinGeckoMessageAdapter
{
	private CoinGeckoApiTiers _tier = CoinGeckoApiTiers.Demo;
	private string _apiEndpoint = CoinGeckoApiTiers.Demo.GetApiEndpoint();

	/// <summary>CoinGecko API tier.</summary>
	[Display(Name = "API tier", GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public CoinGeckoApiTiers Tier
	{
		get => _tier;
		set
		{
			var previousEndpoint = _tier.GetApiEndpoint();
			if (_apiEndpoint.EqualsIgnoreCase(previousEndpoint))
				_apiEndpoint = value.GetApiEndpoint();
			_tier = value;
		}
	}

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>CoinGecko REST API root ending in /api/v3.</summary>
	[Display(Name = "REST endpoint", GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string ApiEndpoint
	{
		get => _apiEndpoint;
		set => _apiEndpoint = value;
	}

	/// <summary>CoinGecko Pro WebSocket endpoint.</summary>
	[Display(Name = "WebSocket endpoint",
		GroupName = LocalizedStrings.AddressesKey, Order = 3)]
	[BasicSetting]
	public string SocketEndpoint { get; set; } = "wss://stream.coingecko.com/v1";

	/// <summary>Default quote currency for aggregated coins.</summary>
	[Display(Name = "Quote currency",
		Description = "CoinGecko supported quote-currency ID.",
		GroupName = LocalizedStrings.MarketDataKey, Order = 0)]
	[BasicSetting]
	public string QuoteCurrency { get; set; } = "usd";

	/// <summary>Optional GeckoTerminal network filter for pool lookup.</summary>
	[Display(Name = "On-chain network",
		Description = "Optional GeckoTerminal network ID used for pool search.",
		GroupName = LocalizedStrings.MarketDataKey, Order = 1)]
	public string OnchainNetwork { get; set; }

	/// <summary>Use the paid CoinGecko WebSocket for live subscriptions.</summary>
	[Display(Name = "Streaming",
		Description = "Use CoinGecko Pro WebSocket (Analyst plan or above).",
		GroupName = LocalizedStrings.MarketDataKey, Order = 2)]
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

	/// <summary>Maximum number of securities returned by a lookup.</summary>
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

	private int _poolSearchPages = 3;

	/// <summary>Maximum number of 20-item on-chain search pages.</summary>
	[Display(Name = "Pool search pages",
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public int PoolSearchPages
	{
		get => _poolSearchPages;
		set => _poolSearchPages = value is >= 1 and <= 10
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Pool search pages must be between one and ten.");
	}

	private int _historyLimit = 10000;

	/// <summary>Maximum history items per subscription.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 0)]
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
			.Set(nameof(Tier), Tier)
			.Set(nameof(Token), Token)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(SocketEndpoint), SocketEndpoint)
			.Set(nameof(QuoteCurrency), QuoteCurrency)
			.Set(nameof(OnchainNetwork), OnchainNetwork)
			.Set(nameof(IsStreamingEnabled), IsStreamingEnabled)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(MaximumItems), MaximumItems)
			.Set(nameof(PoolSearchPages), PoolSearchPages)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Tier = storage.GetValue(nameof(Tier), Tier);
		Token = storage.GetValue<SecureString>(nameof(Token));
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		SocketEndpoint = storage.GetValue(nameof(SocketEndpoint), SocketEndpoint);
		QuoteCurrency = storage.GetValue(nameof(QuoteCurrency), QuoteCurrency);
		OnchainNetwork = storage.GetValue<string>(nameof(OnchainNetwork));
		IsStreamingEnabled = storage.GetValue(nameof(IsStreamingEnabled),
			IsStreamingEnabled);
		RequestInterval = storage.GetValue(nameof(RequestInterval), RequestInterval);
		MaximumItems = storage.GetValue(nameof(MaximumItems), MaximumItems);
		PoolSearchPages = storage.GetValue(nameof(PoolSearchPages), PoolSearchPages);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new CoinGeckoMessageAdapter(TransactionIdGenerator)
		{
			Tier = Tier,
			Token = Token,
			ApiEndpoint = ApiEndpoint,
			SocketEndpoint = SocketEndpoint,
			QuoteCurrency = QuoteCurrency,
			OnchainNetwork = OnchainNetwork,
			IsStreamingEnabled = IsStreamingEnabled,
			RequestInterval = RequestInterval,
			MaximumItems = MaximumItems,
			PoolSearchPages = PoolSearchPages,
			HistoryLimit = HistoryLimit,
		};

	/// <inheritdoc />
	public override string ToString() => base.ToString() + $": {Tier}";
}
