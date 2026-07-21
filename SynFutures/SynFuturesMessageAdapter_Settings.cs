namespace StockSharp.SynFutures;

/// <summary>The message adapter for SynFutures V3 perpetual markets on Base.</summary>
[MediaIcon(Media.MediaNames.synfutures)]
[Doc("topics/api/connectors/crypto_exchanges/synfutures.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SynFuturesKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Candles)]
[OrderCondition(typeof(SynFuturesOrderCondition))]
public partial class SynFuturesMessageAdapter : MessageAdapter
{
	private const string _defaultApiEndpoint =
		"https://base-api.synfutures.com";
	private const string _defaultWebSocketEndpoint =
		"wss://base-api.synfutures.com/v4/public/ws";
	private const string _defaultRpcEndpoint = "https://mainnet.base.org";

	/// <summary>Official SynFutures Base API endpoint.</summary>
	[Display(Name = "REST API", GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public string ApiEndpoint { get; set; } = _defaultApiEndpoint;

	/// <summary>Official SynFutures Base WebSocket endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <summary>Base JSON-RPC endpoint.</summary>
	[Display(Name = "JSON-RPC", GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string RpcEndpoint { get; set; } = _defaultRpcEndpoint;

	/// <summary>Optional EVM wallet for read-only account access.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional EVM private key used to sign transactions.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	private decimal _defaultLeverage = 10m;

	/// <summary>Leverage used when no SynFutures condition is supplied.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 5)]
	public decimal DefaultLeverage
	{
		get => _defaultLeverage;
		set => _defaultLeverage = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Default leverage must be positive.");
	}

	private int _slippageBps = 25;

	/// <summary>Maximum market-order slippage in basis points.</summary>
	[Display(Name = "Slippage (bps)",
		GroupName = LocalizedStrings.TransactionKey, Order = 6)]
	public int SlippageBps
	{
		get => _slippageBps;
		set => _slippageBps = value is >= 0 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"SynFutures slippage must be between zero and 5000 basis points.");
	}

	private TimeSpan _orderDeadline = TimeSpan.FromMinutes(10);

	/// <summary>On-chain order deadline offset.</summary>
	[Display(Name = "Order deadline",
		GroupName = LocalizedStrings.TransactionKey, Order = 7)]
	public TimeSpan OrderDeadline
	{
		get => _orderDeadline;
		set => _orderDeadline = value >= TimeSpan.FromMinutes(1) &&
			value <= TimeSpan.FromHours(1)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Order deadline must be between one minute and one hour.");
	}

	private TimeSpan _transactionTimeout = TimeSpan.FromMinutes(2);

	/// <summary>Maximum time to wait for a Base transaction receipt.</summary>
	[Display(Name = "Transaction timeout",
		GroupName = LocalizedStrings.TransactionKey, Order = 8)]
	public TimeSpan TransactionTimeout
	{
		get => _transactionTimeout;
		set => _transactionTimeout = value >= TimeSpan.FromSeconds(10) &&
			value <= TimeSpan.FromMinutes(15)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Transaction timeout must be between 10 seconds and 15 minutes.");
	}

	private TimeSpan _accountRefreshInterval = TimeSpan.FromSeconds(10);

	/// <summary>Fallback polling interval for account data.</summary>
	[Display(Name = "Account refresh",
		GroupName = LocalizedStrings.ConnectionKey, Order = 9)]
	public TimeSpan AccountRefreshInterval
	{
		get => _accountRefreshInterval;
		set => _accountRefreshInterval = value >= TimeSpan.FromSeconds(2) &&
			value <= TimeSpan.FromMinutes(5)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Account refresh must be between two seconds and five minutes.");
	}

	private int _historyLimit = 100;

	/// <summary>Maximum records requested per history call.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 10)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 100
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"SynFutures history limit must be between one and 100.");
	}

	private int _marketDepth = 50;

	/// <summary>Maximum order-book rows sent per side.</summary>
	[Display(Name = "Market depth", GroupName = LocalizedStrings.MarketDepthKey,
		Order = 11)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 500
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"SynFutures market depth must be between one and 500.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(DefaultLeverage), DefaultLeverage)
			.Set(nameof(SlippageBps), SlippageBps)
			.Set(nameof(OrderDeadline), OrderDeadline)
			.Set(nameof(TransactionTimeout), TransactionTimeout)
			.Set(nameof(AccountRefreshInterval), AccountRefreshInterval)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiEndpoint = NormalizeEndpoint(storage.GetValue(nameof(ApiEndpoint),
			ApiEndpoint), false, nameof(ApiEndpoint));
		WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(WebSocketEndpoint), WebSocketEndpoint), true,
			nameof(WebSocketEndpoint));
		RpcEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RpcEndpoint),
			RpcEndpoint), false, nameof(RpcEndpoint));
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		DefaultLeverage = storage.GetValue(nameof(DefaultLeverage),
			DefaultLeverage);
		SlippageBps = storage.GetValue(nameof(SlippageBps), SlippageBps);
		OrderDeadline = storage.GetValue(nameof(OrderDeadline), OrderDeadline);
		TransactionTimeout = storage.GetValue(nameof(TransactionTimeout),
			TransactionTimeout);
		AccountRefreshInterval = storage.GetValue(
			nameof(AccountRefreshInterval), AccountRefreshInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new SynFuturesMessageAdapter(TransactionIdGenerator)
		{
			ApiEndpoint = ApiEndpoint,
			WebSocketEndpoint = WebSocketEndpoint,
			RpcEndpoint = RpcEndpoint,
			WalletAddress = WalletAddress,
			PrivateKey = PrivateKey,
			DefaultLeverage = DefaultLeverage,
			SlippageBps = SlippageBps,
			OrderDeadline = OrderDeadline,
			TransactionTimeout = TransactionTimeout,
			AccountRefreshInterval = AccountRefreshInterval,
			HistoryLimit = HistoryLimit,
			MarketDepth = MarketDepth,
		};

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
				? "SynFutures WebSocket endpoint must use WS or WSS."
				: "SynFutures endpoint must use HTTP or HTTPS.", parameterName);
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (WalletAddress.IsEmpty() && PrivateKey.IsEmpty()
			? ": Public"
			: PrivateKey.IsEmpty() ? ": Read-only" : ": Trading");
}
