namespace StockSharp.Okex;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for <see cref="Okex"/>.
/// </summary>
[MediaIcon(Media.MediaNames.okex)]
[Doc("topics/api/connectors/crypto_exchanges/okex.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OkexKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(OkexOrderCondition))]
public partial class OkexMessageAdapter : MessageAdapter, IDemoAdapter, IKeySecretAdapter, IPassphraseAdapter
{
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
	/// Passphrase.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PassphraseKey,
		Description = LocalizedStrings.PassphraseKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Passphrase { get; set; }

	private bool _isDemo;

	/// <summary>
	/// Is demo.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo
	{
		get => _isDemo;
		set
		{
			if(_isDemo == value)
				return;

			_isDemo = value;
			OnPropertyChanged();
			ResetWebSocketAddresses();
		}
	}

	/// <summary>
	/// Default number of requested recent orders for each instrument type.
	/// </summary>
	public const int DefaultRecentOrdersRequestLimit = 1000;

	/// <summary>
	/// Number of requested recent orders for each instrument type.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RecentOrdersRequestLimitKey,
		Description = LocalizedStrings.RecentOrdersRequestLimitKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int RecentOrdersRequestLimit { get; set; } = DefaultRecentOrdersRequestLimit;

	/// <summary>
	/// Default number of requested recent orders for each instrument type.
	/// </summary>
	public const int DefaultRecentTradesRequestLimit = 1000;

	/// <summary>
	/// Number of requested recent trades.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RecentTradesRequestLimitKey,
		Description = LocalizedStrings.RecentTradesRequestLimitKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public int RecentTradesRequestLimit { get; set; } = DefaultRecentTradesRequestLimit;

	/// <summary>
	/// Administrative password.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.AdminPasswordKey,
		GroupName = LocalizedStrings.WithdrawKey,
		Order = 0)]
	public SecureString AdminPassword { get; set; }

	/// <summary>
	/// REST address.
	/// </summary>
	public string RestAddress { get; set; } = "https://www.okx.com";

	private string _webSocketAddressPublic;

	/// <summary>
	/// WebSocket address (public).
	/// </summary>
	public string WebSocketAddressPublic
	{
		get => _webSocketAddressPublic;
		set
		{
			_webSocketAddressPublic = value;
			OnPropertyChanged();
		}
	}

	private string _webSocketAddressPrivate;

	/// <summary>
	/// WebSocket address (private).
	/// </summary>
	public string WebSocketAddressPrivate
	{
		get => _webSocketAddressPrivate;
		set
		{
			_webSocketAddressPrivate = value;
			OnPropertyChanged();
		}
	}

	private string _webSocketAddressBusiness;

	/// <summary>
	/// WebSocket address (business).
	/// </summary>
	public string WebSocketAddressBusiness
	{
		get => _webSocketAddressBusiness;
		set
		{
			_webSocketAddressBusiness = value;
			OnPropertyChanged();
		}
	}

	private const string _defaultWsPublicAddr = "wss://ws.okx.com:8443/ws/v5/public";
	private const string _defaultWsPrivateAddr = "wss://ws.okx.com:8443/ws/v5/private";
	private const string _defaultWsBusinessAddr = "wss://ws.okx.com:8443/ws/v5/business";
	private const string _defaultWsPublicAddrDemo = "wss://wspap.okx.com:8443/ws/v5/public?brokerId=9999";
	private const string _defaultWsPrivateAddrDemo = "wss://wspap.okx.com:8443/ws/v5/private?brokerId=9999";
	private const string _defaultWsBusinessAddrDemo = "wss://wspap.okx.com:8443/ws/v5/business?brokerId=9999";

	private void ResetWebSocketAddresses()
	{
		if (IsDemo)
		{
			WebSocketAddressPublic = _defaultWsPublicAddrDemo;
			WebSocketAddressPrivate = _defaultWsPrivateAddrDemo;
			WebSocketAddressBusiness = _defaultWsBusinessAddrDemo;
		}
		else
		{
			WebSocketAddressPublic = _defaultWsPublicAddr;
			WebSocketAddressPrivate = _defaultWsPrivateAddr;
			WebSocketAddressBusiness = _defaultWsBusinessAddr;
		}
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Key), Key);
		storage.SetValue(nameof(Secret), Secret);
		storage.SetValue(nameof(Passphrase), Passphrase);
		storage.SetValue(nameof(IsDemo), IsDemo);
		storage.SetValue(nameof(AdminPassword), AdminPassword);
		storage.SetValue(nameof(RestAddress), RestAddress);
		storage.SetValue(nameof(WebSocketAddressPublic), WebSocketAddressPublic);
		storage.SetValue(nameof(WebSocketAddressPrivate), WebSocketAddressPrivate);
		storage.SetValue(nameof(WebSocketAddressBusiness), WebSocketAddressBusiness);
		storage.SetValue(nameof(RecentOrdersRequestLimit), RecentOrdersRequestLimit);
		storage.SetValue(nameof(RecentTradesRequestLimit), RecentTradesRequestLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		AdminPassword = storage.GetValue<SecureString>(nameof(AdminPassword));
		RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
		WebSocketAddressPublic = storage.GetValue(nameof(WebSocketAddressPublic), WebSocketAddressPublic);
		WebSocketAddressPrivate = storage.GetValue(nameof(WebSocketAddressPrivate), WebSocketAddressPrivate);
		WebSocketAddressBusiness = storage.GetValue(nameof(WebSocketAddressBusiness), WebSocketAddressBusiness);
		RecentOrdersRequestLimit = storage.GetValue(nameof(RecentOrdersRequestLimit), DefaultRecentOrdersRequestLimit);
		RecentTradesRequestLimit = storage.GetValue(nameof(RecentTradesRequestLimit), DefaultRecentTradesRequestLimit);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}
