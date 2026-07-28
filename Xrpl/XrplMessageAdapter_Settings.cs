namespace StockSharp.Xrpl;

/// <summary>
/// The message adapter for the XRP Ledger decentralized exchange.
/// </summary>
[MediaIcon(Media.MediaNames.xrpl)]
[Doc("topics/api/connectors/crypto_exchanges/xrpl.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.XrplKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class XrplMessageAdapter : MessageAdapter
{
	private const string _defaultRpcEndpoint = "https://xrplcluster.com/";
	private const string _defaultStreamingEndpoint =
		"wss://xrplcluster.com/";
	private const string _rlusdIssuer =
		"rMxCKbEDwqr76QuheSUMdEGf4B9xJ8m5De";
	private const string _defaultMarkets = "XRP/RLUSD:" + _rlusdIssuer;

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		XrplExtensions.TimeFrames;

	/// <summary>XRPL JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RpcEndpoint { get; set; } = _defaultRpcEndpoint;

	/// <summary>XRPL WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string StreamingEndpoint { get; set; } =
		_defaultStreamingEndpoint;

	/// <summary>Public classic account address used for private data.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string Account { get; set; }

	/// <summary>Family seed used to sign transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Seed { get; set; }

	/// <summary>
	/// Semicolon-separated BASE/QUOTE markets. Tokens use CODE:ISSUER.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public string Markets { get; set; } = _defaultMarkets;

	/// <summary>Optional permissioned DEX domain identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IdKey,
		Description = LocalizedStrings.IdKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string DomainId { get; set; }

	private int _orderBookDepth = 50;

	/// <summary>Maximum number of levels in order-book snapshots.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		Description = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int OrderBookDepth
	{
		get => _orderBookDepth;
		set => _orderBookDepth = value is >= 1 and <= 400
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"XRPL order-book depth must be between 1 and 400.");
	}

	private int _historyLedgerLimit = 10_000;

	/// <summary>Maximum number of ledgers scanned for history.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int HistoryLedgerLimit
	{
		get => _historyLedgerLimit;
		set => _historyLedgerLimit = value is >= 1 and <= 100_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"XRPL history ledger limit must be between 1 and 100,000.");
	}

	private decimal _feeMultiplier = 1.2m;

	/// <summary>Multiplier applied to the open-ledger transaction cost.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MultiplierKey,
		Description = LocalizedStrings.MultiplierKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public decimal FeeMultiplier
	{
		get => _feeMultiplier;
		set => _feeMultiplier = value is >= 1m and <= 100m
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"XRPL fee multiplier must be between 1 and 100.");
	}

	private int _lastLedgerOffset = 20;

	/// <summary>
	/// Number of ledgers after which a submitted transaction expires.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public int LastLedgerOffset
	{
		get => _lastLedgerOffset;
		set => _lastLedgerOffset = value is >= 4 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"XRPL last-ledger offset must be between 4 and 1,000.");
	}

	private decimal _marketOrderProtection = 5m;

	/// <summary>
	/// Price protection in percent for IOC market-order offers.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public decimal MarketOrderProtection
	{
		get => _marketOrderProtection;
		set => _marketOrderProtection = value is > 0 and < 100
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"XRPL market-order protection must be between 0 and 100 " +
					"percent.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Snapshot and private-state polling interval.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval =
			value >= TimeSpan.FromSeconds(2) &&
			value <= TimeSpan.FromMinutes(5)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"XRPL polling interval must be between two seconds and " +
						"five minutes.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(StreamingEndpoint), StreamingEndpoint)
			.Set(nameof(Account), Account)
			.Set(nameof(Seed), Seed)
			.Set(nameof(Markets), Markets)
			.Set(nameof(DomainId), DomainId)
			.Set(nameof(OrderBookDepth), OrderBookDepth)
			.Set(nameof(HistoryLedgerLimit), HistoryLedgerLimit)
			.Set(nameof(FeeMultiplier), FeeMultiplier)
			.Set(nameof(LastLedgerOffset), LastLedgerOffset)
			.Set(nameof(MarketOrderProtection), MarketOrderProtection)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		RpcEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(RpcEndpoint), RpcEndpoint), false) ??
			_defaultRpcEndpoint;
		StreamingEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(StreamingEndpoint), StreamingEndpoint), true) ??
			_defaultStreamingEndpoint;
		Account = NormalizeAccount(storage.GetValue<string>(
			nameof(Account)));
		Seed = storage.GetValue<SecureString>(nameof(Seed));
		Markets = storage.GetValue(nameof(Markets), Markets);
		DomainId = XrplExtensions.NormalizeDomainId(storage.GetValue<string>(
			nameof(DomainId)));
		OrderBookDepth = storage.GetValue(nameof(OrderBookDepth),
			OrderBookDepth);
		HistoryLedgerLimit = storage.GetValue(nameof(HistoryLedgerLimit),
			HistoryLedgerLimit);
		FeeMultiplier = storage.GetValue(nameof(FeeMultiplier),
			FeeMultiplier);
		LastLedgerOffset = storage.GetValue(nameof(LastLedgerOffset),
			LastLedgerOffset);
		MarketOrderProtection = storage.GetValue(
			nameof(MarketOrderProtection), MarketOrderProtection);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
	}

	private static string NormalizeEndpoint(string value, bool isSocket)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		if (!value.Contains("://", StringComparison.Ordinal))
			value = (isSocket ? "wss://" : "https://") +
				value.TrimStart('/');
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
			throw new ArgumentException(
				$"Invalid XRPL endpoint '{value}'.", nameof(value));
		var isValid = isSocket
			? uri.Scheme.EqualsIgnoreCase("wss") ||
				uri.Scheme.EqualsIgnoreCase("ws") && uri.IsLoopback
			: uri.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps) ||
				uri.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp);
		if (!isValid)
			throw new ArgumentException(
				$"Invalid XRPL endpoint '{value}'.", nameof(value));
		return value;
	}

	private static string NormalizeAccount(string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		if (!XrplCodec.IsValidClassicAddress(value))
			throw new ArgumentException(
				$"XRPL account '{value}' is not a valid classic address.",
				nameof(value));
		return value;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Account={Account}";
}
