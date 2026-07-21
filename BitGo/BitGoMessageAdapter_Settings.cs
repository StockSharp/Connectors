namespace StockSharp.BitGo;

public partial class BitGoMessageAdapter
{
	private const string _defaultApiEndpoint = "https://app.bitgo.com/";
	private const string _defaultSocketEndpoint =
		"wss://app.bitgo.com/api/prime/trading/v1/ws";

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Go account identifier or exact account name.</summary>
	[Display(Name = "Account",
		Description = "Go account ID or exact name. Optional when only one account is available.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string Account { get; set; }

	/// <summary>BitGo REST API or BitGo Express root.</summary>
	[Display(Name = "REST endpoint",
		Description = "BitGo REST API or BitGo Express root.",
		GroupName = LocalizedStrings.AddressesKey, Order = 2)]
	[BasicSetting]
	public string ApiEndpoint { get; set; } = _defaultApiEndpoint;

	/// <summary>BitGo Prime WebSocket endpoint.</summary>
	[Display(Name = "WebSocket endpoint",
		Description = "BitGo Prime trading WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey, Order = 3)]
	[BasicSetting]
	public string SocketEndpoint { get; set; } = _defaultSocketEndpoint;

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

	private int _historyLimit = 500;

	/// <summary>Maximum orders and fills returned per subscription.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"BitGo history limit must be between one and 5000.");
	}

	/// <summary>Include unsettled funds in the API's available balance.</summary>
	[Display(Name = "Include unsettled",
		Description = "Include unsettled funds in the API available balance calculation.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public bool IsIncludeUnsettledInAvailable { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Account), Account)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(SocketEndpoint), SocketEndpoint)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(IsIncludeUnsettledInAvailable),
				IsIncludeUnsettledInAvailable);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Account = storage.GetValue<string>(nameof(Account));
		ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
		SocketEndpoint = storage.GetValue(nameof(SocketEndpoint), SocketEndpoint);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		IsIncludeUnsettledInAvailable = storage.GetValue(
			nameof(IsIncludeUnsettledInAvailable),
			IsIncludeUnsettledInAvailable);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new BitGoMessageAdapter(TransactionIdGenerator)
		{
			Token = Token,
			Account = Account,
			ApiEndpoint = ApiEndpoint,
			SocketEndpoint = SocketEndpoint,
			PollingInterval = PollingInterval,
			HistoryLimit = HistoryLimit,
			IsIncludeUnsettledInAvailable =
				IsIncludeUnsettledInAvailable,
		};

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (Account.IsEmpty() ? string.Empty : ": " + Account);
}
