namespace StockSharp.StandX;

/// <summary>
/// StandX wallet chains.
/// </summary>
[DataContract]
[Serializable]
public enum StandXChains
{
	/// <summary>BNB Smart Chain wallet.</summary>
	[EnumMember]
	Bsc,

	/// <summary>Solana wallet.</summary>
	[EnumMember]
	Solana,
}

/// <summary>
/// The message adapter for StandX perpetual futures.
/// </summary>
[MediaIcon(Media.MediaNames.standx)]
[Doc("topics/api/connectors/crypto_exchanges/standx.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.StandXKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Ticks)]
[OrderCondition(typeof(StandXOrderCondition))]
public partial class StandXMessageAdapter : MessageAdapter
{
	private const string _defaultRestEndpoint = "https://perps.standx.com";
	private const string _defaultAuthEndpoint = "https://api.standx.com";
	private const string _defaultMarketSocketEndpoint =
		"wss://perps.standx.com/ws-stream/v1";
	private const string _defaultOrderSocketEndpoint =
		"wss://perps.standx.com/ws-api/v1";

	/// <summary>Public and private REST endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Wallet authentication endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string AuthEndpoint { get; set; } = _defaultAuthEndpoint;

	/// <summary>Market and account WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string MarketSocketEndpoint { get; set; } =
		_defaultMarketSocketEndpoint;

	/// <summary>Signed order-entry WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TransactionsKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string OrderSocketEndpoint { get; set; } =
		_defaultOrderSocketEndpoint;

	private StandXChains _chain;

	/// <summary>Wallet chain used for private access.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public StandXChains Chain
	{
		get => _chain;
		set => _chain = Enum.IsDefined(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Unsupported StandX wallet chain.");
	}

	/// <summary>Optional wallet address. It is validated against the key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>BSC hex key or Solana base58 64-byte keypair.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	private TimeSpan _tokenLifetime = TimeSpan.FromDays(7);

	/// <summary>Lifetime requested for the StandX JWT.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public TimeSpan TokenLifetime
	{
		get => _tokenLifetime;
		set => _tokenLifetime = value is { TotalSeconds: >= 60 and <= 604800 }
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"StandX token lifetime must be between one minute and seven days.");
	}

	private int _marketDepth = 50;

	/// <summary>Maximum order-book levels sent to StockSharp.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthKey,
		GroupName = LocalizedStrings.MarketDepthKey,
		Order = 8)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 500
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"StandX market depth must be between 1 and 500.");
	}

	private TimeSpan _candlePollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>REST polling interval for current candle updates.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalDataUpdatesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public TimeSpan CandlePollingInterval
	{
		get => _candlePollingInterval;
		set => _candlePollingInterval = value >= TimeSpan.FromSeconds(1)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"StandX candle polling cannot be faster than once per second.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(AuthEndpoint), AuthEndpoint)
			.Set(nameof(MarketSocketEndpoint), MarketSocketEndpoint)
			.Set(nameof(OrderSocketEndpoint), OrderSocketEndpoint)
			.Set(nameof(Chain), Chain)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(TokenLifetime), TokenLifetime)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(CandlePollingInterval), CandlePollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
			RestEndpoint), false, nameof(RestEndpoint));
		AuthEndpoint = NormalizeEndpoint(storage.GetValue(nameof(AuthEndpoint),
			AuthEndpoint), false, nameof(AuthEndpoint));
		MarketSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(MarketSocketEndpoint), MarketSocketEndpoint), true,
			nameof(MarketSocketEndpoint));
		OrderSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(OrderSocketEndpoint), OrderSocketEndpoint), true,
			nameof(OrderSocketEndpoint));
		Chain = storage.GetValue(nameof(Chain), Chain);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		TokenLifetime = storage.GetValue(nameof(TokenLifetime), TokenLifetime);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
		CandlePollingInterval = storage.GetValue(nameof(CandlePollingInterval),
			CandlePollingInterval);
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
				: uri.Scheme is not ("http" or "https")))
			throw new ArgumentException(isWebSocket
				? "StandX WebSocket endpoint must use ws or wss."
				: "StandX REST endpoint must use HTTP or HTTPS.", parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": {Chain}, Wallet={WalletAddress}";
}
