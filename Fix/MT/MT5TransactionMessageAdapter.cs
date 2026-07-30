namespace StockSharp.Fix.MT;

/// <summary>
/// MetaTrader 5 transactions message adapter.
/// </summary>
[MediaIcon(Media.MediaNames.mt5)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MT5TransactionsKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[Doc("topics/api/connectors/forex/metatrader.html")]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
    MessageAdapterCategories.Transactions | MessageAdapterCategories.Free)]
public class MT5TransactionMessageAdapter : FixMessageAdapter
{
	/// <summary>
	/// Initializes a new instance of the <see cref="MT5TransactionMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public MT5TransactionMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		Dialect = typeof(MTFixDialect);

		this.ChangeSupported(false, true);

		Address = MTAddresses.MT5;
		TargetCompId = "StockSharpTS";
		SenderCompId = "mql";
	}
}
