namespace StockSharp.Connectors.Tests;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Foxbit.Native;
using StockSharp.Foxbit.Native.Model;
using StockSharp.Messages;

[TestClass]
public class FoxbitRegressionTests : BaseTestClass
{
	[TestMethod]
	[DataRow("STOP_MARKET")]
	[DataRow("STOP_LIMIT")]
	[DataRow("TRAILING_STOP_MARKET")]
	[DataRow("TRAILING_STOP_LIMIT")]
	public void ConditionalOrderTypesInMarketMetadataAreRecognized(string wireType)
	{
		var response = $$"""
			{"symbol":"btcbrl","order_type":["LIMIT","MARKET","{{wireType}}"]}
			""";
		var market = JsonConvert.DeserializeObject<FoxbitMarket>(response);

		AreEqual("btcbrl", market.Symbol);
		AreEqual(3, market.OrderTypes.Length);
		AreEqual(FoxbitOrderTypes.Limit, market.OrderTypes[0]);
		AreEqual(FoxbitOrderTypes.Market, market.OrderTypes[1]);
		FoxbitOrderTypes? conditionalType = market.OrderTypes[2];
		AreEqual(OrderTypes.Conditional, conditionalType.ToStockSharp());
	}
}
