namespace StockSharp.Yobit;

/// <summary>
/// The message adapter for <see cref="Yobit"/>.
/// </summary>
[MediaIcon(Media.MediaNames.yobit)]
[Doc("topics/api/connectors/crypto_exchanges/yobit.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.YobitKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(YobitOrderCondition))]
public partial class YobitMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultPublicRestEndpoint = "https://yobit.net/api";
	private const string _defaultPrivateRestEndpoint = "https://yobit.net/tapi";
	private const string _defaultTradePageEndpoint = "https://yobit.net/en/trade";
	private const string _defaultWebSocketEndpoint = "wss://s.yobit.biz";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Public REST API endpoint.</summary>
	[Display(
		Name = "Public REST endpoint",
		Description = "Public REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string PublicRestEndpoint { get; set; } = _defaultPublicRestEndpoint;

	/// <summary>Private REST API endpoint.</summary>
	[Display(
		Name = "Private REST endpoint",
		Description = "Private REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string PrivateRestEndpoint { get; set; } = _defaultPrivateRestEndpoint;

	/// <summary>Trading page endpoint.</summary>
	[Display(
		Name = "Trading page endpoint",
		Description = "Trading page endpoint used to resolve pair identifiers.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string TradePageEndpoint { get; set; } = _defaultTradePageEndpoint;

	/// <summary>WebSocket endpoint.</summary>
	[Display(
		Name = "WebSocket endpoint",
		Description = "WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	private TimeSpan _balanceCheckInterval;

	/// <summary>
	/// Balance check interval. Required in case of deposit and withdraw actions.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BalanceKey,
		Description = LocalizedStrings.BalanceCheckIntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public TimeSpan BalanceCheckInterval
	{
		get => _balanceCheckInterval;
		set
		{
			if (value < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(value));

			_balanceCheckInterval = value;
		}
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Key), Key);
		storage.SetValue(nameof(Secret), Secret);
		storage.SetValue(nameof(BalanceCheckInterval), BalanceCheckInterval);
		storage.SetValue(nameof(PublicRestEndpoint), PublicRestEndpoint);
		storage.SetValue(nameof(PrivateRestEndpoint), PrivateRestEndpoint);
		storage.SetValue(nameof(TradePageEndpoint), TradePageEndpoint);
		storage.SetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		BalanceCheckInterval = storage.GetValue<TimeSpan>(nameof(BalanceCheckInterval));
		PublicRestEndpoint = storage.GetValue(nameof(PublicRestEndpoint), PublicRestEndpoint);
		PrivateRestEndpoint = storage.GetValue(nameof(PrivateRestEndpoint), PrivateRestEndpoint);
		TradePageEndpoint = storage.GetValue(nameof(TradePageEndpoint), TradePageEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}
