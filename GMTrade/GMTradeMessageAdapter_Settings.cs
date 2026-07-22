namespace StockSharp.GMTrade;

using Native;

/// <summary>The message adapter for GMTrade perpetual markets.</summary>
[MediaIcon(Media.MediaNames.gmtrade)]
[Doc("topics/api/connectors/crypto_exchanges/gmtrade.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GMTradeKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
public partial class GMTradeMessageAdapter : MessageAdapter
{
	private const string _defaultKeeperEndpoint =
		"https://keeper-prod-api.gmtrade.xyz/graphql";
	private const string _defaultKeeperSocketEndpoint =
		"wss://keeper-prod-api.gmtrade.xyz/graphql-ws";
	private const string _defaultCandleEndpoint =
		"https://price-candle-mainnet.gmtrade.xyz/graphql";
	private const string _defaultCandleSocketEndpoint =
		"wss://price-candle-mainnet.gmtrade.xyz/graphql-ws";
	private const string _defaultIndexerEndpoint =
		"https://gmx-solana-sqd.squids.live/gmx-solana-base:prod/api/graphql";
	private const string _defaultRpcEndpoint = "https://rpc-1.gmtrade.xyz/";

	/// <summary>Keeper GraphQL HTTP endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string KeeperEndpoint { get; set; } = _defaultKeeperEndpoint;

	/// <summary>Keeper GraphQL WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string KeeperSocketEndpoint { get; set; } =
		_defaultKeeperSocketEndpoint;

	/// <summary>Candle GraphQL HTTP endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string CandleEndpoint { get; set; } = _defaultCandleEndpoint;

	/// <summary>Candle GraphQL WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string CandleSocketEndpoint { get; set; } =
		_defaultCandleSocketEndpoint;

	/// <summary>Official Subsquid GraphQL endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.HistoryKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string IndexerEndpoint { get; set; } = _defaultIndexerEndpoint;

	/// <summary>Solana JSON-RPC endpoint used for wallet balances.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string RpcEndpoint { get; set; } = _defaultRpcEndpoint;

	/// <summary>Optional read-only Solana wallet address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	private TimeSpan _tradePollingInterval = TimeSpan.FromSeconds(2);

	/// <summary>Official indexer polling interval for new trades.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalDataUpdatesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public TimeSpan TradePollingInterval
	{
		get => _tradePollingInterval;
		set => _tradePollingInterval = value >= TimeSpan.FromSeconds(1)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"GMTrade trade polling cannot be faster than once per second.");
	}

	private TimeSpan _balancePollingInterval = TimeSpan.FromSeconds(15);

	/// <summary>Solana wallet-balance polling interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalDataUpdatesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public TimeSpan BalancePollingInterval
	{
		get => _balancePollingInterval;
		set => _balancePollingInterval = value >= TimeSpan.FromSeconds(5)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"GMTrade balance polling cannot be faster than once per five seconds.");
	}

	private int _historyLimit = 5000;

	/// <summary>Maximum number of rows requested per history query.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 9)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"GMTrade history limit must be between 1 and 5000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(KeeperEndpoint), KeeperEndpoint)
			.Set(nameof(KeeperSocketEndpoint), KeeperSocketEndpoint)
			.Set(nameof(CandleEndpoint), CandleEndpoint)
			.Set(nameof(CandleSocketEndpoint), CandleSocketEndpoint)
			.Set(nameof(IndexerEndpoint), IndexerEndpoint)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(TradePollingInterval), TradePollingInterval)
			.Set(nameof(BalancePollingInterval), BalancePollingInterval)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		KeeperEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(KeeperEndpoint), KeeperEndpoint), false,
			nameof(KeeperEndpoint));
		KeeperSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(KeeperSocketEndpoint), KeeperSocketEndpoint), true,
			nameof(KeeperSocketEndpoint));
		CandleEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(CandleEndpoint), CandleEndpoint), false,
			nameof(CandleEndpoint));
		CandleSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(CandleSocketEndpoint), CandleSocketEndpoint), true,
			nameof(CandleSocketEndpoint));
		IndexerEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(IndexerEndpoint), IndexerEndpoint), false,
			nameof(IndexerEndpoint));
		RpcEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RpcEndpoint),
			RpcEndpoint), false, nameof(RpcEndpoint));
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		if (!WalletAddress.IsEmpty())
			WalletAddress = WalletAddress.NormalizePublicKey(nameof(WalletAddress));
		TradePollingInterval = storage.GetValue(nameof(TradePollingInterval),
			TradePollingInterval);
		BalancePollingInterval = storage.GetValue(nameof(BalancePollingInterval),
			BalancePollingInterval);
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
				? "GMTrade WebSocket endpoints must use ws or wss."
				: "GMTrade HTTP endpoints must use HTTP or HTTPS.", parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (WalletAddress.IsEmpty()
			? ": Public"
			: $": Wallet={WalletAddress}");
}
