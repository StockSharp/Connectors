namespace StockSharp.Binance;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// Sections.
/// </summary>
[DataContract]
[Serializable]
public enum BinanceSections
{
	/// <summary>
	/// Spot.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotKey,
		Description = LocalizedStrings.SpotSectionKey)]
	Spot,

	/// <summary>
	/// Margin.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarginKey,
		Description = LocalizedStrings.MarginSectionKey)]
	Margin,

	/// <summary>
	/// Futures.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey,
		Description = LocalizedStrings.FuturesSectionKey)]
	Futures,

	/// <summary>
	/// Futures-coin.
	/// </summary>
	[EnumMember]
	FuturesCoin,
}

/// <summary>
/// The message adapter for <see cref="Binance"/>.
/// </summary>
[MediaIcon(Media.MediaNames.binance)]
[Doc("topics/api/connectors/crypto_exchanges/binance.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BinanceKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions | MessageAdapterCategories.OrderLog)]
[OrderCondition(typeof(BinanceOrderCondition))]
public partial class BinanceMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
{
	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. Native.Extensions.TimeFrames.Keys];

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

	private IEnumerable<BinanceSections> _sections = Enumerator.GetValues<BinanceSections>();

	/// <summary>
	/// Sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[ItemsSource(typeof(BinanceSections))]
	[BasicSetting]
	public IEnumerable<BinanceSections> Sections
	{
		get => _sections;
		set
		{
			if (value is null)
				throw new ArgumentNullException(nameof(value));

			var arr = value.ToArray();

			if (arr.Length == 0)
				throw new ArgumentOutOfRangeException(nameof(value));

			_sections = arr;
		}
	}

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>
	/// Remove listen key on disconnect.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public bool RemoveListenKeyOnDisconnect { get; set; } = true;

	private const string _defaultHostWebSocketSpot         = "stream.binance.com";
	private const string _defaultHostWebSocketFuture       = "fstream.binance.com";
	private const string _defaultHostWebSocketFutureCoin   = "dstream.binance.com";

	private const string _defaultHostRestSpot              = "api.binance.com";
	private const string _defaultHostRestFuture            = "fapi.binance.com";
	private const string _defaultHostRestFutureCoin        = "dapi.binance.com";

	/// <summary>
	/// Websocket host for spot/margin.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotSectionKey,
		Description = LocalizedStrings.SpotSectionKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	public string HostWebSocketSpot { get; set; } = _defaultHostWebSocketSpot;

	/// <summary>
	/// Websocket host for future.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesSectionKey,
		Description = LocalizedStrings.FuturesSectionKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 2)]
	public string HostWebSocketFuture { get; set; } = _defaultHostWebSocketFuture;

	/// <summary>
	/// Websocket host for future coin.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesCoinSectionKey,
		Description = LocalizedStrings.FuturesCoinSectionKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 3)]
	public string HostWebSocketFutureCoin { get; set; } = _defaultHostWebSocketFutureCoin;

	/// <summary>
	/// REST host for spot/margin.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotSectionKey,
		Description = LocalizedStrings.SpotSectionKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string HostRestSpot { get; set; } = _defaultHostRestSpot;

	/// <summary>
	/// REST host for future.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesSectionKey,
		Description = LocalizedStrings.FuturesSectionKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string HostRestFuture { get; set; } = _defaultHostRestFuture;

	/// <summary>
	/// REST host for future coin.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesCoinSectionKey,
		Description = LocalizedStrings.FuturesCoinSectionKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string HostRestFutureCoin { get; set; } = _defaultHostRestFutureCoin;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RemoveListenKeyOnDisconnect), RemoveListenKeyOnDisconnect)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())

			.Set(nameof(HostWebSocketSpot), HostWebSocketSpot)
			.Set(nameof(HostWebSocketFuture), HostWebSocketFuture)
			.Set(nameof(HostWebSocketFutureCoin), HostWebSocketFutureCoin)

			.Set(nameof(HostRestSpot), HostRestSpot)
			.Set(nameof(HostRestFuture), HostRestFuture)
			.Set(nameof(HostRestFutureCoin), HostRestFutureCoin)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		RemoveListenKeyOnDisconnect = storage.GetValue<bool>(nameof(RemoveListenKeyOnDisconnect));

		HostWebSocketSpot = storage.GetValue(nameof(HostWebSocketSpot), HostWebSocketSpot);
		HostWebSocketFuture = storage.GetValue(nameof(HostWebSocketFuture), HostWebSocketFuture);
		HostWebSocketFutureCoin = storage.GetValue(nameof(HostWebSocketFutureCoin), HostWebSocketFutureCoin);

		HostRestSpot = storage.GetValue(nameof(HostRestSpot), HostRestSpot);
		HostRestFuture = storage.GetValue(nameof(HostRestFuture), HostRestFuture);
		HostRestFutureCoin = storage.GetValue(nameof(HostRestFutureCoin), HostRestFutureCoin);

		Sections = storage.GetValue<string>(nameof(Sections)).SplitByComma().Select(s => s.To<BinanceSections>()).ToArray();
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}
