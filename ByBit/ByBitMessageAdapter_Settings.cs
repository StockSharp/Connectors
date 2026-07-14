namespace StockSharp.ByBit;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// Sections.
/// </summary>
[DataContract]
[Serializable]
public enum ByBitSections
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
	/// Linear.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LinearKey,
		Description = LocalizedStrings.LinearSectionKey)]
	Linear,

	/// <summary>
	/// Inverse.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.InverseKey,
		Description = LocalizedStrings.InverseSectionKey)]
	Inverse,

	/// <summary>
	/// Options.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OptionsKey,
		Description = LocalizedStrings.OptionsContractKey)]
	Options,
}

/// <summary>
/// The message adapter for <see cref="ByBit"/>.
/// </summary>
[MediaIcon(Media.MediaNames.bybit)]
[Doc("topics/api/connectors/crypto_exchanges/bybit.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ByBitKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions | MessageAdapterCategories.OrderLog)]
public partial class ByBitMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter, IAddressAdapter<string>
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
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	private static readonly ByBitSections[] _allSections = Enumerator.GetValues<ByBitSections>().ToArray();
	
	private IEnumerable<ByBitSections> _sections = _allSections;

	/// <summary>
	/// Sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[ItemsSource(typeof(ByBitSections))]
	[BasicSetting]
	public IEnumerable<ByBitSections> Sections
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
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public string Address { get; set; } = "bybit.com";

	private int _recvWindow = 50000;

	/// <summary>
	/// Time-out.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeOutKey,
		Description = LocalizedStrings.TimeOutKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int RecvWindow
	{
		get => _recvWindow;
		set
		{
			if (value <= 0)
				throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue);

			_recvWindow = value;
		}
	}

	/// <summary>
	/// Timestamp offset.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		//Name = LocalizedStrings.TimeStampKey,
		//Description = LocalizedStrings.TimeStampKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int TimeStampOffset { get; set; } = -5000;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(Address), Address)
			.Set(nameof(RecvWindow), RecvWindow)
			.Set(nameof(TimeStampOffset), TimeStampOffset)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		static string fix(string str)
		{
			if (str == "Usd")
				return nameof(ByBitSections.Linear);
			else if (str == "Futures")
				return nameof(ByBitSections.Inverse);
			else
				return str;
		}

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		Sections = storage.GetValue<string>(nameof(Sections)).SplitByComma().Select(s => fix(s).To<ByBitSections>()).ToArray();
		Address = storage.GetValue<string>(nameof(Address));
		RecvWindow = storage.GetValue(nameof(RecvWindow), RecvWindow);
		TimeStampOffset = storage.GetValue(nameof(TimeStampOffset), TimeStampOffset);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{base.ToString()}: {LocalizedStrings.Key} = {Key.ToId()}";
	}
}