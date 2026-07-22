namespace StockSharp.Nado;

/// <summary>The message adapter for Nado spot and perpetual markets.</summary>
[MediaIcon(Media.MediaNames.nado)]
[Doc("topics/api/connectors/crypto_exchanges/nado.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.NadoKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(NadoOrderCondition))]
public partial class NadoMessageAdapter : MessageAdapter
{
	private const string _defaultGatewayEndpoint =
		"https://gateway.prod.nado.xyz/v1/";
	private const string _defaultGatewayV2Endpoint =
		"https://gateway.prod.nado.xyz/v2/";
	private const string _defaultArchiveEndpoint =
		"https://archive.prod.nado.xyz/v1/";
	private const string _defaultArchiveV2Endpoint =
		"https://archive.prod.nado.xyz/v2/";
	private const string _defaultWebSocketEndpoint =
		"wss://gateway.prod.nado.xyz/v1/subscribe";

	/// <summary>Official gateway v1 endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string GatewayEndpoint { get; set; } = _defaultGatewayEndpoint;

	/// <summary>Official gateway v2 endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = "Gateway V2",
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string GatewayV2Endpoint { get; set; } = _defaultGatewayV2Endpoint;

	/// <summary>Official archive indexer v1 endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.HistoryKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string ArchiveEndpoint { get; set; } = _defaultArchiveEndpoint;

	/// <summary>Official archive indexer v2 endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = "Archive V2",
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string ArchiveV2Endpoint { get; set; } = _defaultArchiveV2Endpoint;

	/// <summary>Official subscription WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <summary>Optional EVM wallet address for read-only account access.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional EVM private key used to sign orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	private string _subaccountName = "default";

	/// <summary>Nado subaccount name encoded into bytes12.</summary>
	[Display(
		Name = "Subaccount",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public string SubaccountName
	{
		get => _subaccountName;
		set
		{
			value = value.ThrowIfEmpty(nameof(value)).Trim();
			if (Encoding.UTF8.GetByteCount(value) > 12)
				throw new ArgumentOutOfRangeException(nameof(value), value,
					"Nado subaccount name must fit in 12 UTF-8 bytes.");
			_subaccountName = value;
		}
	}

	private TimeSpan _orderExpiry = TimeSpan.FromHours(1);

	/// <summary>Lifetime of a newly submitted order.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExpirationKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 8)]
	public TimeSpan OrderExpiry
	{
		get => _orderExpiry;
		set => _orderExpiry = value >= TimeSpan.FromMinutes(1) &&
			value <= TimeSpan.FromDays(30)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Nado order expiry must be between one minute and 30 days.");
	}

	private decimal _marketOrderSlippage = 0.75m;

	/// <summary>Market-order protection in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 9)]
	public decimal MarketOrderSlippage
	{
		get => _marketOrderSlippage;
		set => _marketOrderSlippage = value is > 0 and <= 25
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Nado market-order slippage must be greater than zero and at most 25%.");
	}

	private int _marketDepth = 100;

	/// <summary>Maximum order-book levels sent to StockSharp.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.MarketDepthKey,
		Order = 10)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 500
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Nado market depth must be between 1 and 500.");
	}

	private int _historyLimit = 500;

	/// <summary>Maximum rows requested from history endpoints.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 11)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 500
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Nado history limit must be between 1 and 500.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(GatewayEndpoint), GatewayEndpoint)
			.Set(nameof(GatewayV2Endpoint), GatewayV2Endpoint)
			.Set(nameof(ArchiveEndpoint), ArchiveEndpoint)
			.Set(nameof(ArchiveV2Endpoint), ArchiveV2Endpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(SubaccountName), SubaccountName)
			.Set(nameof(OrderExpiry), OrderExpiry)
			.Set(nameof(MarketOrderSlippage), MarketOrderSlippage)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		GatewayEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(GatewayEndpoint), GatewayEndpoint), false,
			nameof(GatewayEndpoint));
		GatewayV2Endpoint = NormalizeEndpoint(storage.GetValue(
			nameof(GatewayV2Endpoint), GatewayV2Endpoint), false,
			nameof(GatewayV2Endpoint));
		ArchiveEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(ArchiveEndpoint), ArchiveEndpoint), false,
			nameof(ArchiveEndpoint));
		ArchiveV2Endpoint = NormalizeEndpoint(storage.GetValue(
			nameof(ArchiveV2Endpoint), ArchiveV2Endpoint), false,
			nameof(ArchiveV2Endpoint));
		WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(WebSocketEndpoint), WebSocketEndpoint), true,
			nameof(WebSocketEndpoint));
		WalletAddress = storage.GetValue(nameof(WalletAddress), WalletAddress);
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		SubaccountName = storage.GetValue(nameof(SubaccountName), SubaccountName);
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
				? "Nado WebSocket endpoint must use WS or WSS."
				: "Nado REST endpoint must use HTTP or HTTPS.", parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (WalletAddress.IsEmpty() && PrivateKey.IsEmpty()
			? ": Public"
			: PrivateKey.IsEmpty() ? ": Read-only" : ": Trading");
}
