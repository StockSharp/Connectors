namespace StockSharp.ByBitHistory;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for <see cref="ByBitHistory"/>.
/// </summary>
[MediaIcon(Media.MediaNames.bybit)]
[Doc("topics/api/connectors/crypto_exchanges/bybit_history.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ByBitHistoryKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
public partial class ByBitHistoryMessageAdapter : HistoricalMessageAdapter
{
}