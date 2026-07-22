namespace StockSharp.Paxos;

public partial class PaxosMessageAdapter : IKeySecretAdapter
{
	private const string _defaultScopes =
		"funding:read_profile exchange:read_order exchange:write_order " +
		"transfer:read_transfer transfer:write_crypto_withdrawal " +
		"transfer:write_internal_transfer transfer:write_paxos_transfer " +
		"conversion:read_conversion_stablecoin " +
		"conversion:write_conversion_stablecoin";
	private PaxosEnvironments _environment = PaxosEnvironments.Production;
	private string _apiEndpoint =
		PaxosEnvironments.Production.GetApiEndpoint();
	private string _oauthEndpoint =
		PaxosEnvironments.Production.GetOAuthEndpoint();
	private string _socketEndpoint =
		PaxosEnvironments.Production.GetSocketEndpoint();

	/// <summary>Paxos API environment.</summary>
	[Display(
		Name = "Environment",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public PaxosEnvironments Environment
	{
		get => _environment;
		set
		{
			var previousApi = _environment.GetApiEndpoint();
			var previousOAuth = _environment.GetOAuthEndpoint();
			var previousSocket = _environment.GetSocketEndpoint();
			if (_apiEndpoint.EqualsIgnoreCase(previousApi))
				_apiEndpoint = value.GetApiEndpoint();
			if (_oauthEndpoint.EqualsIgnoreCase(previousOAuth))
				_oauthEndpoint = value.GetOAuthEndpoint();
			if (_socketEndpoint.EqualsIgnoreCase(previousSocket))
				_socketEndpoint = value.GetSocketEndpoint();
			_environment = value;
		}
	}

	/// <summary>Paxos OAuth Client ID.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Paxos OAuth Client Secret.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Space-delimited OAuth scopes.</summary>
	[Display(
		Name = "OAuth scopes",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string Scopes { get; set; } = _defaultScopes;

	/// <summary>Paxos REST API root ending in /v2.</summary>
	[Display(
		Name = "REST endpoint",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	[BasicSetting]
	public string ApiEndpoint
	{
		get => _apiEndpoint;
		set => _apiEndpoint = value;
	}

	/// <summary>Paxos OAuth token endpoint.</summary>
	[Display(
		Name = "OAuth endpoint",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	[BasicSetting]
	public string OAuthEndpoint
	{
		get => _oauthEndpoint;
		set => _oauthEndpoint = value;
	}

	/// <summary>Paxos public WebSocket root.</summary>
	[Display(
		Name = "WebSocket endpoint",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	[BasicSetting]
	public string SocketEndpoint
	{
		get => _socketEndpoint;
		set => _socketEndpoint = value;
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>Private-state reconciliation interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(5) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Polling interval must be between five seconds and five minutes.");
	}

	private int _pageSize = 100;

	/// <summary>REST page size.</summary>
	[Display(
		Name = "Page size",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public int PageSize
	{
		get => _pageSize;
		set => _pageSize = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Page size must be between one and 1000.");
	}

	private int _maximumItems = 10000;

	/// <summary>Maximum reference-data items loaded.</summary>
	[Display(
		Name = "Maximum items",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
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
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 10000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History limit must be between one and 10000.");
	}

	private int _marketDepth = 100;

	/// <summary>Maximum published order-book levels.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDepthKey,
		Description = LocalizedStrings.MarketDepthKey,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 0)]
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
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Scopes), Scopes)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(OAuthEndpoint), OAuthEndpoint)
			.Set(nameof(SocketEndpoint), SocketEndpoint)
			.Set(nameof(PollingInterval), PollingInterval)
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
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Scopes = storage.GetValue(nameof(Scopes), Scopes);
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		OAuthEndpoint = storage.GetValue(nameof(OAuthEndpoint), OAuthEndpoint);
		SocketEndpoint = storage.GetValue(nameof(SocketEndpoint), SocketEndpoint);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		PageSize = storage.GetValue(nameof(PageSize), PageSize);
		MaximumItems = storage.GetValue(nameof(MaximumItems), MaximumItems);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new PaxosMessageAdapter(TransactionIdGenerator)
		{
			Environment = Environment,
			Key = Key,
			Secret = Secret,
			Scopes = Scopes,
			ApiEndpoint = ApiEndpoint,
			OAuthEndpoint = OAuthEndpoint,
			SocketEndpoint = SocketEndpoint,
			PollingInterval = PollingInterval,
			PageSize = PageSize,
			MaximumItems = MaximumItems,
			HistoryLimit = HistoryLimit,
			MarketDepth = MarketDepth,
		};

	/// <inheritdoc />
	public override string ToString() => base.ToString() + $": {Environment}";
}
