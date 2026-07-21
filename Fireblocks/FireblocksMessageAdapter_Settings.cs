namespace StockSharp.Fireblocks;

/// <summary>The message adapter for Fireblocks custody workspaces.</summary>
[MediaIcon(Media.MediaNames.fireblocks)]
[Doc("topics/api/connectors/crypto_exchanges/fireblocks.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FireblocksKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.History | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(FireblocksOrderCondition))]
public partial class FireblocksMessageAdapter : MessageAdapter
{
	private FireblocksEnvironments _environment = FireblocksEnvironments.Us;
	private string _apiEndpoint = FireblocksEnvironments.Us.GetApiEndpoint();

	/// <summary>Fireblocks workspace cloud environment.</summary>
	[Display(Name = "Environment",
		Description = "Fireblocks workspace cloud environment.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public FireblocksEnvironments Environment
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

	/// <summary>Fireblocks API user ID.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string ApiKey { get; set; }

	/// <summary>PEM-encoded Fireblocks API private key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Fireblocks REST API root ending in /v1.</summary>
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
		set => _pollingInterval = value >= TimeSpan.FromSeconds(2) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Polling interval must be between two seconds and five minutes.");
	}

	private int _vaultPageSize = 500;

	/// <summary>Vault accounts requested per API page.</summary>
	[Display(Name = "Vault page size",
		Description = "Vault accounts requested per Fireblocks API page.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public int VaultPageSize
	{
		get => _vaultPageSize;
		set => _vaultPageSize = value is >= 1 and <= 500
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Vault page size must be between one and 500.");
	}

	private int _maximumVaultAccounts = 5000;

	/// <summary>Maximum vault accounts loaded from the workspace.</summary>
	[Display(Name = "Maximum vault accounts",
		Description = "Maximum vault accounts loaded from the workspace.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public int MaximumVaultAccounts
	{
		get => _maximumVaultAccounts;
		set => _maximumVaultAccounts = value is >= 1 and <= 50000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Maximum vault account count must be between one and 50000.");
	}

	private int _securityLookupLimit = 5000;

	/// <summary>Maximum assets returned by an unbounded lookup.</summary>
	[Display(Name = "Security lookup limit",
		Description = "Maximum Fireblocks assets returned by one lookup.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public int SecurityLookupLimit
	{
		get => _securityLookupLimit;
		set => _securityLookupLimit = value is >= 1 and <= 50000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Security lookup limit must be between one and 50000.");
	}

	private int _historyLimit = 500;

	/// <summary>Maximum transactions requested from the history endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 500
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Fireblocks history limit must be between one and 500.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Environment), Environment)
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(VaultPageSize), VaultPageSize)
			.Set(nameof(MaximumVaultAccounts), MaximumVaultAccounts)
			.Set(nameof(SecurityLookupLimit), SecurityLookupLimit)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Environment = storage.GetValue(nameof(Environment), Environment);
		ApiKey = storage.GetValue<string>(nameof(ApiKey));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		VaultPageSize = storage.GetValue(nameof(VaultPageSize), VaultPageSize);
		MaximumVaultAccounts = storage.GetValue(nameof(MaximumVaultAccounts),
			MaximumVaultAccounts);
		SecurityLookupLimit = storage.GetValue(nameof(SecurityLookupLimit),
			SecurityLookupLimit);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new FireblocksMessageAdapter(TransactionIdGenerator)
		{
			Environment = Environment,
			ApiKey = ApiKey,
			PrivateKey = PrivateKey,
			ApiEndpoint = ApiEndpoint,
			PollingInterval = PollingInterval,
			VaultPageSize = VaultPageSize,
			MaximumVaultAccounts = MaximumVaultAccounts,
			SecurityLookupLimit = SecurityLookupLimit,
			HistoryLimit = HistoryLimit,
		};

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": {Environment}";
}
