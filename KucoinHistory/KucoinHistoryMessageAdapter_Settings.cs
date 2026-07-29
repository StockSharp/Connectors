namespace StockSharp.KucoinHistory;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for <see cref="KucoinHistory"/>.
/// </summary>
[MediaIcon(Media.MediaNames.kucoin)]
[Doc("topics/api/connectors/crypto_exchanges/kucoin_history.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KucoinHistoryKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
public partial class KucoinHistoryMessageAdapter : HistoricalMessageAdapter
{
	/// <summary>
	/// Check dates.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CheckDatesKey,
		Description = LocalizedStrings.CheckDatesDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public bool CheckDates { get; set; } = true;

	private string _apiAddress = "api.kucoin.com";

	/// <summary>
	/// API address for securities lookup.
	/// </summary>
	[Browsable(false)]
	public string ApiAddress
	{
		get => _apiAddress;
		set => _apiAddress = value.ThrowIfEmpty(nameof(value));
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(CheckDates), CheckDates);
		storage.SetValue(nameof(ApiAddress), ApiAddress);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		CheckDates = storage.GetValue(nameof(CheckDates), CheckDates);
		ApiAddress = storage.GetValue(nameof(ApiAddress), ApiAddress);
	}
}
