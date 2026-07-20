namespace StockSharp.Reya;

public partial class ReyaMessageAdapter
{
	private const string _defaultRestEndpoint = "https://api.reya.xyz/v2";
	private const string _defaultWebSocketEndpoint = "wss://ws.reya.xyz/";
	private const string _defaultGatewayAddress =
		"0xfc8c96be87da63cecddbf54abfa7b13ee8044739";

	/// <summary>Owner wallet used for account queries and streams.</summary>
	[Display(Name = "Owner wallet",
		Description = "Reya account owner EVM wallet address.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional Reya account ID override.</summary>
	[Display(Name = "Reya account ID",
		Description = "Optional account ID; discovered from the owner wallet when empty.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <summary>EVM signer private key used for trading.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>REST API v2 endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>WebSocket API v2 endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	private long _chainId = 1729;

	/// <summary>Reya EVM chain identifier.</summary>
	[Display(Name = "Chain ID", GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public long ChainId
	{
		get => _chainId;
		set => _chainId = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Reya chain ID must be positive.");
	}

	/// <summary>Orders Gateway verifying contract.</summary>
	[Display(Name = "Orders Gateway",
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public string OrdersGatewayAddress { get; set; } = _defaultGatewayAddress;

	private long _exchangeId = 2;

	/// <summary>Reya DEX exchange identifier.</summary>
	[Display(Name = "Exchange ID",
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public long ExchangeId
	{
		get => _exchangeId;
		set => _exchangeId = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Reya exchange ID must be positive.");
	}

	private string _poolAccountId = "2";

	/// <summary>Reya perpetual pool counterparty account ID.</summary>
	[Display(Name = "Pool account ID",
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public string PoolAccountId
	{
		get => _poolAccountId;
		set
		{
			value = value.ThrowIfEmpty(nameof(value)).Trim();
			if (value.ParseReyaInteger("pool account ID") <= 0)
				throw new ArgumentOutOfRangeException(nameof(value), value,
					"Reya pool account ID must be positive.");
			_poolAccountId = value;
		}
	}

	private decimal _marketOrderSlippage = 1m;

	/// <summary>Market-order protection in percent.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 0)]
	public decimal MarketOrderSlippage
	{
		get => _marketOrderSlippage;
		set => _marketOrderSlippage = value is > 0 and <= 25
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Reya market-order slippage must be greater than zero and at most 25%.");
	}

	private int _marketDepth = 100;

	/// <summary>Maximum order-book levels sent to StockSharp.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.MarketDepthKey, Order = 0)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 500
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Reya market depth must be between 1 and 500.");
	}

	private int _historyLimit = 100;

	/// <summary>Maximum execution rows requested from history endpoints.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 100
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Reya execution history limit must be between 1 and 100.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(ChainId), ChainId)
			.Set(nameof(OrdersGatewayAddress), OrdersGatewayAddress)
			.Set(nameof(ExchangeId), ExchangeId)
			.Set(nameof(PoolAccountId), PoolAccountId)
			.Set(nameof(MarketOrderSlippage), MarketOrderSlippage)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress))?.Trim();
		AccountId = storage.GetValue<string>(nameof(AccountId))?.Trim();
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
			RestEndpoint), false, nameof(RestEndpoint));
		WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(WebSocketEndpoint), WebSocketEndpoint), true,
			nameof(WebSocketEndpoint));
		ChainId = storage.GetValue(nameof(ChainId), ChainId);
		OrdersGatewayAddress = storage.GetValue(nameof(OrdersGatewayAddress),
			OrdersGatewayAddress)?.Trim();
		ExchangeId = storage.GetValue(nameof(ExchangeId), ExchangeId);
		PoolAccountId = storage.GetValue(nameof(PoolAccountId), PoolAccountId);
		MarketOrderSlippage = storage.GetValue(nameof(MarketOrderSlippage),
			MarketOrderSlippage);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	private static string NormalizeEndpoint(string endpoint, bool isWebSocket,
		string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim().TrimEnd('/');
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = (isWebSocket ? "wss://" : "https://") +
				endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			(isWebSocket
				? uri.Scheme is not ("ws" or "wss")
				: uri.Scheme is not ("http" or "https")) ||
			!uri.UserInfo.IsEmpty() ||
			(uri.Scheme is "http" or "ws" && !uri.IsLoopback))
			throw new ArgumentException(isWebSocket
				? "Reya WebSocket endpoint must use WSS, except locally."
				: "Reya REST endpoint must use HTTPS, except locally.",
				parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (WalletAddress.IsEmpty() && PrivateKey.IsEmpty()
			? ": Public"
			: PrivateKey.IsEmpty() ? ": Read-only" : ": Trading");
}
