namespace StockSharp.Huobi;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// Sections.
/// </summary>
[DataContract]
[Serializable]
public enum HuobiSections
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

	/// <summary>
	/// USDT Futures.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UsdtKey,
		Description = LocalizedStrings.UsdtSectionKey)]
	Usdt,

	///// <summary>
	///// Swap.
	///// </summary>
	//[EnumMember]
	//[Display(
	//	ResourceType = typeof(LocalizedStrings),
	//	Name = LocalizedStrings.SwapKey,
	//	Description = LocalizedStrings.SwapSectionKey
	//)]
	//Swap,
}

/// <summary>
/// The message adapter for <see cref="Huobi"/>.
/// </summary>
[MediaIcon(Media.MediaNames.huobi)]
[Doc("topics/api/connectors/crypto_exchanges/huobi.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.HuobiKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(HuobiOrderCondition))]
public partial class HuobiMessageAdapter : MessageAdapter, IKeySecretAdapter, IAddressAdapter<string>
{
	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => [.. Extensions.TimeFrames.Keys];

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

	private HuobiSections _section;

	/// <summary>
	/// Section.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		//Description = LocalizedStrings.SpotSectionKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public HuobiSections Section
	{
		get => _section;
		set
		{
			_section = value;

			Address = value == HuobiSections.Spot ? "api.huobi.pro" : "api.hbdm.com";
		}
	}

	private string _address;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DomainAddressKey,
		Description = LocalizedStrings.DomainAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string Address
	{
		get => _address;
		set
		{
			if (value.IsEmpty())
				throw new ArgumentNullException(nameof(value));

			_address = value;
			OnPropertyChanged(nameof(Address));
		}
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Section), Section.To<string>())
			.Set(nameof(Address), Address)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Section = storage.GetValue(nameof(Section), Section);
		Address = storage.GetValue(nameof(Address), Address);
	}

	/// <inheritdoc />
	public override string ToString() => base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
}