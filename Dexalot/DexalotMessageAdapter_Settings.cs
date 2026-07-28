namespace StockSharp.Dexalot;

/// <summary>
/// The message adapter for the Dexalot on-chain central limit order book.
/// </summary>
[MediaIcon(Media.MediaNames.dexalot)]
[Doc("topics/api/connectors/crypto_exchanges/dexalot.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DexalotKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class DexalotMessageAdapter : MessageAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.dexalot.com/privapi";
	private const string _defaultWebSocketEndpoint =
		"wss://api.dexalot.com/api/ws";
	private const string _defaultRpcEndpoint =
		"https://subnets.avax.network/dexalot/mainnet/rpc";

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Dexalot REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Dexalot market-data WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } =
		_defaultWebSocketEndpoint;

	/// <summary>Dexalot L1 HTTP JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string RpcEndpoint { get; set; } = _defaultRpcEndpoint;

	/// <summary>
	/// Optional TradePairs contract override. The current address is discovered
	/// from the REST deployment endpoint when this value is empty.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string TradePairsAddress { get; set; }

	/// <summary>
	/// Optional Portfolio contract override. The current address is discovered
	/// from the REST deployment endpoint when this value is empty.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string PortfolioAddress { get; set; }

	/// <summary>Public wallet address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Private key used to sign Dexalot L1 transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Optional comma- or semicolon-separated trading-pair filter.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public string Pairs { get; set; }

	private int _orderBookDepth = 100;

	/// <summary>Maximum number of aggregated levels per order-book side.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		Description = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public int OrderBookDepth
	{
		get => _orderBookDepth;
		set => _orderBookDepth = value is >= 1 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Dexalot order-book depth must be between 1 and 5000.");
	}

	private TimeSpan _privatePollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Polling interval for balances and private order state.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public TimeSpan PrivatePollingInterval
	{
		get => _privatePollingInterval;
		set => _privatePollingInterval =
			value >= TimeSpan.FromSeconds(2) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Dexalot private polling interval must be between two " +
						"seconds and five minutes.");
	}

	private TimeSpan _receiptTimeout = TimeSpan.FromMinutes(2);

	/// <summary>Maximum time to wait for a Dexalot L1 transaction receipt.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		Description = LocalizedStrings.TimeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public TimeSpan ReceiptTimeout
	{
		get => _receiptTimeout;
		set => _receiptTimeout =
			value >= TimeSpan.FromSeconds(15) &&
			value <= TimeSpan.FromMinutes(15)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Dexalot receipt timeout must be between 15 seconds and " +
						"15 minutes.");
	}

	/// <summary>Default self-trade prevention behavior.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TypeKey,
		Description = LocalizedStrings.TypeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public DexalotSelfTradePrevention SelfTradePrevention { get; set; } =
		DexalotSelfTradePrevention.CancelTaker;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(TradePairsAddress), TradePairsAddress)
			.Set(nameof(PortfolioAddress), PortfolioAddress)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(Pairs), Pairs)
			.Set(nameof(OrderBookDepth), OrderBookDepth)
			.Set(nameof(PrivatePollingInterval), PrivatePollingInterval)
			.Set(nameof(ReceiptTimeout), ReceiptTimeout)
			.Set(nameof(SelfTradePrevention), SelfTradePrevention);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		RestEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(RestEndpoint), RestEndpoint), "https") ??
			_defaultRestEndpoint;
		WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(WebSocketEndpoint), WebSocketEndpoint), "wss") ??
			_defaultWebSocketEndpoint;
		RpcEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(RpcEndpoint), RpcEndpoint), "https") ??
			_defaultRpcEndpoint;
		TradePairsAddress = NormalizeAddress(storage.GetValue<string>(
			nameof(TradePairsAddress)));
		PortfolioAddress = NormalizeAddress(storage.GetValue<string>(
			nameof(PortfolioAddress)));
		WalletAddress = NormalizeAddress(storage.GetValue<string>(
			nameof(WalletAddress)));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		Pairs = storage.GetValue<string>(nameof(Pairs));
		OrderBookDepth = storage.GetValue(nameof(OrderBookDepth),
			OrderBookDepth);
		PrivatePollingInterval = storage.GetValue(
			nameof(PrivatePollingInterval), PrivatePollingInterval);
		ReceiptTimeout = storage.GetValue(nameof(ReceiptTimeout),
			ReceiptTimeout);
		SelfTradePrevention = storage.GetValue(nameof(SelfTradePrevention),
			SelfTradePrevention);
		if (!System.Enum.IsDefined(SelfTradePrevention))
			throw new InvalidDataException(
				$"Unknown Dexalot self-trade prevention mode " +
					$"'{SelfTradePrevention}'.");
	}

	private static string NormalizeEndpoint(string endpoint,
		string defaultScheme)
	{
		endpoint = endpoint?.Trim();
		if (endpoint.IsEmpty())
			return null;
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{defaultScheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	private static string NormalizeAddress(string address)
		=> address.IsEmpty() ? null : address.NormalizeAddress();

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Wallet={WalletAddress}";
}
