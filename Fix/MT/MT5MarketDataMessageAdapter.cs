namespace StockSharp.Fix.MT;

/// <summary>
/// MetaTrader 5 market data message adapter.
/// </summary>
[MediaIcon(Media.MediaNames.mt5)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MT5MarketDataKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[Doc("topics/api/connectors/forex/metatrader.html")]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks)]
public class MT5MarketDataMessageAdapter : FixMessageAdapter
{
	/// <summary>
	/// Initializes a new instance of the <see cref="MT5MarketDataMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public MT5MarketDataMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		Dialect = typeof(MTFixDialect);

		this.ChangeSupported(false, false);

		Address = MTAddresses.MT5;
		TargetCompId = "StockSharp";
		SenderCompId = "mql";
	}
}
