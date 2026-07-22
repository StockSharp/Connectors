namespace StockSharp.Aevo;

/// <summary>Aevo environments.</summary>
[DataContract]
public enum AevoEnvironments
{
	/// <summary>Mainnet.</summary>
	[EnumMember]
	[Display(
		Name = "Mainnet")]
	Mainnet,

	/// <summary>Testnet.</summary>
	[EnumMember]
	[Display(
		Name = "Testnet")]
	Testnet,
}

public partial class AevoMessageAdapter : IKeySecretAdapter
{
	/// <summary>Aevo environment.</summary>
	[Display(Name = "Environment", Description = "Aevo API environment.",
		GroupName = "Connection", Order = 0)]
	[BasicSetting]
	public AevoEnvironments Environment { get; set; }

	/// <summary>API key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>API secret.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Aevo account wallet address.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Private EVM signing key registered with Aevo.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public SecureString SigningKey { get; set; }

	/// <summary>Optional REST endpoint override.</summary>
	[Display(Name = "REST endpoint", Description =
		"Optional Aevo REST endpoint override.", GroupName = "Addresses",
		Order = 0)]
	public string RestEndpoint { get; set; }

	/// <summary>Optional WebSocket endpoint override.</summary>
	[Display(Name = "WebSocket endpoint", Description =
		"Optional Aevo WebSocket endpoint override.", GroupName = "Addresses",
		Order = 1)]
	public string SocketEndpoint { get; set; }

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Private REST polling interval.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(2)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Aevo polling interval cannot be less than two seconds.");
	}

	private int _historyLimit = 50;

	/// <summary>Maximum history records per request.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 50
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Aevo history limit must be between one and 50.");
	}

	private int _marketDepth = 100;

	/// <summary>Maximum published order-book depth.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDepthKey,
		Description = LocalizedStrings.MarketDepthKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Aevo market depth must be between one and 1000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Environment), Environment)
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(SigningKey), SigningKey)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(SocketEndpoint), SocketEndpoint)
			.Set(nameof(PollingInterval), PollingInterval)
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
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		SigningKey = storage.GetValue<SecureString>(nameof(SigningKey));
		RestEndpoint = storage.GetValue<string>(nameof(RestEndpoint));
		SocketEndpoint = storage.GetValue<string>(nameof(SocketEndpoint));
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new AevoMessageAdapter(TransactionIdGenerator)
		{
			Environment = Environment,
			Key = Key,
			Secret = Secret,
			WalletAddress = WalletAddress,
			SigningKey = SigningKey,
			RestEndpoint = RestEndpoint,
			SocketEndpoint = SocketEndpoint,
			PollingInterval = PollingInterval,
			HistoryLimit = HistoryLimit,
			MarketDepth = MarketDepth,
		};
}
