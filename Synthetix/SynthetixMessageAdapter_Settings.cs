namespace StockSharp.Synthetix;

public partial class SynthetixMessageAdapter
{
	/// <summary>Synthetix subaccount identifier.</summary>
	[Display(Name = "Subaccount ID", Description =
		"Synthetix trading subaccount identifier.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string SubAccountId { get; set; }

	/// <summary>EVM private key used for EIP-712 authentication.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Optional public REST endpoint override.</summary>
	[Display(Name = "Info REST endpoint", Description =
		"Optional Synthetix public REST endpoint override.",
		GroupName = "Addresses", Order = 0)]
	public string InfoEndpoint { get; set; }

	/// <summary>Optional authenticated REST endpoint override.</summary>
	[Display(Name = "Trade REST endpoint", Description =
		"Optional Synthetix authenticated REST endpoint override.",
		GroupName = "Addresses", Order = 1)]
	public string TradeEndpoint { get; set; }

	/// <summary>Optional public WebSocket endpoint override.</summary>
	[Display(Name = "Info WebSocket endpoint", Description =
		"Optional Synthetix public WebSocket endpoint override.",
		GroupName = "Addresses", Order = 2)]
	public string InfoSocketEndpoint { get; set; }

	/// <summary>Optional authenticated WebSocket endpoint override.</summary>
	[Display(Name = "Trade WebSocket endpoint", Description =
		"Optional Synthetix authenticated WebSocket endpoint override.",
		GroupName = "Addresses", Order = 3)]
	public string TradeSocketEndpoint { get; set; }

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Private REST reconciliation interval.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(2)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Synthetix polling interval cannot be less than two seconds.");
	}

	private int _historyLimit = 1000;

	/// <summary>Maximum history records per request.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Synthetix history limit must be between one and 1000.");
	}

	private int _marketDepth = 50;

	/// <summary>Maximum published live order-book depth.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDepthKey,
		Description = LocalizedStrings.MarketDepthKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 100
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Synthetix live market depth must be between one and 100.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(SubAccountId), SubAccountId)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(InfoEndpoint), InfoEndpoint)
			.Set(nameof(TradeEndpoint), TradeEndpoint)
			.Set(nameof(InfoSocketEndpoint), InfoSocketEndpoint)
			.Set(nameof(TradeSocketEndpoint), TradeSocketEndpoint)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		SubAccountId = storage.GetValue<string>(nameof(SubAccountId));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		InfoEndpoint = storage.GetValue<string>(nameof(InfoEndpoint));
		TradeEndpoint = storage.GetValue<string>(nameof(TradeEndpoint));
		InfoSocketEndpoint = storage.GetValue<string>(nameof(InfoSocketEndpoint));
		TradeSocketEndpoint = storage.GetValue<string>(
			nameof(TradeSocketEndpoint));
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new SynthetixMessageAdapter(TransactionIdGenerator)
		{
			SubAccountId = SubAccountId,
			PrivateKey = PrivateKey,
			InfoEndpoint = InfoEndpoint,
			TradeEndpoint = TradeEndpoint,
			InfoSocketEndpoint = InfoSocketEndpoint,
			TradeSocketEndpoint = TradeSocketEndpoint,
			PollingInterval = PollingInterval,
			HistoryLimit = HistoryLimit,
			MarketDepth = MarketDepth,
		};
}
