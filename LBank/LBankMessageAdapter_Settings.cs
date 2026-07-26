namespace StockSharp.LBank;

/// <summary>
/// The message adapter for <see cref="LBank"/>.
/// </summary>
[MediaIcon(Media.MediaNames.lbank)]
[Doc("topics/api/connectors/crypto_exchanges/lbank.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LBankKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(LBankOrderCondition))]
public partial class LBankMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.lbkex.com/v2";
	private const string _defaultWebSocketEndpoint = "wss://www.lbkex.net/ws/V2/";

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } = Extensions.TimeFrames.Keys.ToArray();

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

	/// <summary>REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

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
		storage.SetValue(nameof(RestEndpoint), RestEndpoint);
		storage.SetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		BalanceCheckInterval = storage.GetValue<TimeSpan>(nameof(BalanceCheckInterval));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}
