namespace StockSharp.Fix.Quik.Lua;

/// <summary>
/// Message adapter for QUIK LUA FIX.
/// </summary>
[MediaIcon(Media.MediaNames.quik)]
[Doc("topics/api/connectors/russia/quik.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QuikLuaTransactionsKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Russia | MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
    MessageAdapterCategories.Transactions | MessageAdapterCategories.Free)]
public class LuaFixTransactionMessageAdapter : FixMessageAdapter
{
	/// <summary>
	/// Create <see cref="LuaFixTransactionMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction identifiers generator.</param>
	public LuaFixTransactionMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		Dialect = typeof(LuaFixDialect);

		this.ChangeSupported(false, true);

		Login = "quik";
		Password = "quik".Secure();
		Address = LuaFixExtensions.DefaultLuaAddress;
		TargetCompId = "StockSharpTS";
		SenderCompId = "quik";
		//ExchangeBoard = ExchangeBoard.Forts;
		//RequestAllPortfolios = true;
	}
}