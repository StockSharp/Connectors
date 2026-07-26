namespace StockSharp.Bitbank;

using System.ComponentModel.DataAnnotations;
using System.Security;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for <see cref="Bitbank"/>.
/// </summary>
[MediaIcon(Media.MediaNames.bitbank)]
[Doc("topics/api/connectors/crypto_exchanges/bitbank.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitbankKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
public partial class BitbankMessageAdapter : IKeySecretAdapter
{
	private const string _defaultPublicRestEndpoint = "https://public.bitbank.cc";
	private const string _defaultPrivateRestEndpoint = "https://api.bitbank.cc/v1";
	private const string _defaultWebSocketEndpoint = "wss://stream.bitbank.cc/socket.io/?EIO=4&transport=websocket";

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => Extensions.TimeFrames.Keys.ToArray();

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
	/// Public REST API endpoint.
	/// </summary>
	[Display(
		Name = "Public REST endpoint",
		Description = "Bitbank public REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string PublicRestEndpoint { get; set; } = _defaultPublicRestEndpoint;

	/// <summary>
	/// Private REST API endpoint.
	/// </summary>
	[Display(
		Name = "Private REST endpoint",
		Description = "Bitbank private REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string PrivateRestEndpoint { get; set; } = _defaultPrivateRestEndpoint;

	/// <summary>
	/// WebSocket API endpoint.
	/// </summary>
	[Display(
		Name = "WebSocket endpoint",
		Description = "Bitbank WebSocket API endpoint.",
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <summary>
	/// Request withdraw account's info.
	/// </summary>
	public bool RequestWithdrawAccounts { get; set; }

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
		storage.SetValue(nameof(RequestWithdrawAccounts), RequestWithdrawAccounts);
		storage.SetValue(nameof(BalanceCheckInterval), BalanceCheckInterval);
		storage.SetValue(nameof(PublicRestEndpoint), PublicRestEndpoint);
		storage.SetValue(nameof(PrivateRestEndpoint), PrivateRestEndpoint);
		storage.SetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		RequestWithdrawAccounts = storage.GetValue<bool>(nameof(RequestWithdrawAccounts));
		BalanceCheckInterval = storage.GetValue<TimeSpan>(nameof(BalanceCheckInterval));
		PublicRestEndpoint = storage.GetValue(nameof(PublicRestEndpoint), PublicRestEndpoint);
		PrivateRestEndpoint = storage.GetValue(nameof(PrivateRestEndpoint), PrivateRestEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}
