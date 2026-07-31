namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinGlass;
using StockSharp.CoinGlass.Native;

[TestClass]
public class CoinGlassTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedV4Endpoint()
	{
		var adapter = new CoinGlassMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://open-api-v4.coinglass.com",
			adapter.RestEndpoint);
		AreEqual(CoinGlassMarketTypes.Futures, adapter.MarketType);
		AreEqual(CoinGlassCandleMetrics.Price, adapter.CandleMetric);
		AreEqual("Binance", adapter.Exchange);
		AreEqual("BTC", adapter.Symbol);
		AreEqual(1000, adapter.HistoryLimit);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsEndpointAndAnalytics()
	{
		var source = new CoinGlassMessageAdapter(
			new IncrementalIdGenerator())
		{
			Token = "key".Secure(),
			RestEndpoint = "https://example.test/v4/",
			MarketType = CoinGlassMarketTypes.Options,
			CandleMetric = CoinGlassCandleMetrics.OpenInterest,
			Exchange = "Deribit",
			Symbol = "eth",
			RequestInterval = TimeSpan.FromSeconds(1),
			PollingInterval = TimeSpan.FromMinutes(2),
			MaximumItems = 777,
			HistoryLimit = 321,
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new CoinGlassMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("key", target.Token.UnSecure());
		AreEqual(
			"https://example.test/v4",
			target.RestEndpoint);
		AreEqual(
			CoinGlassMarketTypes.Options,
			target.MarketType);
		AreEqual(
			CoinGlassCandleMetrics.OpenInterest,
			target.CandleMetric);
		AreEqual("Deribit", target.Exchange);
		AreEqual("ETH", target.Symbol);
		AreEqual(777, target.MaximumItems);
		AreEqual(321, target.HistoryLimit);
	}

	[TestMethod]
	public void FuturesPairsRetainNativeInstrumentIdentity()
	{
		const string json =
			"{\"code\":\"0\",\"msg\":\"success\",\"data\":{" +
			"\"Binance\":[{\"instrument_id\":\"BTCUSD_PERP\"," +
			"\"base_asset\":\"BTC\",\"quote_asset\":\"USD\"," +
			"\"settlement_currency\":\"USDT\"," +
			"\"max_leverage\":100,\"price_tick_size\":0.1}]}}";

		var instrument = CoinGlassRestClient.DeserializePairs(
			json,
			CoinGlassMarketTypes.Futures,
			"Binance").Single();

		AreEqual(
			"futures:Binance:BTCUSD_PERP",
			instrument.NativeId);
		AreEqual("BTC/USD@Binance", instrument.Symbol);
		AreEqual(0.1m, instrument.PriceStep);
		AreEqual(100m, instrument.MaxLeverage);
	}

	[TestMethod]
	public void PairMarketMapsDerivativesMetrics()
	{
		const string json =
			"{\"code\":\"0\",\"msg\":\"success\",\"data\":[{" +
			"\"instrument_id\":\"BTCUSDT\"," +
			"\"exchange_name\":\"Binance\"," +
			"\"symbol\":\"BTC/USDT\",\"current_price\":84604.3," +
			"\"index_price\":84646.6,\"volume_usd\":12345," +
			"\"price_change_percent_24h\":0.67," +
			"\"open_interest_usd\":6589095073.8," +
			"\"long_liquidation_usd_24h\":3654182.12," +
			"\"short_liquidation_usd_24h\":4099047.79," +
			"\"funding_rate\":0.002007," +
			"\"next_funding_time\":1744963200000}]}";

		var snapshot = CoinGlassRestClient
			.DeserializePairMarkets(
				json, CoinGlassMarketTypes.Futures)
			.Single();

		AreEqual(84604.3m, snapshot.LastPrice);
		AreEqual(84646.6m, snapshot.IndexPrice);
		AreEqual(6589095073.8m, snapshot.OpenInterest);
		AreEqual(0.002007m, snapshot.FundingRate);
		AreEqual(3654182.12m, snapshot.LongLiquidation);
		AreEqual(4099047.79m, snapshot.ShortLiquidation);
	}

	[TestMethod]
	public void OptionsInfoCreatesExchangeAnalyticsSecurities()
	{
		const string json =
			"{\"code\":\"0\",\"msg\":\"success\",\"data\":[{" +
			"\"exchange_name\":\"Deribit\"," +
			"\"open_interest\":262641.9," +
			"\"open_interest_usd\":23005403973.349," +
			"\"open_interest_change_24h\":2.57," +
			"\"volume_usd_24h\":2080336672.709}]}";

		var option = CoinGlassRestClient.DeserializeOptions(
			json, "BTC").Single();

		AreEqual(
			"options:Deribit:BTC-OPTIONS",
			option.NativeId);
		AreEqual("BTC-OPTIONS@Deribit", option.Symbol);
		AreEqual(23005403973.349m, option.OpenInterest);
		AreEqual(2080336672.709m, option.Volume);
	}

	[TestMethod]
	public void EtfListMapsTickerAndMarketSnapshot()
	{
		const string json =
			"{\"code\":\"0\",\"msg\":\"success\",\"data\":[{" +
			"\"ticker\":\"ETHA\"," +
			"\"name\":\"iShares Ethereum Trust ETF\"," +
			"\"primary_exchange\":\"XNAS\",\"price\":18.92," +
			"\"volume_quantity\":5592645," +
			"\"price_change_percent\":3.67," +
			"\"update_time\":1722995656637}]}";

		var etf = CoinGlassRestClient.DeserializeEtfs(
			json,
			CoinGlassMarketTypes.EthereumEtf).Single();

		AreEqual("ETHA", etf.Symbol);
		AreEqual("ETH", etf.BaseAsset);
		AreEqual("XNAS", etf.Exchange);
		AreEqual(18.92m, etf.LastPrice);
		AreEqual(5592645m, etf.Volume);
	}

	[TestMethod]
	public void OhlcAndLiquidationHistoriesAreParsed()
	{
		const string ohlcJson =
			"{\"code\":\"0\",\"msg\":\"success\",\"data\":[{" +
			"\"time\":1658880000000,\"open\":\"0.004603\"," +
			"\"high\":\"0.009388\",\"low\":\"-0.005063\"," +
			"\"close\":\"0.009229\"}]}";
		const string liquidationJson =
			"{\"code\":\"0\",\"msg\":\"success\",\"data\":[{" +
			"\"time\":1658880000000," +
			"\"long_liquidation_usd\":\"2369935.19562\"," +
			"\"short_liquidation_usd\":\"6947459.43674\"}]}";

		var ohlc = CoinGlassRestClient
			.DeserializeOhlc(ohlcJson)
			.Single();
		var liquidation = CoinGlassRestClient
			.DeserializeLiquidations(liquidationJson)
			.Single();

		AreEqual(0.004603m, ohlc.Open);
		AreEqual(-0.005063m, ohlc.Low);
		AreEqual(0.009229m, ohlc.Close);
		AreEqual(2369935.19562m, liquidation.Open);
		AreEqual(6947459.43674m, liquidation.Close);
		AreEqual(
			9317394.63236m,
			liquidation.High);
	}

	[TestMethod]
	public void OptionExchangeSeriesIsCaseInsensitive()
	{
		const string json =
			"{\"code\":\"0\",\"msg\":\"success\",\"data\":[{" +
			"\"time_list\":[1691460000000,1691463600000]," +
			"\"price_list\":[29140.9,29200.1]," +
			"\"data_map\":{\"deribit\":[15167.03,16000.5]}}]}";

		var series = CoinGlassRestClient.DeserializeSeries(
			json, "Deribit", false);

		AreEqual(2, series.Length);
		AreEqual(15167.03m, series[0].Close);
		AreEqual(16000.5m, series[1].Close);
	}
}
