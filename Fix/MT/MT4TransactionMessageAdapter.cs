namespace StockSharp.Fix.MT;

/// <summary>
/// MetaTrader 4 transactions message adapter.
/// </summary>
[MediaIcon(Media.MediaNames.mt4)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MT4TransactionsKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[Doc("topics/api/connectors/forex/metatrader.html")]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Free)]
public class MT4TransactionMessageAdapter : FixMessageAdapter
{
	/// <summary>
	/// Initializes a new instance of the <see cref="MT4TransactionMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public MT4TransactionMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		Dialect = typeof(MTFixDialect);

		this.ChangeSupported(false, true);

		Address = MTAddresses.MT4;
		TargetCompId = "StockSharpTS";
		SenderCompId = "mql";
	}
}