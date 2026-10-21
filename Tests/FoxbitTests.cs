namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Foxbit;
using StockSharp.Foxbit.Native;
using StockSharp.Foxbit.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Tests for the Foxbit connector.
/// </summary>
[TestClass]
public class FoxbitTests : LiveMarketDataTestBase
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

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new FoxbitMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCBRL",
		BoardCode = BoardCodes.Foxbit,
	};
}
