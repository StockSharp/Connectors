namespace StockSharp.FalconX;

public partial class FalconXMessageAdapter : IKeySecretAdapter, IPassphraseAdapter
{
	private const string _defaultApiEndpoint = "https://api.falconx.io/";
	private const string _defaultPriceSocketEndpoint =
		"wss://stream.falconx.io/price.tickers";
	private const string _defaultOrderSocketEndpoint =
		"wss://order.falconx.io/order";

	/// <summary>FalconX API key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Base64-encoded FalconX API secret.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>FalconX API passphrase.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PassphraseKey,
		Description = LocalizedStrings.PasswordKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Passphrase { get; set; }

	/// <summary>FalconX REST API root.</summary>
	[Display(Name = "REST endpoint",
		Description = "FalconX REST API root.",
		GroupName = LocalizedStrings.AddressesKey, Order = 3)]
	[BasicSetting]
	public string ApiEndpoint { get; set; } = _defaultApiEndpoint;

	/// <summary>FalconX price-stream WebSocket endpoint.</summary>
	[Display(Name = "Price WebSocket",
		Description = "FalconX price-stream WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey, Order = 4)]
	[BasicSetting]
	public string PriceSocketEndpoint { get; set; } =
		_defaultPriceSocketEndpoint;

	/// <summary>FalconX order WebSocket endpoint.</summary>
	[Display(Name = "Order WebSocket",
		Description = "FalconX order-entry WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey, Order = 5)]
	[BasicSetting]
	public string OrderSocketEndpoint { get; set; } =
		_defaultOrderSocketEndpoint;

	private decimal[] _quoteLevels = [1m];

	/// <summary>Base-token quantities requested from the FalconX price stream.</summary>
	[Display(Name = "Quote levels",
		Description = "Positive base-token quantities used for streamed FalconX prices.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public decimal[] QuoteLevels
	{
		get => [.. _quoteLevels];
		set
		{
			if (value is null || value.Length == 0 || value.Length > 50 ||
				value.Any(static level => level <= 0))
				throw new ArgumentOutOfRangeException(nameof(value),
					"Quote levels must contain 1-50 positive quantities.");
			_quoteLevels = [.. value.Distinct().OrderBy(static level => level)];
		}
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>REST account and order reconciliation interval.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
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

	/// <summary>Maximum order-history records returned per subscription.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 100
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"FalconX history limit must be between one and 100.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Passphrase), Passphrase)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(PriceSocketEndpoint), PriceSocketEndpoint)
			.Set(nameof(OrderSocketEndpoint), OrderSocketEndpoint)
			.Set(nameof(QuoteLevels), QuoteLevels)
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
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		PriceSocketEndpoint = storage.GetValue(nameof(PriceSocketEndpoint),
			PriceSocketEndpoint);
		OrderSocketEndpoint = storage.GetValue(nameof(OrderSocketEndpoint),
			OrderSocketEndpoint);
		QuoteLevels = storage.GetValue(nameof(QuoteLevels), QuoteLevels);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new FalconXMessageAdapter(TransactionIdGenerator)
		{
			Key = Key,
			Secret = Secret,
			Passphrase = Passphrase,
			ApiEndpoint = ApiEndpoint,
			PriceSocketEndpoint = PriceSocketEndpoint,
			OrderSocketEndpoint = OrderSocketEndpoint,
			QuoteLevels = QuoteLevels,
			PollingInterval = PollingInterval,
			HistoryLimit = HistoryLimit,
		};

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (Key.IsEmpty() ? ": Unconfigured" : ": Live");
}
