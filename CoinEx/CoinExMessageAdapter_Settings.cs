namespace StockSharp.CoinEx;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

using Ecng.ComponentModel;

/// <summary>
/// Sections.
/// </summary>
[DataContract]
[Serializable]
public enum CoinExSections
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
	/// Futures.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey,
		Description = LocalizedStrings.FuturesSectionKey)]
	Futures,
}

/// <summary>
/// The message adapter for <see cref="CoinEx"/>.
/// </summary>
[MediaIcon(Media.MediaNames.coinex)]
[Doc("topics/api/connectors/crypto_exchanges/coinex.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinExKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(CoinExOrderCondition))]
public partial class CoinExMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.coinex.com";
	private const string _defaultSpotWebSocketEndpoint = "wss://socket.coinex.com/v2/spot";
	private const string _defaultFuturesWebSocketEndpoint = "wss://socket.coinex.com/v2/futures";

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => Native.Extensions.TimeFrames.Keys.ToArray();

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
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.CoinExRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>
	/// Spot WebSocket API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotWebSocketEndpointKey,
		Description = LocalizedStrings.CoinExSpotWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey)]
	[BasicSetting]
	public string SpotWebSocketEndpoint { get; set; } = _defaultSpotWebSocketEndpoint;

	/// <summary>
	/// Futures WebSocket API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesWebSocketEndpointKey,
		Description = LocalizedStrings.CoinExFuturesWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey)]
	[BasicSetting]
	public string FuturesWebSocketEndpoint { get; set; } = _defaultFuturesWebSocketEndpoint;

	private IEnumerable<CoinExSections> _sections = Enumerator.GetValues<CoinExSections>();

	/// <summary>
	/// Sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[ItemsSource(typeof(CoinExSections))]
	[BasicSetting]
	public IEnumerable<CoinExSections> Sections
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
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(SpotWebSocketEndpoint), SpotWebSocketEndpoint)
			.Set(nameof(FuturesWebSocketEndpoint), FuturesWebSocketEndpoint)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Sections = storage.GetValue<string>(nameof(Sections)).SplitByComma().Select(s => s.To<CoinExSections>()).ToArray();
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		SpotWebSocketEndpoint = storage.GetValue(nameof(SpotWebSocketEndpoint), SpotWebSocketEndpoint);
		FuturesWebSocketEndpoint = storage.GetValue(nameof(FuturesWebSocketEndpoint), FuturesWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}
