namespace StockSharp.Polymarket;

public partial class PolymarketMessageAdapter : IKeySecretAdapter, IPassphraseAdapter
{
	private const string _defaultClobEndpoint = "https://clob.polymarket.com";
	private const string _defaultDataEndpoint =
		"https://data-api.polymarket.com";
	private const string _defaultMarketSocketEndpoint =
		"wss://ws-subscriptions-clob.polymarket.com/ws/market";
	private const string _defaultUserSocketEndpoint =
		"wss://ws-subscriptions-clob.polymarket.com/ws/user";

	/// <summary>Polymarket CLOB REST endpoint.</summary>
	[Display(
		Name = "CLOB endpoint",
		Description = "Polymarket CLOB REST endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public string ClobEndpoint { get; set; } = _defaultClobEndpoint;

	/// <summary>Polymarket Data API endpoint.</summary>
	[Display(
		Name = "Data endpoint",
		Description = "Polymarket positions Data API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string DataEndpoint { get; set; } = _defaultDataEndpoint;

	/// <summary>Polymarket market WebSocket endpoint.</summary>
	[Display(
		Name = "Market WebSocket",
		Description = "Polymarket public market WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string MarketSocketEndpoint { get; set; } =
		_defaultMarketSocketEndpoint;

	/// <summary>Polymarket user WebSocket endpoint.</summary>
	[Display(
		Name = "User WebSocket",
		Description = "Polymarket authenticated user WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string UserSocketEndpoint { get; set; } = _defaultUserSocketEndpoint;

	/// <summary>Polymarket CLOB API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Polymarket CLOB API secret.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Polymarket CLOB API passphrase.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PassphraseKey,
		Description = LocalizedStrings.PassphraseKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Passphrase { get; set; }

	/// <summary>EOA address used to authenticate API requests.</summary>
	[Display(
		Name = "Signer address",
		Description = "EOA address associated with the API credentials.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string SignerAddress { get; set; }

	/// <summary>Wallet holding collateral and conditional tokens.</summary>
	[Display(
		Name = "Funder address",
		Description = "Polymarket proxy, Safe or deposit wallet holding funds.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public string FunderAddress { get; set; }

	/// <summary>EOA private key used to sign orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Polymarket wallet signature type.</summary>
	[Display(
		Name = "Signature type",
		Description = "Polymarket wallet and order signature type.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public PolymarketSignatureTypes SignatureType { get; set; }

	/// <summary>Optional registered Polymarket builder code.</summary>
	[Display(
		Name = "Builder code",
		Description = "Optional registered bytes32 builder code.",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 0)]
	public string BuilderCode { get; set; }

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Private account reconciliation interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(2) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Polymarket polling interval must be between two seconds " +
					"and five minutes.");
	}

	private int _historyLimit = 1000;

	/// <summary>Maximum private history records per request.</summary>
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
				"Polymarket history limit must be between one and 10000.");
	}

	private int _marketDepth = 100;

	/// <summary>Maximum published order-book depth.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDepthKey,
		Description = LocalizedStrings.MarketDepthKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Polymarket market depth must be between one and 1000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ClobEndpoint), ClobEndpoint)
			.Set(nameof(DataEndpoint), DataEndpoint)
			.Set(nameof(MarketSocketEndpoint), MarketSocketEndpoint)
			.Set(nameof(UserSocketEndpoint), UserSocketEndpoint)
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Passphrase), Passphrase)
			.Set(nameof(SignerAddress), SignerAddress)
			.Set(nameof(FunderAddress), FunderAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(SignatureType), SignatureType)
			.Set(nameof(BuilderCode), BuilderCode)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ClobEndpoint = storage.GetValue(nameof(ClobEndpoint), ClobEndpoint);
		DataEndpoint = storage.GetValue(nameof(DataEndpoint), DataEndpoint);
		MarketSocketEndpoint = storage.GetValue(nameof(MarketSocketEndpoint),
			MarketSocketEndpoint);
		UserSocketEndpoint = storage.GetValue(nameof(UserSocketEndpoint),
			UserSocketEndpoint);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));
		SignerAddress = storage.GetValue<string>(nameof(SignerAddress));
		FunderAddress = storage.GetValue<string>(nameof(FunderAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		SignatureType = storage.GetValue(nameof(SignatureType), SignatureType);
		BuilderCode = storage.GetValue<string>(nameof(BuilderCode));
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new PolymarketMessageAdapter(TransactionIdGenerator)
		{
			ClobEndpoint = ClobEndpoint,
			DataEndpoint = DataEndpoint,
			MarketSocketEndpoint = MarketSocketEndpoint,
			UserSocketEndpoint = UserSocketEndpoint,
			Key = Key,
			Secret = Secret,
			Passphrase = Passphrase,
			SignerAddress = SignerAddress,
			FunderAddress = FunderAddress,
			PrivateKey = PrivateKey,
			SignatureType = SignatureType,
			BuilderCode = BuilderCode,
			PollingInterval = PollingInterval,
			HistoryLimit = HistoryLimit,
			MarketDepth = MarketDepth,
		};

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (Key.IsEmpty()
			? ": Public"
			: PrivateKey.IsEmpty() ? ": Read-only" : ": Trading");
}
