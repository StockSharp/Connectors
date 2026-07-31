namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Coinmetro;
using StockSharp.Coinmetro.Native;
using StockSharp.Coinmetro.Native.Model;
using StockSharp.Messages;

[TestClass]
public class CoinmetroTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddresses()
	{
		var adapter = new CoinmetroMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.coinmetro.com",
			adapter.RestEndpoint);
		AreEqual("wss://api.coinmetro.com/ws",
			adapter.WebSocketEndpoint);
		AreEqual("https://api.coinmetro.com/open",
			adapter.DemoRestEndpoint);
		AreEqual("wss://api.coinmetro.com/open/ws",
			adapter.DemoWebSocketEndpoint);
		IsFalse(adapter.IsDemo);
		AreEqual(TimeSpan.FromMinutes(1),
			adapter.PrivatePollingInterval);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new CoinmetroMessageAdapter(
			new IncrementalIdGenerator())
		{
			Token = "token".Secure(),
			IsDemo = true,
			RestEndpoint = "https://rest.example.test/",
			WebSocketEndpoint = "wss://ws.example.test/",
			DemoRestEndpoint = "https://demo-rest.example.test/",
			DemoWebSocketEndpoint =
				"wss://demo-ws.example.test/",
			PrivatePollingInterval = TimeSpan.FromSeconds(75),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new CoinmetroMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("token", target.Token.UnSecure());
		IsTrue(target.IsDemo);
		AreEqual(
			"https://rest.example.test", target.RestEndpoint);
		AreEqual(
			"wss://ws.example.test", target.WebSocketEndpoint);
		AreEqual(
			"https://demo-rest.example.test",
			target.DemoRestEndpoint);
		AreEqual(
			"wss://demo-ws.example.test",
			target.DemoWebSocketEndpoint);
		AreEqual(TimeSpan.FromSeconds(75),
			target.PrivatePollingInterval);
	}

	[TestMethod]
	public void MarketsJoinAssetsAndCurrentPairMetadata()
	{
		const string assetsJson =
			"[{\"symbol\":\"BTC\",\"name\":\"Bitcoin\"," +
			"\"digits\":6,\"minQty\":0.00009},{" +
			"\"symbol\":\"EUR\",\"name\":\"Euro\",\"digits\":2," +
			"\"minQty\":5}]";
		const string marketsJson =
			"[{\"pair\":\"BTCEUR\",\"precision\":2," +
			"\"margin\":true}]";
		const string pricesJson =
			"{\"latestPrices\":[{\"pair\":\"BTCEUR\"," +
			"\"base\":\"BTC\",\"quote\":\"EUR\",\"price\":9842.81," +
			"\"qty\":0.001,\"timestamp\":1567791175553," +
			"\"ask\":9848.82,\"bid\":9836.54}]}";

		var markets = CoinmetroRestClient.CreateMarkets(
			CoinmetroRestClient.DeserializeAssets(assetsJson),
			CoinmetroRestClient.DeserializeMarketSpecs(marketsJson),
			CoinmetroRestClient.DeserializeTickers(pricesJson));

		AreEqual(1, markets.Length);
		AreEqual("BTC/EUR", markets[0].SecurityCode);
		AreEqual(2, markets[0].PricePrecision);
		AreEqual(6, markets[0].AmountPrecision);
		AreEqual(0.00009m, markets[0].MinimumAmount);
	}

	[TestMethod]
	public void LatestPricesExposeTickFields()
	{
		const string json =
			"{\"latestPrices\":[{\"pair\":\"BTCEUR\"," +
			"\"base\":\"BTC\",\"quote\":\"EUR\",\"price\":9842.81," +
			"\"qty\":0.00101597,\"timestamp\":1567791175553," +
			"\"seqNum\":96444449,\"ask\":9848.82," +
			"\"bid\":9836.54}]}";

		var ticker = CoinmetroRestClient
			.DeserializeTickers(json).Single();

		AreEqual(9842.81m, ticker.Price);
		AreEqual(0.00101597m, ticker.Volume);
		AreEqual(9848.82m, ticker.Ask);
		AreEqual(9836.54m, ticker.Bid);
	}

	[TestMethod]
	public void FullBookReadsPriceKeyedObjects()
	{
		const string json =
			"{\"book\":{\"pair\":\"BTCEUR\",\"seqNumber\":96446938," +
			"\"ask\":{\"9881.31\":0.55610377}," +
			"\"bid\":{\"9836.54\":0.91617929}," +
			"\"checksum\":-300309101}}";

		var book = CoinmetroRestClient.DeserializeBook(json);

		AreEqual(9836.54m, book.Bids[0].Price);
		AreEqual(0.91617929m, book.Bids[0].Volume);
		AreEqual(9881.31m, book.Asks[0].Price);
		AreEqual(96446938L, book.Sequence);
	}

	[TestMethod]
	public void TickHistoryUsesPublishedShape()
	{
		const string json =
			"{\"tickHistory\":[{\"pair\":\"BTCEUR\"," +
			"\"price\":9842.81,\"qty\":0.00101597," +
			"\"timestamp\":1567791175553,\"seqNum\":96444449}]}";

		var trades = CoinmetroRestClient.DeserializeTrades(json);

		AreEqual(1, trades.Length);
		AreEqual("96444449", trades[0].Id);
		AreEqual(9842.81m, trades[0].Price);
		AreEqual(0.00101597m, trades[0].Volume);
	}

	[TestMethod]
	public void CandlesUsePublishedShortFieldNames()
	{
		const string json =
			"{\"candleHistory\":[{\"timeframe\":60000," +
			"\"h\":9848.82,\"c\":9842.81," +
			"\"timestamp\":1567791000000,\"v\":1.5," +
			"\"l\":9836.54,\"o\":9840,\"pair\":\"BTCEUR\"}]}";

		var candle = CoinmetroRestClient
			.DeserializeCandles(json).Single();

		AreEqual(TimeSpan.FromMinutes(1), candle.TimeFrame);
		AreEqual(9840m, candle.Open);
		AreEqual(9848.82m, candle.High);
		AreEqual(9836.54m, candle.Low);
		AreEqual(9842.81m, candle.Close);
		AreEqual(1.5m, candle.Volume);
	}

	[TestMethod]
	public void WalletsExposeAvailableAndReserved()
	{
		const string json =
			"{\"list\":[{\"id\":\"wallet-1\",\"currency\":\"BTC\"," +
			"\"label\":\"BTC\",\"balance\":10,\"reserved\":1.25}]}";

		var wallet = CoinmetroRestClient
			.DeserializeWallets(json).Single();

		AreEqual("BTC", wallet.Currency);
		AreEqual(8.75m, wallet.Available);
		AreEqual(1.25m, wallet.Reserved);
		AreEqual(10m, wallet.Total);
	}

	[TestMethod]
	public void OrdersInferSidePriceStateAndBalance()
	{
		const string json =
			"[{\"orderType\":\"limit\",\"buyingCurrency\":\"BTC\"," +
			"\"sellingCurrency\":\"EUR\",\"buyingQty\":1," +
			"\"sellingQty\":100,\"orderID\":\"order-1\"," +
			"\"timeInForce\":1,\"boughtQty\":0.25,\"soldQty\":25," +
			"\"creationTime\":1567788344656," +
			"\"completionTime\":null}]";
		var market = new CoinmetroMarket
		{
			Pair = "BTCEUR",
			BaseCurrency = "BTC",
			QuoteCurrency = "EUR",
		};

		var order = CoinmetroRestClient
			.DeserializeOrders(json, [market]).Single();

		AreEqual(Sides.Buy, order.Side);
		AreEqual(100m, order.Price);
		AreEqual(1m, order.OriginalAmount);
		AreEqual(0.75m, order.RemainingAmount);
		AreEqual(OrderStates.Active, order.State);
	}

	[TestMethod]
	public void LimitSellFormUsesBaseAndQuoteQuantities()
	{
		var market = new CoinmetroMarket
		{
			Pair = "BTCEUR",
			BaseCurrency = "BTC",
			QuoteCurrency = "EUR",
		};

		var values = CoinmetroRestClient.CreateOrderForm(
			market,
			Sides.Sell,
			OrderTypes.Limit,
			2m,
			100m,
			TimeInForce.PutInQueue,
			null);

		AreEqual("limit", values["orderType"]);
		AreEqual("EUR", values["buyingCurrency"]);
		AreEqual("BTC", values["sellingCurrency"]);
		AreEqual("200", values["buyingQty"]);
		AreEqual("2", values["sellingQty"]);
		AreEqual("1", values["timeInForce"]);
	}

	[TestMethod]
	public void WebSocketEndpointAndMessagesUsePublishedShape()
	{
		AreEqual(
			"wss://api.coinmetro.com/ws?token=device%3Atoken&" +
			"pairs=BTCEUR",
			CoinmetroWsClient.CreateEndpoint(
				"wss://api.coinmetro.com/ws",
				"BTCEUR",
				"device:token"));

		const string json =
			"{\"bookUpdate\":{\"pair\":\"BTCEUR\"," +
			"\"seqNumber\":96446939,\"ask\":{\"9881.31\":-0.1}," +
			"\"bid\":{\"9836.54\":0.2},\"checksum\":123}}";
		var message = CoinmetroWsClient.DeserializeMessage(json);

		AreEqual("BTCEUR", message.BookUpdate.Pair);
		AreEqual(96446939L, message.BookUpdate.Sequence);
		AreEqual(-0.1m,
			message.BookUpdate.Asks.Single().Volume);
		AreEqual(0.2m,
			message.BookUpdate.Bids.Single().Volume);
	}
}
