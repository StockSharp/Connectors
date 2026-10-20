namespace StockSharp.Connectors.Tests;

using System;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Backpack.Native.Model;

[TestClass]
public class BackpackRegressionTests : BaseTestClass
{
	[TestMethod]
	[DataRow(typeof(BackpackWsBookTicker), "bookTicker")]
	[DataRow(typeof(BackpackWsDepth), "depth")]
	[DataRow(typeof(BackpackWsTrade), "trade")]
	[DataRow(typeof(BackpackWsTicker), "ticker")]
	[DataRow(typeof(BackpackWsKline), "kline")]
	[DataRow(typeof(BackpackWsMarkPrice), "markPrice")]
	public void PublicEventTypeCannotOverwriteItsTimestamp(Type messageType, string eventType)
	{
		foreach (var payload in new[]
		{
			$"{{\"e\":\"{eventType}\",\"E\":1694687965941000}}",
			$"{{\"E\":1694687965941000,\"e\":\"{eventType}\"}}",
		})
		{
			var message = JsonConvert.DeserializeObject(payload, messageType);
			AreEqual(1694687965941000L, messageType.GetProperty("EventTime").GetValue(message));
		}
	}

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
