namespace StockSharp.Kucoin;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Security;

using Ecng.ComponentModel;

/// <summary>
/// Sections.
/// </summary>
[DataContract]
[Serializable]
public enum KucoinSections
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
/// The message adapter for <see cref="Kucoin"/>.
/// </summary>
[MediaIcon(Media.MediaNames.kucoin)]
[Doc("topics/api/connectors/crypto_exchanges/kucoin.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KucoinKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions | MessageAdapterCategories.OrderLog)]
[OrderCondition(typeof(KucoinOrderCondition))]
public partial class KucoinMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter, IPassphraseAdapter
{
	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => Native.Spot.Extensions.TimeFrames.Keys.ToArray();

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
	/// Sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[ItemsSource(typeof(KucoinSections))]
	[BasicSetting]
	public IEnumerable<KucoinSections> Sections { get; set; } = Enumerator.GetValues<KucoinSections>();

	/// <summary>
	/// Spot address.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotKey,
		Description = LocalizedStrings.DomainAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string SpotAddress { get; set; } = "https://api.kucoin.com";

	/// <summary>
	/// Futures address.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey,
		Description = LocalizedStrings.DomainAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public string FuturesAddress { get; set; } = "https://api-futures.kucoin.com";

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Passphrase), Passphrase)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(SpotAddress), SpotAddress)
			.Set(nameof(FuturesAddress), FuturesAddress)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		SpotAddress = storage.GetValue(nameof(SpotAddress), SpotAddress);
		FuturesAddress = storage.GetValue(nameof(FuturesAddress), FuturesAddress);

		if (storage.ContainsKey(nameof(Sections)))
			Sections = storage.GetValue<string>(nameof(Sections)).SplitByComma().Select(s => s.To<KucoinSections>()).ToArray();
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}