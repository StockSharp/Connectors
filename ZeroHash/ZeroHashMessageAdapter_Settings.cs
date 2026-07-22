namespace StockSharp.ZeroHash;

public partial class ZeroHashMessageAdapter : IKeySecretAdapter, IPassphraseAdapter
{
	private const string _defaultApiEndpoint = "https://api.zerohash.com/";

	/// <summary>Zero Hash API key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Base64-encoded Zero Hash API secret.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Zero Hash API passphrase.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PassphraseKey,
		Description = LocalizedStrings.PasswordKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Passphrase { get; set; }

	/// <summary>Fully-qualified Zero Hash CLOB account.</summary>
	[Display(Name = "Account",
		Description = "Fully-qualified CLOB account used for orders and balances.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public string Account { get; set; }

	/// <summary>Fully-qualified Zero Hash CLOB user.</summary>
	[Display(Name = "User",
		Description = "Case-sensitive fully-qualified CLOB user.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public string User { get; set; }

	/// <summary>Zero Hash REST and HTTP-stream API root.</summary>
	[Display(Name = "API endpoint",
		Description = "Zero Hash REST and CLOB HTTP-stream API root.",
		GroupName = LocalizedStrings.AddressesKey, Order = 5)]
	[BasicSetting]
	public string ApiEndpoint { get; set; } = _defaultApiEndpoint;

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>Private-state reconciliation interval.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(2) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Polling interval must be between two seconds and five minutes.");
	}

	private int _historyLimit = 100;

	/// <summary>Maximum orders and executions returned per subscription.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Zero Hash history limit must be between one and 1000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Passphrase), Passphrase)
			.Set(nameof(Account), Account)
			.Set(nameof(User), User)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));
		Account = storage.GetValue<string>(nameof(Account));
		User = storage.GetValue<string>(nameof(User));
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new ZeroHashMessageAdapter(TransactionIdGenerator)
		{
			Key = Key,
			Secret = Secret,
			Passphrase = Passphrase,
			Account = Account,
			User = User,
			ApiEndpoint = ApiEndpoint,
			PollingInterval = PollingInterval,
			HistoryLimit = HistoryLimit,
		};

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (Account.IsEmpty() ? ": Market data" : ": " +
			AccountCode);
}
