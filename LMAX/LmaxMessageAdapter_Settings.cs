namespace StockSharp.LMAX;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

using Ecng.ComponentModel;

/// <summary>
/// LMAX locations.
/// </summary>
[DataContract]
[Serializable]
public enum LmaxLocations
{
	/// <summary>
	/// London.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LondonKey)]
	London,

	/// <summary>
	/// New York.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NewYorkKey)]
	NewYork,

	/// <summary>
	/// Tokyo.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokyoKey)]
	Tokyo,

	/// <summary>
	/// Singapore.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SingaporeExchangeKey)]
	Singapore,

	/// <summary>
	/// London Digital.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CryptocurrencyKey)]
	Digital,
}

/// <summary>
/// The messages adapter for LMAX.
/// </summary>
[MediaIcon(Media.MediaNames.lmax)]
[Doc("topics/api/connectors/forex/lmax.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LmaxKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.History | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(LmaxOrderCondition))]
public partial class LmaxMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
{
	/// <summary>
	/// Default value for <see cref="MessageAdapter.HeartbeatInterval"/>.
	/// </summary>
	public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromMinutes(1);

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

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>
	/// LMAX location.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LmaxLocationKey,
		Description = LocalizedStrings.LmaxLocationDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public LmaxLocations Location { get; set; } = LmaxLocations.London;

	/// <summary>Optional account REST API endpoint override.</summary>
	[Display(
		Name = "Account REST endpoint",
		Description = "Optional account REST API endpoint override.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string AccountRestEndpoint { get; set; }

	/// <summary>Optional market data REST API endpoint override.</summary>
	[Display(
		Name = "Market data REST endpoint",
		Description = "Optional market data REST API endpoint override.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string MarketDataRestEndpoint { get; set; }

	/// <summary>Optional market data WebSocket endpoint override.</summary>
	[Display(
		Name = "Market data WebSocket endpoint",
		Description = "Optional market data WebSocket endpoint override.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string MarketDataWebSocketEndpoint { get; set; }

	/// <summary>Optional account WebSocket endpoint override.</summary>
	[Display(
		Name = "Account WebSocket endpoint",
		Description = "Optional account WebSocket endpoint override.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string AccountWebSocketEndpoint { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(Location), Location)
			.Set(nameof(AccountRestEndpoint), AccountRestEndpoint)
			.Set(nameof(MarketDataRestEndpoint), MarketDataRestEndpoint)
			.Set(nameof(MarketDataWebSocketEndpoint), MarketDataWebSocketEndpoint)
			.Set(nameof(AccountWebSocketEndpoint), AccountWebSocketEndpoint)
			;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		Location = storage.GetValue(nameof(Location), Location);
		AccountRestEndpoint = storage.GetValue<string>(nameof(AccountRestEndpoint));
		MarketDataRestEndpoint = storage.GetValue<string>(nameof(MarketDataRestEndpoint));
		MarketDataWebSocketEndpoint = storage.GetValue<string>(nameof(MarketDataWebSocketEndpoint));
		AccountWebSocketEndpoint = storage.GetValue<string>(nameof(AccountWebSocketEndpoint));
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{base.ToString()}: {LocalizedStrings.Key} = {Key.ToId()}";
	}
}
