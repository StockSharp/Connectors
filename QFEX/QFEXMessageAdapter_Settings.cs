namespace StockSharp.QFEX;

/// <summary>The message adapter for QFEX perpetual futures.</summary>
[MediaIcon(Media.MediaNames.qfex)]
[Doc("topics/api/connectors/crypto_exchanges/qfex.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QFEXKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(QFEXOrderCondition))]
public partial class QFEXMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.qfex.com/";
	private const string _defaultMarketSocketEndpoint = "wss://mds.qfex.com/";
	private const string _defaultTradeSocketEndpoint = "wss://trade.qfex.com/";

	/// <summary>Official REST endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Public market-data WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string MarketSocketEndpoint { get; set; } =
		_defaultMarketSocketEndpoint;

	/// <summary>Authenticated order-entry WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TransactionsKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string TradeSocketEndpoint { get; set; } = _defaultTradeSocketEndpoint;

	/// <summary>Optional QFEX public API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Optional QFEX API secret.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Optional QFEX subaccount UUID.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string AccountId { get; set; }

	private int _marketDepth = 20;

	/// <summary>Maximum order-book levels sent to StockSharp.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.MarketDepthKey,
		Order = 6)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 20
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"QFEX market depth must be between 1 and 20.");
	}

	private int _historyLimit = 1000;

	/// <summary>Maximum rows requested from QFEX history endpoints.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 7)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"QFEX history limit must be between 1 and 1000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(MarketSocketEndpoint), MarketSocketEndpoint)
			.Set(nameof(TradeSocketEndpoint), TradeSocketEndpoint)
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
			RestEndpoint), false, nameof(RestEndpoint));
		MarketSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(MarketSocketEndpoint), MarketSocketEndpoint), true,
			nameof(MarketSocketEndpoint));
		TradeSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(TradeSocketEndpoint), TradeSocketEndpoint), true,
			nameof(TradeSocketEndpoint));
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		AccountId = storage.GetValue<string>(nameof(AccountId))?.Trim();
		if (!AccountId.IsEmpty() && !Guid.TryParse(AccountId, out _))
			throw new InvalidOperationException(
				"QFEX account ID must be a valid UUID.");
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	private static string NormalizeEndpoint(string endpoint, bool isWebSocket,
		string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = (isWebSocket ? "wss://" : "https://") +
				endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			(isWebSocket
				? uri.Scheme is not ("ws" or "wss")
				: uri.Scheme is not ("http" or "https")))
			throw new ArgumentException(isWebSocket
				? "QFEX WebSocket endpoints must use WS or WSS."
				: "QFEX REST endpoint must use HTTP or HTTPS.", parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (Key.IsEmpty()
			? ": Public"
			: AccountId.IsEmpty() ? ": Private" : $": Account={AccountId}");
}
