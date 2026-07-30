namespace StockSharp.Fix.Quik.Lua;

/// <summary>
/// Message adapter for QUIK LUA FIX.
/// </summary>
[MediaIcon(Media.MediaNames.quik)]
[Doc("topics/api/connectors/russia/quik.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QuikLuaMarketDataKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Russia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks)]
public class LuaFixMarketDataMessageAdapter : FixMessageAdapter
{
	/// <summary>
	/// Create <see cref="LuaFixMarketDataMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction identifiers generator.</param>
	public LuaFixMarketDataMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		Dialect = typeof(LuaFixDialect);

		this.ChangeSupported(false, false);

		Login = "quik";
		Password = "quik".Secure();
		Address = LuaFixExtensions.DefaultLuaAddress;
		TargetCompId = "StockSharpMD";
		SenderCompId = "quik";
		//ExchangeBoard = ExchangeBoard.Forts;
		//Version = FixVersions.Fix44_Lua;
		//RequestAllSecurities = true;
		//MarketData = FixMarketData.MarketData;
		//TimeZone = TimeHelper.Moscow;
	}
}