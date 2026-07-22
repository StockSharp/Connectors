namespace StockSharp.DydxChain;

public partial class DydxChainMessageAdapter
{
	private const string _defaultIndexerEndpoint =
		"https://indexer.dydx.trade";
	private const string _defaultWebSocketEndpoint =
		"wss://indexer.dydx.trade/v4/ws";
	private const string _defaultValidatorEndpoint =
		"https://dydx-ops-rpc.kingnodes.com:443";

	/// <summary>Official dYdX Indexer REST endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string IndexerEndpoint { get; set; } = _defaultIndexerEndpoint;

	/// <summary>Official dYdX Indexer WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <summary>dYdX Chain CometBFT RPC endpoint.</summary>
	[Display(
		Name = "Validator RPC",
		Description = "dYdX Chain CometBFT JSON-RPC endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string ValidatorEndpoint { get; set; } = _defaultValidatorEndpoint;

	/// <summary>Optional dYdX wallet address for account data.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional secp256k1 private key for direct transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	private int _subaccountNumber;

	/// <summary>dYdX subaccount number.</summary>
	[Display(
		Name = "Subaccount",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int SubaccountNumber
	{
		get => _subaccountNumber;
		set => _subaccountNumber = value is >= 0 and <= 128000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"dYdX subaccount number must be between 0 and 128000.");
	}

	private decimal _marketOrderSlippage = 0.5m;

	/// <summary>Market-order limit-price deviation from the oracle.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 6)]
	public decimal MarketOrderSlippage
	{
		get => _marketOrderSlippage;
		set => _marketOrderSlippage = value is > 0 and <= 50
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"dYdX market-order slippage must be above zero and at most 50%.");
	}

	private int _shortTermBlockWindow = 15;

	/// <summary>Short-term order lifetime in blocks.</summary>
	[Display(
		Name = "Block lifetime",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 7)]
	public int ShortTermBlockWindow
	{
		get => _shortTermBlockWindow;
		set => _shortTermBlockWindow = value is >= 2 and <= 20
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"dYdX short-term lifetime must be between 2 and 20 blocks.");
	}

	private TimeSpan _statefulOrderLifetime = TimeSpan.FromDays(28);

	/// <summary>Default lifetime for long-term and conditional orders.</summary>
	[Display(
		Name = "Stateful lifetime",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 8)]
	public TimeSpan StatefulOrderLifetime
	{
		get => _statefulOrderLifetime;
		set => _statefulOrderLifetime = value >= TimeSpan.FromMinutes(1) &&
			value <= TimeSpan.FromDays(90)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"dYdX stateful lifetime must be between one minute and 90 days.");
	}

	private long _gasLimit = 1_000_000;

	/// <summary>Gas limit used for zero-fee dYdX transactions.</summary>
	[Display(
		Name = "Gas limit",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 9)]
	public long GasLimit
	{
		get => _gasLimit;
		set => _gasLimit = value is >= 100_000 and <= 10_000_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"dYdX gas limit must be between 100000 and 10000000.");
	}

	private int _historyLimit = 1000;

	/// <summary>Maximum number of rows requested from Indexer history.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 10)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"dYdX history limit must be between 1 and 1000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(IndexerEndpoint), IndexerEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(ValidatorEndpoint), ValidatorEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(SubaccountNumber), SubaccountNumber)
			.Set(nameof(MarketOrderSlippage), MarketOrderSlippage)
			.Set(nameof(ShortTermBlockWindow), ShortTermBlockWindow)
			.Set(nameof(StatefulOrderLifetime), StatefulOrderLifetime)
			.Set(nameof(GasLimit), GasLimit)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		IndexerEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(IndexerEndpoint), IndexerEndpoint), false,
			nameof(IndexerEndpoint));
		WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(WebSocketEndpoint), WebSocketEndpoint), true,
			nameof(WebSocketEndpoint));
		ValidatorEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(ValidatorEndpoint), ValidatorEndpoint), false,
			nameof(ValidatorEndpoint));
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress))?.Trim();
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		SubaccountNumber = storage.GetValue(nameof(SubaccountNumber),
			SubaccountNumber);
		MarketOrderSlippage = storage.GetValue(nameof(MarketOrderSlippage),
			MarketOrderSlippage);
		ShortTermBlockWindow = storage.GetValue(nameof(ShortTermBlockWindow),
			ShortTermBlockWindow);
		StatefulOrderLifetime = storage.GetValue(nameof(StatefulOrderLifetime),
			StatefulOrderLifetime);
		GasLimit = storage.GetValue(nameof(GasLimit), GasLimit);
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
				: uri.Scheme is not ("http" or "https")) ||
			!uri.UserInfo.IsEmpty() ||
			(uri.Scheme is "http" or "ws" && !uri.IsLoopback))
			throw new ArgumentException(isWebSocket
				? "dYdX WebSocket endpoint must use WSS, except locally."
				: "dYdX endpoint must use HTTPS, except locally.",
				parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (WalletAddress.IsEmpty() && PrivateKey.IsEmpty()
			? ": Public"
			: PrivateKey.IsEmpty() ? ": Read-only" : ": Trading");
}
