namespace StockSharp.OkexHistory;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for <see cref="OkexHistory"/>.
/// </summary>
[MediaIcon(Media.MediaNames.okex)]
[Doc("topics/api/connectors/crypto_exchanges/okex_history.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OkexHistoryKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
public partial class OkexHistoryMessageAdapter : IAddressAdapter<string>
{
	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames.CachedKeys;

	private string _address = "www.okx.com";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string Address
	{
		get => _address;
		set => _address = value.ThrowIfEmpty(nameof(value));
	}

	/// <summary>
	/// Try to check available date range before downloading.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CheckDatesKey,
		Description = LocalizedStrings.CheckDatesDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public bool CheckDates { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Address), Address)
			.Set(nameof(CheckDates), CheckDates)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Address = storage.GetValue(nameof(Address), Address);
		CheckDates = storage.GetValue(nameof(CheckDates), CheckDates);
	}
}