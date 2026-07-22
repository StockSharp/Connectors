namespace StockSharp.Tardis;

public partial class TardisMessageAdapter
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	private string _exchange = "binance";

	/// <summary>Tardis exchange identifier.</summary>
	[Display(Name = "Exchange", Description = "Tardis exchange ID.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string Exchange
	{
		get => _exchange;
		set => _exchange = TardisExtensions.NormalizeExchange(value);
	}

	/// <summary>Tardis cloud REST API v1 root.</summary>
	[Display(Name = "REST endpoint", GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string ApiEndpoint { get; set; } = "https://api.tardis.dev/v1/";

	/// <summary>Local Tardis Machine HTTP root.</summary>
	[Display(Name = "Machine HTTP endpoint",
		GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	[BasicSetting]
	public string MachineHttpEndpoint { get; set; } = "http://localhost:8000/";

	/// <summary>Local Tardis Machine WebSocket root.</summary>
	[Display(Name = "Machine WebSocket endpoint",
		GroupName = LocalizedStrings.AddressesKey, Order = 2)]
	[BasicSetting]
	public string MachineSocketEndpoint { get; set; } = "ws://localhost:8001/";

	private TimeSpan _requestInterval = TimeSpan.FromMilliseconds(100);

	/// <summary>Minimum delay between cloud REST requests.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	public TimeSpan RequestInterval
	{
		get => _requestInterval;
		set => _requestInterval = value >= TimeSpan.Zero &&
			value <= TimeSpan.FromMinutes(1)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Request interval must be between zero and one minute.");
	}

	private TimeSpan _streamTimeout = TimeSpan.FromSeconds(30);

	/// <summary>Underlying exchange stream inactivity timeout.</summary>
	[Display(Name = "Stream timeout",
		Description = "Tardis Machine exchange-stream inactivity timeout.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public TimeSpan StreamTimeout
	{
		get => _streamTimeout;
		set => _streamTimeout = value >= TimeSpan.FromSeconds(10) &&
			value <= TimeSpan.FromMinutes(10)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Stream timeout must be between ten seconds and ten minutes.");
	}

	private int _maximumItems = 25000;

	/// <summary>Maximum number of instruments returned by a lookup.</summary>
	[Display(Name = "Maximum items", GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int MaximumItems
	{
		get => _maximumItems;
		set => _maximumItems = value is >= 1 and <= 1000000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Maximum item count must be between one and 1000000.");
	}

	private int _historyLimit = 100000;

	/// <summary>Maximum number of replay messages per subscription.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 10000000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History limit must be between one and 10000000.");
	}

	private TimeSpan _historyLookback = TimeSpan.FromDays(1);

	/// <summary>Default replay range when no start time is specified.</summary>
	[Display(Name = "History lookback",
		Description = "Default replay range when history has no start time.",
		GroupName = LocalizedStrings.HistoryKey, Order = 1)]
	public TimeSpan HistoryLookback
	{
		get => _historyLookback;
		set => _historyLookback = value >= TimeSpan.FromMinutes(1) &&
			value <= TimeSpan.FromDays(31)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"History lookback must be between one minute and 31 days.");
	}

	private TimeSpan _maximumReplaySpan = TimeSpan.FromDays(7);

	/// <summary>Maximum time span accepted by one replay subscription.</summary>
	[Display(Name = "Maximum replay span",
		Description = "Maximum range accepted by one normalized replay.",
		GroupName = LocalizedStrings.HistoryKey, Order = 2)]
	public TimeSpan MaximumReplaySpan
	{
		get => _maximumReplaySpan;
		set => _maximumReplaySpan = value >= TimeSpan.FromMinutes(1) &&
			value <= TimeSpan.FromDays(31)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Maximum replay span must be between one minute and 31 days.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Exchange), Exchange)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(MachineHttpEndpoint), MachineHttpEndpoint)
			.Set(nameof(MachineSocketEndpoint), MachineSocketEndpoint)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(StreamTimeout), StreamTimeout)
			.Set(nameof(MaximumItems), MaximumItems)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(HistoryLookback), HistoryLookback)
			.Set(nameof(MaximumReplaySpan), MaximumReplaySpan);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Exchange = storage.GetValue(nameof(Exchange), Exchange);
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		MachineHttpEndpoint = storage.GetValue(nameof(MachineHttpEndpoint),
			MachineHttpEndpoint);
		MachineSocketEndpoint = storage.GetValue(nameof(MachineSocketEndpoint),
			MachineSocketEndpoint);
		RequestInterval = storage.GetValue(nameof(RequestInterval), RequestInterval);
		StreamTimeout = storage.GetValue(nameof(StreamTimeout), StreamTimeout);
		MaximumItems = storage.GetValue(nameof(MaximumItems), MaximumItems);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		HistoryLookback = storage.GetValue(nameof(HistoryLookback), HistoryLookback);
		MaximumReplaySpan = storage.GetValue(nameof(MaximumReplaySpan),
			MaximumReplaySpan);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new TardisMessageAdapter(TransactionIdGenerator)
		{
			Token = Token,
			Exchange = Exchange,
			ApiEndpoint = ApiEndpoint,
			MachineHttpEndpoint = MachineHttpEndpoint,
			MachineSocketEndpoint = MachineSocketEndpoint,
			RequestInterval = RequestInterval,
			StreamTimeout = StreamTimeout,
			MaximumItems = MaximumItems,
			HistoryLimit = HistoryLimit,
			HistoryLookback = HistoryLookback,
			MaximumReplaySpan = MaximumReplaySpan,
		};
}
