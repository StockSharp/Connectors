namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Coinalyze;
using StockSharp.Coinalyze.Native;

[TestClass]
public class CoinalyzeTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsHonorPublishedRateLimit()
	{
		var adapter = new CoinalyzeMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://api.coinalyze.net/v1",
			adapter.RestEndpoint);
		AreEqual(
			TimeSpan.FromMilliseconds(1500),
			adapter.RequestInterval);
		AreEqual(CoinalyzeMarketTypes.Futures, adapter.MarketType);
		AreEqual(CoinalyzeCandleMetrics.Price, adapter.CandleMetric);
		IsTrue(adapter.ConvertToUsd);
		AreEqual(2000, adapter.HistoryLimit);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsHistorySelection()
	{
		var source = new CoinalyzeMessageAdapter(
			new IncrementalIdGenerator())
		{
			Token = "key".Secure(),
			RestEndpoint = "https://example.test/v1/",
			MarketType = CoinalyzeMarketTypes.Spot,
			CandleMetric =
				CoinalyzeCandleMetrics.LongShortRatio,
			Exchange = "A",
			ConvertToUsd = false,
			RequestInterval = TimeSpan.FromSeconds(2),
			MaximumItems = 321,
			HistoryLimit = 123,
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new CoinalyzeMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("key", target.Token.UnSecure());
		AreEqual(
			"https://example.test/v1",
			target.RestEndpoint);
		AreEqual(CoinalyzeMarketTypes.Spot, target.MarketType);
		AreEqual(
			CoinalyzeCandleMetrics.LongShortRatio,
			target.CandleMetric);
		AreEqual("A", target.Exchange);
		IsFalse(target.ConvertToUsd);
		AreEqual(321, target.MaximumItems);
		AreEqual(123, target.HistoryLimit);
	}

	[TestMethod]
	public void FutureMarketsRetainCanonicalSymbol()
	{
		const string json =
			"[{\"symbol\":\"BTCUSDT_PERP.A\"," +
			"\"exchange\":\"A\",\"symbol_on_exchange\":\"BTCUSDT\"," +
			"\"base_asset\":\"BTC\",\"quote_asset\":\"USDT\"," +
			"\"is_perpetual\":true,\"margined\":\"STABLE\"," +
			"\"expire_at\":0," +
			"\"oi_lq_vol_denominated_in\":\"BASE_ASSET\"," +
			"\"has_long_short_ratio_data\":true," +
			"\"has_ohlcv_data\":true," +
			"\"has_buy_sell_data\":true}]";

		var market = CoinalyzeRestClient.DeserializeMarkets(
			json,
			CoinalyzeMarketTypes.Futures).Single();

		AreEqual("BTCUSDT_PERP.A", market.Symbol);
		AreEqual("BTCUSDT", market.ExchangeSymbol);
		AreEqual("BTC", market.BaseAsset);
		AreEqual("USDT", market.QuoteAsset);
		IsTrue(market.IsPerpetual);
		IsTrue(market.HasLongShortRatio);
	}

	[TestMethod]
	public void OhlcvHistoryMapsVolumeAndTrades()
	{
		const string json =
			"[{\"symbol\":\"BTCUSDT_PERP.A\",\"history\":[{" +
			"\"t\":1722168000,\"o\":65000,\"h\":66000," +
			"\"l\":64000,\"c\":65500,\"v\":123.5," +
			"\"bv\":70.25,\"tx\":456,\"btx\":250}]}]";

		var candle = CoinalyzeRestClient.DeserializeHistory(
			json,
			CoinalyzeCandleMetrics.Price).Single();

		AreEqual(65000m, candle.Open);
		AreEqual(66000m, candle.High);
		AreEqual(64000m, candle.Low);
		AreEqual(65500m, candle.Close);
		AreEqual(123.5m, candle.Volume);
		AreEqual(70.25m, candle.BuyVolume);
		AreEqual(456, candle.Trades);
	}

	[TestMethod]
	public void OpenInterestHistoryUsesOhlcValues()
	{
		const string json =
			"[{\"symbol\":\"BTCUSDT_PERP.A\",\"history\":[{" +
			"\"t\":1722168000,\"o\":100,\"h\":120," +
			"\"l\":90,\"c\":110}]}]";

		var candle = CoinalyzeRestClient.DeserializeHistory(
			json,
			CoinalyzeCandleMetrics.OpenInterest).Single();

		AreEqual(100m, candle.Open);
		AreEqual(120m, candle.High);
		AreEqual(90m, candle.Low);
		AreEqual(110m, candle.Close);
	}

	[TestMethod]
	public void LiquidationHistoryRetainsBothSides()
	{
		const string json =
			"[{\"symbol\":\"BTCUSDT_PERP.A\",\"history\":[{" +
			"\"t\":1722168000,\"l\":25.5,\"s\":15.25}]}]";

		var candle = CoinalyzeRestClient.DeserializeHistory(
			json,
			CoinalyzeCandleMetrics.Liquidation).Single();

		AreEqual(25.5m, candle.Open);
		AreEqual(15.25m, candle.Close);
		AreEqual(40.75m, candle.High);
		AreEqual(40.75m, candle.Volume);
	}

	[TestMethod]
	public void LongShortHistoryRetainsRatioAndPercentages()
	{
		const string json =
			"[{\"symbol\":\"BTCUSDT_PERP.A\",\"history\":[{" +
			"\"t\":1722168000,\"r\":1.25,\"l\":55.56," +
			"\"s\":44.44}]}]";

		var candle = CoinalyzeRestClient.DeserializeHistory(
			json,
			CoinalyzeCandleMetrics.LongShortRatio).Single();

		AreEqual(1.25m, candle.Open);
		AreEqual(55.56m, candle.High);
		AreEqual(44.44m, candle.Low);
		AreEqual(1.25m, candle.Close);
	}
}
