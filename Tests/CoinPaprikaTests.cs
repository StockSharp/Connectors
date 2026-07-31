namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinPaprika;
using StockSharp.CoinPaprika.Native;

[TestClass]
public class CoinPaprikaTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedFreeEndpoint()
	{
		var adapter = new CoinPaprikaMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://api.coinpaprika.com/v1",
			adapter.RestEndpoint);
		AreEqual("USD", adapter.QuoteCurrency);
		AreEqual(2000, adapter.MaximumItems);
		AreEqual(366, adapter.HistoryLimit);
		AreEqual(
			TimeSpan.FromMinutes(5),
			adapter.PollingInterval);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsEndpointAndFilters()
	{
		var source = new CoinPaprikaMessageAdapter(
			new IncrementalIdGenerator())
		{
			Token = "token".Secure(),
			RestEndpoint =
				"https://api-pro.example.test/v1/",
			QuoteCurrency = "eur",
			ExchangeId = "binance",
			RequestInterval =
				TimeSpan.FromMilliseconds(750),
			PollingInterval = TimeSpan.FromMinutes(2),
			MaximumItems = 321,
			HistoryLimit = 123,
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new CoinPaprikaMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("token", target.Token.UnSecure());
		AreEqual(
			"https://api-pro.example.test/v1",
			target.RestEndpoint);
		AreEqual("EUR", target.QuoteCurrency);
		AreEqual("binance", target.ExchangeId);
		AreEqual(
			TimeSpan.FromMilliseconds(750),
			target.RequestInterval);
		AreEqual(
			TimeSpan.FromMinutes(2),
			target.PollingInterval);
		AreEqual(321, target.MaximumItems);
		AreEqual(123, target.HistoryLimit);
	}

	[TestMethod]
	public void CoinsMapToNativeIdsAndQuoteSecurities()
	{
		const string json =
			"[{\"id\":\"btc-bitcoin\",\"name\":\"Bitcoin\"," +
			"\"symbol\":\"BTC\",\"rank\":1,\"is_active\":true," +
			"\"type\":\"coin\"},{\"id\":\"usdt-tether\"," +
			"\"name\":\"Tether\",\"symbol\":\"USDT\",\"rank\":3," +
			"\"is_active\":false,\"type\":\"token\"}]";

		var coins = CoinPaprikaRestClient.DeserializeCoins(
			json, "usd");

		AreEqual(2, coins.Length);
		AreEqual("coin:btc-bitcoin:USD", coins[0].NativeId);
		AreEqual("BTC/USD", coins[0].Symbol);
		AreEqual("Bitcoin", coins[0].Name);
		IsTrue(coins[0].IsActive);
		IsFalse(coins[1].IsActive);
	}

	[TestMethod]
	public void ExchangeMarketsRetainPairIdentity()
	{
		const string json =
			"[{\"pair\":\"BTC/USDT\"," +
			"\"base_currency_id\":\"btc-bitcoin\"," +
			"\"base_currency_name\":\"Bitcoin\"," +
			"\"quote_currency_id\":\"usdt-tether\"," +
			"\"quote_currency_name\":\"Tether\"," +
			"\"category\":\"Spot\",\"outlier\":false," +
			"\"quotes\":{\"USD\":{\"price\":64000," +
			"\"volume_24h\":123456}}," +
			"\"last_updated\":\"2026-07-28T12:00:00Z\"}]";

		var market = CoinPaprikaRestClient
			.DeserializeMarkets(json, "binance", "USD")
			.Single();

		AreEqual(
			"market:binance:btc-bitcoin:usdt-tether",
			market.NativeId);
		AreEqual("BTC/USDT@binance", market.Symbol);
		AreEqual("binance", market.ExchangeId);
		AreEqual(64000m, market.Price);
		AreEqual(123456m, market.Volume24Hours);
	}

	[TestMethod]
	public void TickerMapsSelectedQuote()
	{
		const string json =
			"{\"id\":\"btc-bitcoin\",\"name\":\"Bitcoin\"," +
			"\"symbol\":\"BTC\",\"rank\":1," +
			"\"last_updated\":\"2026-07-28T12:00:00Z\"," +
			"\"quotes\":{\"USD\":{\"price\":64000.5," +
			"\"volume_24h\":1000,\"market_cap\":1200000," +
			"\"percent_change_24h\":2.5}," +
			"\"EUR\":{\"price\":59000}}}";

		var ticker = CoinPaprikaRestClient.DeserializeTicker(
			json, "USD");

		AreEqual("BTC/USD", ticker.Symbol);
		AreEqual(64000.5m, ticker.Price);
		AreEqual(1000m, ticker.Volume24Hours);
		AreEqual(1200000m, ticker.MarketCap);
		AreEqual(2.5m, ticker.Change24Hours);
	}

	[TestMethod]
	public void HistoricalOhlcvIsParsed()
	{
		const string json =
			"[{\"time_open\":\"2026-07-27T00:00:00Z\"," +
			"\"time_close\":\"2026-07-27T23:59:59Z\"," +
			"\"open\":60000,\"high\":65000,\"low\":59000," +
			"\"close\":64000,\"volume\":1234," +
			"\"market_cap\":1200000}]";

		var candle = CoinPaprikaRestClient
			.DeserializeCandles(json)
			.Single();

		AreEqual(
			new DateTime(
				2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
			candle.OpenTime);
		AreEqual(65000m, candle.High);
		AreEqual(59000m, candle.Low);
		AreEqual(64000m, candle.Close);
		AreEqual(1234m, candle.Volume);
		AreEqual(1200000m, candle.MarketCap);
	}
}
