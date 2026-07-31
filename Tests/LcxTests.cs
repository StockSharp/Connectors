namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.LCX;
using StockSharp.LCX.Native;
using StockSharp.Messages;

[TestClass]
public class LcxTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddresses()
	{
		var adapter = new LcxMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://exchange-api.lcx.com",
			adapter.RestEndpoint);
		AreEqual(
			"https://api-kline.lcx.com",
			adapter.KlineEndpoint);
		AreEqual(
			"wss://exchange-api.lcx.com",
			adapter.WebSocketEndpoint);
		AreEqual("1.1.0", adapter.ApiVersion);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new LcxMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "key".Secure(),
			Secret = "secret".Secure(),
			RestEndpoint = "https://rest.example.test/",
			KlineEndpoint = "https://kline.example.test/",
			WebSocketEndpoint = "wss://ws.example.test/",
			ApiVersion = "1.1.2",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new LcxMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("key", target.Key.UnSecure());
		AreEqual("secret", target.Secret.UnSecure());
		AreEqual(
			"https://rest.example.test",
			target.RestEndpoint);
		AreEqual(
			"https://kline.example.test",
			target.KlineEndpoint);
		AreEqual(
			"wss://ws.example.test",
			target.WebSocketEndpoint);
		AreEqual("1.1.2", target.ApiVersion);
	}

	[TestMethod]
	public void SignatureMatchesOfficialAlgorithm()
	{
		const string body =
			"{\"Pair\":\"LCX/EUR\",\"Amount\":10," +
			"\"Price\":0.004,\"OrderType\":\"LIMIT\"," +
			"\"Side\":\"BUY\",\"ClientOrderId\":" +
			"\"00000000-0000-0000-0000-000000000001\"}";

		AreEqual(
			"DtqgOFkXVHP/RkA34uChGEFHghhaZWA1uCo3OIxLlKo=",
			LcxRestClient.GenerateSignature(
				"POST", "/api/create", body, "secret"));
	}

	[TestMethod]
	public void PairsUsePublishedPrecisionShape()
	{
		const string json =
			"{\"status\":\"success\",\"data\":[{" +
			"\"Id\":\"pair-id\",\"Symbol\":\"LCX/EUR\"," +
			"\"Base\":\"LCX\",\"Quote\":\"EUR\"," +
			"\"Precision\":{\"Amount\":4,\"Price\":6}," +
			"\"Orderprecision\":{\"Amount\":2,\"Price\":5}," +
			"\"MinOrder\":{\"Base\":10,\"Quote\":1}," +
			"\"MaxOrder\":{\"Base\":100000,\"Quote\":50000}," +
			"\"Status\":true,\"Mode\":\"trade\"}]}";

		var market = LcxRestClient
			.DeserializeMarkets(json).Single();

		AreEqual("LCX/EUR", market.Symbol);
		AreEqual(5, market.PricePrecision);
		AreEqual(2, market.AmountPrecision);
		AreEqual(10m, market.MinimumAmount);
		IsTrue(market.IsActive);
	}

	[TestMethod]
	public void TickersUsePairNamedObject()
	{
		const string json =
			"{\"status\":\"success\",\"data\":{\"LCX/EUR\":{" +
			"\"bestAsk\":0.101,\"bestBid\":0.099," +
			"\"lastPrice\":0.1,\"lastUpdated\":1700025221," +
			"\"high\":0.11,\"low\":0.09,\"volume\":5079.69," +
			"\"change\":-3.84,\"symbol\":\"LCX/EUR\"}}}";

		var ticker = LcxRestClient
			.DeserializeTickers(json).Single();

		AreEqual("LCX/EUR", ticker.Symbol);
		AreEqual(0.1m, ticker.LastPrice);
		AreEqual(0.099m, ticker.Bid);
		AreEqual(0.101m, ticker.Ask);
		AreEqual(5079.69m, ticker.Volume);
	}

	[TestMethod]
	public void BookAndTradesUseArrayLevels()
	{
		const string bookJson =
			"{\"status\":\"success\",\"data\":{" +
			"\"buy\":[[0.099,3]],\"sell\":[[0.101,4]]}}";
		const string tradesJson =
			"{\"status\":\"success\",\"data\":[" +
			"[0.1,2,\"BUY\",1700025221]]}";

		var book = LcxRestClient.DeserializeBook(bookJson);
		var trade = LcxRestClient
			.DeserializePublicTrades(
				tradesJson, "LCX/EUR").Single();

		AreEqual(0.099m, book.Bids.Single().Price);
		AreEqual(3m, book.Bids.Single().Volume);
		AreEqual(0.101m, book.Asks.Single().Price);
		AreEqual(Sides.Buy, trade.Side);
		AreEqual(2m, trade.Volume);
	}

	[TestMethod]
	public void CandlesUsePublishedShape()
	{
		const string json =
			"{\"count\":1,\"status\":\"success\",\"data\":[{" +
			"\"close\":0.1,\"high\":0.11,\"low\":0.09," +
			"\"open\":0.095,\"pair\":\"LCX/EUR\"," +
			"\"timeframe\":\"60\",\"timestamp\":1605722400000," +
			"\"volume\":10}]}";

		var candle = LcxRestClient
			.DeserializeCandles(json).Single();

		AreEqual(TimeSpan.FromHours(1), candle.TimeFrame);
		AreEqual(0.095m, candle.Open);
		AreEqual(0.11m, candle.High);
		AreEqual(0.09m, candle.Low);
		AreEqual(0.1m, candle.Close);
	}

	[TestMethod]
	public void BalancesExposeFreeAndOccupiedValues()
	{
		const string json =
			"{\"status\":\"success\",\"data\":[{" +
			"\"balance\":{\"freeBalance\":9," +
			"\"occupiedBalance\":1,\"totalBalance\":10}," +
			"\"coin\":\"LCX\",\"fullName\":\"LCX Token\"}]}";

		var balance = LcxRestClient
			.DeserializeBalances(json).Single();

		AreEqual("LCX", balance.Currency);
		AreEqual(9m, balance.Available);
		AreEqual(1m, balance.Blocked);
		AreEqual(10m, balance.Total);
	}

	[TestMethod]
	public void OrdersAndUserTradesUsePublishedFields()
	{
		const string orderJson =
			"{\"status\":\"success\",\"data\":{" +
			"\"Id\":\"order-1\",\"ClientOrderId\":\"client-1\"," +
			"\"Pair\":\"LCX/EUR\",\"Price\":0.1,\"Amount\":100," +
			"\"Side\":\"BUY\",\"OrderType\":\"LIMIT\"," +
			"\"Status\":\"OPEN\",\"Filled\":25," +
			"\"CreatedAt\":1699955183,\"Fee\":0.5}}";
		const string tradeJson =
			"{\"status\":\"success\",\"data\":[{" +
			"\"Id\":\"trade-1\",\"OrderId\":\"order-1\"," +
			"\"Pair\":\"LCX/EUR\",\"Price\":0.1,\"Amount\":25," +
			"\"Side\":\"BUY\",\"CreatedAt\":1699955183," +
			"\"Fee\":0.1,\"FeeCoin\":\"LCX\"}]}";

		var order = LcxRestClient
			.DeserializeOrders(orderJson).Single();
		var trade = LcxRestClient
			.DeserializeUserTrades(tradeJson).Single();

		AreEqual(OrderStates.Active, order.State);
		AreEqual(75m, order.RemainingAmount);
		AreEqual("client-1", order.ClientOrderId);
		AreEqual("order-1", trade.OrderId);
		AreEqual("LCX", trade.FeeCurrency);
	}

	[TestMethod]
	public void WebSocketProtocolUsesOfficialMessages()
	{
		AreEqual(
			"{\"Topic\":\"subscribe\",\"Type\":\"orderbook\"," +
			"\"Pair\":\"LCX/EUR\"}",
			LcxWsClient.CreateSubscription(
				"orderbook", "LCX/EUR", true));

		var message = LcxWsClient.DeserializeMessage(
			"{\"type\":\"trade\",\"topic\":\"update\"," +
			"\"pair\":\"LCX/EUR\",\"data\":[" +
			"[0.1,2,\"BUY\",1700025221]]}");

		AreEqual("trade", message.Type);
		AreEqual("LCX/EUR", message.Pair);
		AreEqual(1, message.Trades.Length);
		AreEqual(0.1m, message.Trades[0].Price);
	}

	[TestMethod]
	public void PrivateWebSocketEndpointUsesSignedQuery()
	{
		var endpoint = LcxWsClient.CreatePrivateEndpoint(
			"wss://exchange-api.lcx.com",
			"api key",
			"signature/+",
			1700000000000);

		AreEqual(
			"wss://exchange-api.lcx.com/api/auth/ws?" +
			"x-access-key=api%20key&" +
			"x-access-sign=signature%2F%2B&" +
			"x-access-timestamp=1700000000000",
			endpoint);
	}
}
