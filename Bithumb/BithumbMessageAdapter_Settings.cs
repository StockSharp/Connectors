namespace StockSharp.Bithumb;

/// <summary>
/// The message adapter for <see cref="Bithumb"/>.
/// </summary>
[MediaIcon(Media.MediaNames.bithumb)]
[Doc("topics/api/connectors/crypto_exchanges/bithumb.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BithumbKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BithumbOrderCondition))]
public partial class BithumbMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.bithumb.com";
	private const string _defaultPrimeRestEndpoint = "https://prime.bithumb.com";
	private const string _defaultWebSocketEndpoint = "wss://ws-api.bithumb.com/websocket/v1";

	/// <summary>
	/// Default value for <see cref="MessageAdapter.HeartbeatInterval"/>.
	/// </summary>
	public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(1);

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

	/// <summary>
	/// REST API endpoint.
	/// </summary>
	[Display(
		Name = "REST endpoint",
		Description = "Bithumb REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>
	/// Prime REST API endpoint.
	/// </summary>
	[Display(
		Name = "Prime REST endpoint",
		Description = "Bithumb Prime REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string PrimeRestEndpoint { get; set; } = _defaultPrimeRestEndpoint;

	/// <summary>
	/// WebSocket API endpoint.
	/// </summary>
	[Display(
		Name = "WebSocket endpoint",
		Description = "Bithumb WebSocket API endpoint.",
		GroupName = LocalizedStrings.WebSocketAddressesKey)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <summary>
	/// Premium service.
	/// </summary>
	//[Display(
	//	ResourceType = typeof(LocalizedStrings),
	//	Name = LocalizedStrings.PremiumServiceKey,
	//	Description = LocalizedStrings.PremiumServiceKey + LocalizedStrings.Dot,
	//	GroupName = LocalizedStrings.ConnectionKey,
	//	Order = 2)]
	public bool IsPrime { get; set; }

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
		storage.SetValue(nameof(IsPrime), IsPrime);
		storage.SetValue(nameof(RestEndpoint), RestEndpoint);
		storage.SetValue(nameof(PrimeRestEndpoint), PrimeRestEndpoint);
		storage.SetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		BalanceCheckInterval = storage.GetValue<TimeSpan>(nameof(BalanceCheckInterval));
		IsPrime = storage.GetValue<bool>(nameof(IsPrime));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		PrimeRestEndpoint = storage.GetValue(nameof(PrimeRestEndpoint), PrimeRestEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}
