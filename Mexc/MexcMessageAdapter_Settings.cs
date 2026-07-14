namespace StockSharp.Mexc;

/// <summary>
/// Sections.
/// </summary>
[DataContract]
[Serializable]
public enum MexcSections
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
/// The message adapter for <see cref="Mexc"/>.
/// </summary>
[MediaIcon(Media.MediaNames.mexc)]
[Doc("topics/api/connectors/crypto_exchanges/mexc.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MexcKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(MexcOrderCondition))]
public partial class MexcMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
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

	private IEnumerable<MexcSections> _sections = Enumerator.GetValues<MexcSections>();

	/// <summary>
	/// Sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[ItemsSource(typeof(MexcSections))]
	[BasicSetting]
	public IEnumerable<MexcSections> Sections
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
		Order = 4)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	private const string _defaultRestDomain = "api.mexc.com";
	private const string _defaultFuturesRestDomain = "contract.mexc.com";
	private const string _defaultSpotWsDomain = "wbs-api.mexc.com/ws";
	private const string _defaultFuturesWsDomain = "contract.mexc.com/edge";

	/// <summary>
	/// REST domain.
	/// </summary>
	public string RestDomain { get; set; } = _defaultRestDomain;

	/// <summary>
	/// <see cref="MexcSections.Futures"/> REST domain.
	/// </summary>
	public string FuturesRestDomain { get; set; } = _defaultFuturesRestDomain;

	/// <summary>
	/// <see cref="MexcSections.Spot"/> web sockets domain.
	/// </summary>
	public string SpotWsDomain { get; set; } = _defaultSpotWsDomain;

	/// <summary>
	/// <see cref="MexcSections.Futures"/> web sockets domain.
	/// </summary>
	public string FuturesWsDomain { get; set; } = _defaultFuturesWsDomain;

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
			.Set(nameof(FuturesRestDomain), FuturesRestDomain)
			.Set(nameof(SpotWsDomain), SpotWsDomain)
			.Set(nameof(FuturesWsDomain), FuturesWsDomain)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Sections = [.. storage.GetValue<string>(nameof(Sections)).SplitByComma().Select(s => s.To<MexcSections>())];
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));

		RestDomain = storage.GetValue(nameof(RestDomain), RestDomain);
		FuturesRestDomain = storage.GetValue(nameof(FuturesRestDomain), FuturesRestDomain);
		SpotWsDomain = storage.GetValue(nameof(SpotWsDomain), SpotWsDomain);
		FuturesWsDomain = storage.GetValue(nameof(FuturesWsDomain), FuturesWsDomain);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}