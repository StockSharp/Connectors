namespace StockSharp.Pacifica;

/// <summary>The message adapter for Pacifica perpetual futures.</summary>
[MediaIcon(Media.MediaNames.pacifica)]
[Doc("topics/api/connectors/crypto_exchanges/pacifica.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PacificaKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(PacificaOrderCondition))]
public partial class PacificaMessageAdapter : MessageAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.pacifica.fi/api/v1/";
	private const string _defaultWebSocketEndpoint =
		"wss://ws.pacifica.fi/ws";

	/// <summary>Official REST endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Official WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string WebSocketEndpoint { get; set; } =
		_defaultWebSocketEndpoint;

	/// <summary>Optional main Pacifica account address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional base58 Solana keypair used for signing.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Optional API agent wallet derived from the signing key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PacificaAgentWalletKey,
		Description = LocalizedStrings.PacificaAgentWalletDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string AgentWallet { get; set; }

	private TimeSpan _signatureExpiryWindow = TimeSpan.FromSeconds(5);

	/// <summary>Lifetime of signed trading requests.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PacificaExpiryWindowKey,
		Description = LocalizedStrings.PacificaExpiryWindowDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public TimeSpan SignatureExpiryWindow
	{
		get => _signatureExpiryWindow;
		set => _signatureExpiryWindow =
			value >= TimeSpan.FromSeconds(1) &&
			value <= TimeSpan.FromSeconds(60)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Pacifica signature expiry must be between 1 and 60 seconds.");
	}

	private decimal _marketOrderSlippage = 0.5m;

	/// <summary>Default market-order slippage tolerance in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 6)]
	public decimal MarketOrderSlippage
	{
		get => _marketOrderSlippage;
		set => _marketOrderSlippage = value is > 0 and <= 100
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Pacifica slippage must be greater than zero and at most 100%.");
	}

	private int _marketDepth = 10;

	/// <summary>Maximum order-book levels sent to StockSharp.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.MarketDepthKey,
		Order = 7)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 10
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Pacifica market depth must be between 1 and 10.");
	}

	private int _historyLimit = 4000;

	/// <summary>Maximum rows requested from history endpoints.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 8)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 4000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Pacifica history limit must be between 1 and 4000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(AgentWallet), AgentWallet)
			.Set(nameof(SignatureExpiryWindow), SignatureExpiryWindow)
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
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress))?.Trim();
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		AgentWallet = storage.GetValue<string>(nameof(AgentWallet))?.Trim();
		SignatureExpiryWindow = storage.GetValue(
			nameof(SignatureExpiryWindow), SignatureExpiryWindow);
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
				? "Pacifica WebSocket endpoint must use WS or WSS."
				: "Pacifica REST endpoint must use HTTP or HTTPS.",
				parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (WalletAddress.IsEmpty()
			? ": Public"
			: PrivateKey.IsEmpty() ? ": Read-only" : ": Trading");
}
