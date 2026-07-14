namespace StockSharp.GateIO;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

using Ecng.ComponentModel;

/// <summary>
/// Sections.
/// </summary>
[DataContract]
[Serializable]
public enum GateIOSections
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
	/// Perpetual.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PerpetualKey,
		Description = LocalizedStrings.PerpetualSectionKey)]
	Perpetual,

	/// <summary>
	/// Delivery.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DeliveryKey,
		Description = LocalizedStrings.DeliverySectionKey)]
	Delivery,

	/// <summary>
	/// Options.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OptionsKey,
		Description = LocalizedStrings.OptionsSectionKey)]
	Options,
}

/// <summary>
/// The message adapter for <see cref="GateIO"/>.
/// </summary>
[MediaIcon(Media.MediaNames.gateio)]
[Doc("topics/api/connectors/crypto_exchanges/gateio.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GateIOKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(GateIOOrderCondition))]
public partial class GateIOMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
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

	private IEnumerable<GateIOSections> _sections = Enumerator.GetValues<GateIOSections>();

	/// <summary>
	/// Sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[ItemsSource(typeof(GateIOSections))]
	[BasicSetting]
	public IEnumerable<GateIOSections> Sections
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

	private const string _defaultRestDomain = "api.gateio.ws";
	private const string _defaultSpotWsDomain = "api.gateio.ws/ws/v4";
	private const string _defaultFuturesWsDomain = "fx-ws.gateio.ws/v4/ws";
	private const string _defaultDeliveryWsDomain = "fx-ws.gateio.ws/v4/ws/delivery";
	private const string _defaultOptionsWsDomain = "op-ws.gateio.live/v4/ws";

	/// <summary>
	/// REST domain.
	/// </summary>
	public string RestDomain { get; set; } = _defaultRestDomain;

	/// <summary>
	/// <see cref="GateIOSections.Spot"/> web sockets domain.
	/// </summary>
	public string SpotWsDomain { get; set; } = _defaultSpotWsDomain;

	/// <summary>
	/// <see cref="GateIOSections.Perpetual"/> web sockets domain.
	/// </summary>
	public string FuturesWsDomain { get; set; } = _defaultFuturesWsDomain;

	/// <summary>
	/// <see cref="GateIOSections.Delivery"/> web sockets domain.
	/// </summary>
	public string DeliveryWsDomain { get; set; } = _defaultDeliveryWsDomain;

	/// <summary>
	/// <see cref="GateIOSections.Options"/> web sockets domain.
	/// </summary>
	public string OptionsWsDomain { get; set; } = _defaultOptionsWsDomain;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(IsDemo), IsDemo)

			.Set(nameof(RestDomain), RestDomain)
			.Set(nameof(SpotWsDomain), SpotWsDomain)
			.Set(nameof(FuturesWsDomain), FuturesWsDomain)
			.Set(nameof(DeliveryWsDomain), DeliveryWsDomain)
			.Set(nameof(OptionsWsDomain), OptionsWsDomain)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Sections = storage.GetValue<string>(nameof(Sections)).SplitByComma().Select(s => s.To<GateIOSections>()).ToArray();
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));

		RestDomain = storage.GetValue(nameof(RestDomain), RestDomain);
		SpotWsDomain = storage.GetValue(nameof(SpotWsDomain), SpotWsDomain);
		FuturesWsDomain = storage.GetValue(nameof(FuturesWsDomain), FuturesWsDomain);
		DeliveryWsDomain = storage.GetValue(nameof(DeliveryWsDomain), DeliveryWsDomain);
		OptionsWsDomain = storage.GetValue(nameof(OptionsWsDomain), OptionsWsDomain);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}