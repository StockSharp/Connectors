namespace StockSharp.AlorHistory;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for Alor history.
/// </summary>
[MediaIcon(Media.MediaNames.alor)]
[Doc("topics/api/connectors/russia/alorhistory.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AlorHistoryKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Russia | MessageAdapterCategories.History |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Free)]
public partial class AlorHistoryMessageAdapter
{
	private string[] _exchanges = ["MOEX"];

	/// <summary>
	/// Exchanges.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExchangeKey,
		Description = LocalizedStrings.ExchangeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string Exchanges
	{
		get => _exchanges.JoinComma();
		set => _exchanges = value.ThrowIfEmpty(nameof(value)).SplitByComma(true);
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.Set(nameof(Exchanges), Exchanges);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Exchanges = storage.GetValue(nameof(Exchanges), Exchanges);
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + ": " + Exchanges;
}
