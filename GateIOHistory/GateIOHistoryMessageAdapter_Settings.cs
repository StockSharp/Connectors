namespace StockSharp.GateIOHistory;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for <see cref="GateIOHistory"/>.
/// </summary>
[MediaIcon(Media.MediaNames.gateio)]
[Doc("topics/api/connectors/crypto_exchanges/gateio_history.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GateIOHistoryKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
public partial class GateIOHistoryMessageAdapter : HistoricalMessageAdapter
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

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(CheckDates), CheckDates);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		CheckDates = storage.GetValue(nameof(CheckDates), CheckDates);
	}
}
