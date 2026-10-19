namespace StockSharp.Connectors.Tests;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Backpack.Native.Model;

[TestClass]
public class BackpackRegressionTests : BaseTestClass
{
	[TestMethod]
	public void MarketListAcceptsIndexAlongsideCryptoAndStocks()
	{
		const string response = """
			[
				{"symbol":"BTC_USDC","marketType":"SPOT","rwaMarketType":null},
				{"symbol":"AAPL.US_USDC","marketType":"SPOT","rwaMarketType":"STOCK"},
				{"symbol":"SPY.US_USDC_PERP","marketType":"PERP","rwaMarketType":"INDEX"}
			]
			""";

		var markets = JsonConvert.DeserializeObject<BackpackMarket[]>(response);

		AreEqual(3, markets.Length);
		IsNull(markets[0].RealWorldAssetMarketType);
		AreEqual(BackpackRealWorldAssetMarketTypes.Stock, markets[1].RealWorldAssetMarketType.Value);
		AreEqual("SPY.US_USDC_PERP", markets[2].Symbol);
		AreEqual(BackpackMarketTypes.Perpetual, markets[2].MarketType);
		AreEqual("Index", markets[2].RealWorldAssetMarketType?.ToString());
	}
}
