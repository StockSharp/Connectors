namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Coincall;
using StockSharp.Coincall.Native;
using StockSharp.Messages;

[TestClass]
public class CoincallTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddresses()
	{
		var adapter = new CoincallMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://api.coincall.com",
			adapter.RestEndpoint);
		AreEqual(
			"wss://ws.coincall.com/options",
			adapter.OptionsWebSocketEndpoint);
		AreEqual(
			"wss://ws.coincall.com/futures",
			adapter.FuturesWebSocketEndpoint);
		AreEqual(
			TimeSpan.FromSeconds(5),
			adapter.RequestValidityWindow);
		AreEqual(
			CoincallProductTypes.Options,
			adapter.ProductType);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsAllAddresses()
	{
		var source = new CoincallMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "key".Secure(),
			Secret = "secret".Secure(),
			ProductType = CoincallProductTypes.Futures,
			RestEndpoint = "https://rest.example.test/",
			OptionsWebSocketEndpoint =
				"wss://options.example.test/",
			FuturesWebSocketEndpoint =
				"wss://futures.example.test/",
			RequestValidityWindow = TimeSpan.FromSeconds(3),
			PrivatePollingInterval = TimeSpan.FromSeconds(17),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new CoincallMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("key", target.Key.UnSecure());
		AreEqual("secret", target.Secret.UnSecure());
		AreEqual(
			CoincallProductTypes.Futures,
			target.ProductType);
		AreEqual(
			"https://rest.example.test",
			target.RestEndpoint);
		AreEqual(
			"wss://options.example.test",
			target.OptionsWebSocketEndpoint);
		AreEqual(
			"wss://futures.example.test",
			target.FuturesWebSocketEndpoint);
		AreEqual(
			TimeSpan.FromSeconds(3),
			target.RequestValidityWindow);
		AreEqual(
			TimeSpan.FromSeconds(17),
			target.PrivatePollingInterval);
	}

	[TestMethod]
	public void RestSignatureUsesDocumentedCanonicalString()
	{
		AreEqual(
			"6C73DD3EB26EFD14FBB41EFDEA5DAFE749FB48C1" +
				"FE7B7AC4A2E8CC22256FA5CF",
			CoincallRestClient.GenerateSignature(
				"GET",
				"/open/futures/leverage/current/v1",
				new[]
				{
					new KeyValuePair<string, string>(
						"symbol", "BTCUSD"),
				},
				"xdtHWn32rsuDQConutzl9JDZB+Y1leitFl356YHrmts=",
				1688436087184,
				3000,
				"fce1102b2a0dea92957fa7d2e981df826295cd85" +
					"696e40f0d521a6b8707b94c8"));
	}

	[TestMethod]
	public void WebSocketAuthenticationAndRequestsAreCanonical()
	{
		AreEqual(
			"5BD76787104337441E6415059099279FBDEB85878" +
				"A79CD148FD644F24A898CDE",
			CoincallWsClient.GenerateWebSocketSignature(
				"key", 1700000000000, "secret"));
		AreEqual(
			"wss://ws.example.test/options?code=10&uuid=key&" +
				"ts=1700000000000&sign=" +
				"5BD76787104337441E6415059099279FBDEB85878" +
				"A79CD148FD644F24A898CDE&apiKey=key",
			CoincallWsClient.CreateConnectionUri(
				"wss://ws.example.test/options",
				"key",
				"secret",
				1700000000000));
		AreEqual(
			"{\"action\":\"subscribe\",\"dataType\":\"kline\"," +
				"\"payload\":{\"symbol\":\"BTCUSD\"," +
				"\"period\":\"m5\"}}",
			CoincallWsClient.CreateSubscriptionJson(
				true, "kline", "btcusd", "m5"));
		AreEqual(
			"{\"action\":\"unSubscribe\",\"dataType\":\"order\"}",
			CoincallWsClient.CreateSubscriptionJson(
				false, "order", null));
	}

	[TestMethod]
	public void InstrumentsMapOptionsAndFutures()
	{
		const string optionJson =
			"{\"code\":0,\"msg\":\"Success\",\"data\":[{" +
			"\"baseCurrency\":\"BTC\"," +
			"\"expirationTimestamp\":1694678400000," +
			"\"strike\":22500," +
			"\"symbolName\":\"BTCUSD-14SEP23-22500-P\"," +
			"\"isActive\":true,\"minQty\":0.01," +
			"\"tickSize\":0.1}]}";
		const string futureJson =
			"{\"code\":0,\"data\":[{\"contractId\":1," +
			"\"symbol\":\"BTCUSD\",\"symbolName\":\"BTC/USD\"," +
			"\"displayName\":\"BTC-PERP\",\"baseToken\":\"BTC\"," +
			"\"quoteToken\":\"USD\",\"tickSize\":0.1," +
			"\"minQty\":0.001,\"lastPrice\":64000," +
			"\"openInterest\":100}]}";

		var option = CoincallRestClient
			.DeserializeInstruments(
				optionJson,
				CoincallProductTypes.Options)
			.Single();
		var future = CoincallRestClient
			.DeserializeInstruments(
				futureJson,
				CoincallProductTypes.Futures)
			.Single();

		AreEqual(
			"BTCUSD-14SEP23-22500-P",
			option.Symbol);
		AreEqual(SecurityTypes.Option, option.SecurityType);
		AreEqual(OptionTypes.Put, option.OptionType);
		AreEqual(22500m, option.Strike);
		AreEqual(SecurityTypes.Future, future.SecurityType);
		AreEqual(64000m, future.LastPrice);
		AreEqual(100m, future.OpenInterest);
	}

	[TestMethod]
	public void PublicRestShapesAreParsed()
	{
		const string bookJson =
			"{\"code\":0,\"data\":{\"optionName\":" +
			"\"BTCUSD-14SEP23-22500-P\",\"bids\":[{" +
			"\"size\":\"0.5\",\"price\":\"5251\"}]," +
			"\"asks\":[{\"size\":\"1\",\"price\":\"8888\"}]}}";
		const string tradeJson =
			"{\"code\":0,\"data\":[{\"symbol\":\"BTCUSD\"," +
			"\"price\":\"18898\",\"qty\":\"0.001\"," +
			"\"time\":1666754916559,\"tradeSide\":1}]}";
		const string candleJson =
			"{\"code\":0,\"data\":[{\"open\":10,\"close\":11," +
			"\"low\":9,\"high\":12,\"volume\":14.243," +
			"\"time\":1693526400000,\"period\":\"M1\"}]}";

		var book = CoincallRestClient.DeserializeBook(
			bookJson, "BTCUSD-14SEP23-22500-P");
		var trade = CoincallRestClient.DeserializeTrades(
			tradeJson, "BTCUSD").Single();
		var candle = CoincallRestClient.DeserializeCandles(
			candleJson,
			"BTCUSD",
			TimeSpan.FromMinutes(1)).Single();

		AreEqual(0.5m, book.Bids.Single().Volume);
		AreEqual(8888m, book.Asks.Single().Price);
		AreEqual(Sides.Buy, trade.Side);
		AreEqual(0.001m, trade.Volume);
		AreEqual(12m, candle.High);
		AreEqual(14.243m, candle.Volume);
	}

	[TestMethod]
	public void PrivateRestShapesAreParsed()
	{
		const string accountJson =
			"{\"code\":0,\"data\":{\"accounts\":[{" +
			"\"coin\":\"USDT\",\"equityAmount\":\"100\"," +
			"\"availableBalance\":\"70\",\"imAmount\":\"30\"," +
			"\"unrealizedAmount\":\"2\"}]}}";
		const string positionJson =
			"{\"code\":0,\"data\":[{\"positionId\":11," +
			"\"symbol\":\"BTCUSD\",\"qty\":0.5," +
			"\"avgPrice\":60000,\"markPrice\":61000," +
			"\"upnl\":500,\"tradeSide\":2,\"leverage\":5}]}";
		const string orderJson =
			"{\"code\":0,\"data\":{\"list\":[{\"orderId\":123," +
			"\"clientOrderId\":9,\"symbol\":\"BTCUSD\"," +
			"\"qty\":1,\"remainQty\":0.4,\"fillQty\":0.6," +
			"\"price\":500,\"tradeSide\":1,\"tradeType\":1," +
			"\"state\":2,\"createTime\":1685326195118}]}}";
		const string fillJson =
			"{\"code\":0,\"data\":{\"list\":[{\"id\":77," +
			"\"orderId\":123,\"symbol\":\"BTCUSD\"," +
			"\"price\":499,\"qty\":0.6,\"tradeSide\":1," +
			"\"time\":1685326195119,\"fee\":0.1}]}}";

		var account = CoincallRestClient
			.DeserializeAccounts(accountJson).Single();
		var position = CoincallRestClient
			.DeserializePositions(positionJson).Single();
		var order = CoincallRestClient
			.DeserializeOrders(orderJson).Single();
		var fill = CoincallRestClient
			.DeserializeFills(fillJson).Single();

		AreEqual(70m, account.Available);
		AreEqual(Sides.Sell, position.Side);
		AreEqual(-0.5m, position.SignedQuantity);
		AreEqual(OrderStates.Active, order.State);
		AreEqual(0.4m, order.RemainingQuantity);
		AreEqual(77L, fill.Id);
		AreEqual(0.1m, fill.Fee);
	}

	[TestMethod]
	public void WebSocketCompactMessagesAreParsed()
	{
		var book = CoincallWsClient.DeserializeMessage(
			"{\"dt\":32,\"c\":20,\"d\":{\"s\":\"BTCUSD\"," +
				"\"asks\":[{\"pr\":\"11\",\"sz\":\"3\"}]," +
				"\"bids\":[{\"pr\":\"10\",\"sz\":\"2\"}]," +
				"\"ts\":1688384138863}}").Book;
		var trade = CoincallWsClient.DeserializeMessage(
			"{\"dt\":43,\"c\":20,\"d\":[{" +
				"\"matchPrice\":\"10.5\",\"matchQty\":\"0.2\"," +
				"\"symbol\":\"BTCUSD\",\"tradeId\":\"77\"," +
				"\"tradeSide\":\"2\"," +
				"\"tradeTime\":\"1750763072834\"}]}").Trades.Single();
		var candle = CoincallWsClient.DeserializeMessage(
			"{\"dt\":31,\"c\":20,\"d\":{\"high\":12," +
				"\"s\":\"BTCUSD\",\"low\":9,\"pe\":\"m1\"," +
				"\"v\":5,\"close\":11,\"open\":10," +
				"\"ts\":1688383680000}}").Candle;
		var order = CoincallWsClient.DeserializeMessage(
			"{\"dt\":35,\"c\":20,\"d\":{\"coid\":9," +
				"\"oid\":123,\"s\":\"BTCUSD\",\"q\":\"1\"," +
				"\"rq\":\"0.4\",\"fq\":\"0.6\",\"pr\":\"500\"," +
				"\"si\":1,\"ty\":1,\"os\":2," +
				"\"ct\":1685326195118}}").Orders.Single();

		AreEqual(3m, book.Asks.Single().Volume);
		AreEqual(10m, book.Bids.Single().Price);
		AreEqual(Sides.Sell, trade.Side);
		AreEqual(0.2m, trade.Volume);
		AreEqual(TimeSpan.FromMinutes(1), candle.TimeFrame);
		AreEqual(11m, candle.Close);
		AreEqual(123L, order.Id);
		AreEqual(0.4m, order.RemainingQuantity);
	}
}
