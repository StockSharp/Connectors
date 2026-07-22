namespace StockSharp.Extended;

/// <summary>The message adapter for Extended spot and perpetual markets.</summary>
[MediaIcon(Media.MediaNames.extended)]
[Doc("topics/api/connectors/crypto_exchanges/extended.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ExtendedKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(ExtendedOrderCondition))]
public partial class ExtendedMessageAdapter : MessageAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.starknet.extended.exchange/api/v1/";
	private const string _defaultWebSocketEndpoint =
		"wss://api.starknet.extended.exchange/stream.extended.exchange/v2/rpc";

	/// <summary>Official REST endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Official WebSocket RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <summary>Optional Extended API key for account access.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Optional Stark private key used to sign trading orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	private TimeSpan _orderExpiry = TimeSpan.FromHours(1);

	/// <summary>Lifetime of a newly submitted order.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExpirationKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 4)]
	public TimeSpan OrderExpiry
	{
		get => _orderExpiry;
		set => _orderExpiry = value >= TimeSpan.FromMinutes(1) &&
			value <= TimeSpan.FromDays(30)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Extended order expiry must be between one minute and 30 days.");
	}

	private decimal _marketOrderSlippage = 0.75m;

	/// <summary>Market-order slippage tolerance in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 5)]
	public decimal MarketOrderSlippage
	{
		get => _marketOrderSlippage;
		set => _marketOrderSlippage = value is > 0 and <= 25
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Extended market-order slippage must be greater than zero and at most 25%.");
	}

	private int _marketDepth = 100;

	/// <summary>Maximum order-book levels sent to StockSharp.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.MarketDepthKey,
		Order = 6)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Extended market depth must be between 1 and 1000.");
	}

	private int _historyLimit = 1000;

	/// <summary>Maximum rows requested from history endpoints.</summary>
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
				"Extended history limit must be between 1 and 1000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(Key), Key)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(OrderExpiry), OrderExpiry)
			.Set(nameof(MarketOrderSlippage), MarketOrderSlippage)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
			RestEndpoint), false, nameof(RestEndpoint));
		WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(WebSocketEndpoint), WebSocketEndpoint), true,
			nameof(WebSocketEndpoint));
		Key = storage.GetValue<SecureString>(nameof(Key));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		OrderExpiry = storage.GetValue(nameof(OrderExpiry), OrderExpiry);
		MarketOrderSlippage = storage.GetValue(nameof(MarketOrderSlippage),
			MarketOrderSlippage);
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
				? "Extended WebSocket endpoint must use WS or WSS."
				: "Extended REST endpoint must use HTTP or HTTPS.",
				parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (Key.IsEmpty()
			? ": Public"
			: PrivateKey.IsEmpty() ? ": Read-only" : ": Trading");
}
