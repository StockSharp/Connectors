namespace StockSharp.BingX;

/// <summary>
/// Sections.
/// </summary>
[DataContract]
[Serializable]
public enum BingXSections
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
/// The message adapter for <see cref="BingX"/>.
/// </summary>
[MediaIcon(Media.MediaNames.bingx)]
[Doc("topics/api/connectors/crypto_exchanges/bingx.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BingXKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BingXOrderCondition))]
public partial class BingXMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
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

	private IEnumerable<BingXSections> _sections = Enumerator.GetValues<BingXSections>();

	/// <summary>
	/// Sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[ItemsSource(typeof(BingXSections))]
	[BasicSetting]
	public IEnumerable<BingXSections> Sections
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

	private const string _defaultRestDomain = "open-api.bingx.com";
	private const string _defaultSpotWsDomain = "open-api-ws.bingx.com/market";
	private const string _defaultFuturesWsDomain = "open-api-swap.bingx.com/swap-market";

	/// <summary>
	/// REST domain.
	/// </summary>
	public string RestDomain { get; set; } = _defaultRestDomain;

	/// <summary>
	/// <see cref="BingXSections.Spot"/> web sockets domain.
	/// </summary>
	public string SpotWsDomain { get; set; } = _defaultSpotWsDomain;

	/// <summary>
	/// <see cref="BingXSections.Futures"/> web sockets domain.
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
		Sections = [.. storage.GetValue<string>(nameof(Sections)).SplitByComma().Select(s => s.To<BingXSections>())];
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));

		RestDomain = storage.GetValue(nameof(RestDomain), RestDomain);
		SpotWsDomain = storage.GetValue(nameof(SpotWsDomain), SpotWsDomain);
		FuturesWsDomain = storage.GetValue(nameof(FuturesWsDomain), FuturesWsDomain);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}