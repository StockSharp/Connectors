namespace StockSharp.Kraken;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for <see cref="Kraken"/>.
/// </summary>
[MediaIcon(Media.MediaNames.kraken)]
[Doc("topics/api/connectors/crypto_exchanges/kraken.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KrakenKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
public partial class KrakenMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.kraken.com";
	private const string _defaultWebSocketEndpoint = "wss://ws.kraken.com";

	private static readonly HashSet<TimeSpan> _timeFrames = new(
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(15),
	]);

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>
	/// Default value for <see cref="MessageAdapter.HeartbeatInterval"/>.
	/// </summary>
	public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(10);

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

    ///// <summary>
    ///// Is margin enabled.
    ///// </summary>
    //[Display(
    //    ResourceType = typeof(LocalizedStrings),
    //    Name = LocalizedStrings.MarginLeverageKey,
    //    Description = LocalizedStrings.MarginLeverageKey,
    //    GroupName = LocalizedStrings.ConnectionKey,
    //    Order = 1)]
    //public bool IsMarginEnabled { get; set; } = true;

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

	/// <summary>REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>WebSocket endpoint.</summary>
	[Display(
		Name = "WebSocket endpoint",
		Description = "WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Key), Key);
		storage.SetValue(nameof(Secret), Secret);
		//storage.SetValue(nameof(IsMarginEnabled), IsMarginEnabled);
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
		//IsMarginEnabled = storage.GetValue<bool>(nameof(IsMarginEnabled));
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
