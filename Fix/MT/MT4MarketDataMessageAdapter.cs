namespace StockSharp.Fix.MT;

/// <summary>
/// MetaTrader 4 market data message adapter.
/// </summary>
[MediaIcon(Media.MediaNames.mt4)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MT4MarketDataKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[Doc("topics/api/connectors/forex/metatrader.html")]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks)]
public class MT4MarketDataMessageAdapter : FixMessageAdapter
{
	/// <summary>
	/// Initializes a new instance of the <see cref="MT4MarketDataMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public MT4MarketDataMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		Dialect = typeof(MTFixDialect);

		this.ChangeSupported(false, false);

		Address = MTAddresses.MT4;
		TargetCompId = "StockSharpMD";
		SenderCompId = "mql";
	}
}