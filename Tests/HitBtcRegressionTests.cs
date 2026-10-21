namespace StockSharp.Connectors.Tests;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.HitBtc.Native;

[TestClass]
public class HitBtcRegressionTests : BaseTestClass
{
	[TestMethod]
	[DataRow("trades", true, 100)]
	[DataRow("trades", false, null)]
	[DataRow("ticker/1s", true, null)]
	[DataRow("orderbook/full", true, null)]
	public void TradeSubscriptionsRequestAnInitialSnapshot(string channel, bool subscribe, int? expectedLimit)
	{
		var request = HitBtcSocketClient.CreatePublicSubscription(channel, "UNIUSDT", subscribe);

		AreEqual(1, request.Symbols.Length);
		AreEqual("UNIUSDT", request.Symbols[0]);
		AreEqual(expectedLimit, request.Limit);
	}
}
