namespace StockSharp.Bitget;

using Ecng.ComponentModel;

/// <summary>
/// Sections.
/// </summary>
[DataContract]
[Serializable]
public enum BitgetSections
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
/// The message adapter for <see cref="Bitget"/>.
/// </summary>
[MediaIcon(Media.MediaNames.bitget)]
[Doc("topics/api/connectors/crypto_exchanges/bitget.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitgetKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BitgetOrderCondition))]
public partial class BitgetMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
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

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PassphraseKey,
		Description = LocalizedStrings.PassphraseKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Passphrase { get; set; }

	private IEnumerable<BitgetSections> _sections = Enumerator.GetValues<BitgetSections>();

	/// <summary>
	/// Sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[ItemsSource(typeof(BitgetSections))]
	[BasicSetting]
	public IEnumerable<BitgetSections> Sections
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

	private const string _defaultRestDomain = "api.bitget.com";
	private const string _defaultSpotPublicWsEndpoint = "wss://ws.bitget.com/v2/ws/public";
	private const string _defaultSpotPrivateWsEndpoint = "wss://ws.bitget.com/v2/ws/private";
	private const string _defaultFuturesPublicWsEndpoint = "wss://ws.bitget.com/v2/ws/public";
	private const string _defaultFuturesPrivateWsEndpoint = "wss://ws.bitget.com/v2/ws/private";

	/// <summary>
	/// REST domain.
	/// </summary>
	public string RestDomain { get; set; } = _defaultRestDomain;

	/// <summary>
	/// <see cref="BitgetSections.Spot"/> public web sockets endpoint.
	/// </summary>
	public string SpotPublicWsEndpoint { get; set; } = _defaultSpotPublicWsEndpoint;

	/// <summary>
	/// <see cref="BitgetSections.Spot"/> private web sockets endpoint.
	/// </summary>
	public string SpotPrivateWsEndpoint { get; set; } = _defaultSpotPrivateWsEndpoint;

	/// <summary>
	/// <see cref="BitgetSections.Futures"/> public web sockets endpoint.
	/// </summary>
	public string FuturesPublicWsEndpoint { get; set; } = _defaultFuturesPublicWsEndpoint;

	/// <summary>
	/// <see cref="BitgetSections.Futures"/> private web sockets endpoint.
	/// </summary>
	public string FuturesPrivateWsEndpoint { get; set; } = _defaultFuturesPrivateWsEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Passphrase), Passphrase)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(IsDemo), IsDemo)

			.Set(nameof(RestDomain), RestDomain)
			.Set(nameof(SpotPublicWsEndpoint), SpotPublicWsEndpoint)
			.Set(nameof(SpotPrivateWsEndpoint), SpotPrivateWsEndpoint)
			.Set(nameof(FuturesPublicWsEndpoint), FuturesPublicWsEndpoint)
			.Set(nameof(FuturesPrivateWsEndpoint), FuturesPrivateWsEndpoint)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));
		Sections = [.. storage.GetValue<string>(nameof(Sections)).SplitByComma().Select(s => s.To<BitgetSections>())];
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));

		RestDomain = storage.GetValue(nameof(RestDomain), RestDomain);
		SpotPublicWsEndpoint = NormalizeWsEndpoint(storage.GetValue(nameof(SpotPublicWsEndpoint), SpotPublicWsEndpoint), _defaultSpotPublicWsEndpoint);
		SpotPrivateWsEndpoint = NormalizeWsEndpoint(storage.GetValue(nameof(SpotPrivateWsEndpoint), SpotPrivateWsEndpoint), _defaultSpotPrivateWsEndpoint);
		FuturesPublicWsEndpoint = NormalizeWsEndpoint(storage.GetValue(nameof(FuturesPublicWsEndpoint), FuturesPublicWsEndpoint), _defaultFuturesPublicWsEndpoint);
		FuturesPrivateWsEndpoint = NormalizeWsEndpoint(storage.GetValue(nameof(FuturesPrivateWsEndpoint), FuturesPrivateWsEndpoint), _defaultFuturesPrivateWsEndpoint);
	}

	private static string NormalizeWsEndpoint(string endpoint, string fallback)
	{
		if (endpoint.IsEmpty())
			endpoint = fallback;

		endpoint = endpoint.Trim();

		if (!endpoint.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
			!endpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
			endpoint = $"wss://{endpoint}";

		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}