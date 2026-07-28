namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.WazirX;
using StockSharp.WazirX.Native;

[TestClass]
public class WazirXTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddresses()
	{
		var adapter = new WazirXMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://api.wazirx.com",
			adapter.RestEndpoint);
		AreEqual(
			"wss://stream.wazirx.com/stream",
			adapter.WebSocketEndpoint);
		AreEqual(5000L, adapter.ReceiveWindow);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new WazirXMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "key".Secure(),
			Secret = "secret".Secure(),
			RestEndpoint = "https://rest.example.test/",
			WebSocketEndpoint = "wss://ws.example.test/",
			ReceiveWindow = 12000,
			PrivatePollingInterval = TimeSpan.FromSeconds(15),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new WazirXMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("key", target.Key.UnSecure());
		AreEqual("secret", target.Secret.UnSecure());
		AreEqual(
			"https://rest.example.test",
			target.RestEndpoint);
		AreEqual(
			"wss://ws.example.test",
			target.WebSocketEndpoint);
		AreEqual(12000L, target.ReceiveWindow);
		AreEqual(
			TimeSpan.FromSeconds(15),
			target.PrivatePollingInterval);
	}

	[TestMethod]
	public void SignatureUsesPublishedHmacSha256Scheme()
	{
		AreEqual(
			"7dbd1c1027da36d94b93c88a7b8499a9682e1422" +
				"eee6921b07cfaa1133a52bea",
			WazirXRestClient.GenerateSignature(
				"symbol=btcinr&timestamp=1632376923837",
				"secret"));
	}

	[TestMethod]
	public void ExchangeInfoUsesPublishedFilters()
	{
		const string json =
			"{\"timezone\":\"UTC\",\"serverTime\":1631531599247," +
			"\"symbols\":[{\"symbol\":\"btcinr\"," +
			"\"status\":\"trading\",\"baseAsset\":\"btc\"," +
			"\"quoteAsset\":\"inr\",\"baseAssetPrecision\":5," +
			"\"quoteAssetPrecision\":0,\"orderTypes\":[" +
			"\"limit\",\"stop_limit\"]," +
			"\"isSpotTradingAllowed\":true,\"filters\":[{" +
			"\"filterType\":\"PRICE_FILTER\",\"minPrice\":\"1\"," +
			"\"tickSize\":\"1\"}]}]}";

		var market = WazirXRestClient
			.DeserializeMarkets(json).Single();

		AreEqual("btcinr", market.Symbol);
		AreEqual("btc", market.BaseAsset);
		AreEqual("inr", market.QuoteAsset);
		AreEqual(1m, market.PriceStep);
		IsTrue(market.IsActive);
		IsTrue(market.SupportsStopLimit);
	}

	[TestMethod]
	public void TickerUsesPublishedShape()
	{
		const string json =
			"[{\"symbol\":\"btcinr\",\"baseAsset\":\"btc\"," +
			"\"quoteAsset\":\"inr\",\"openPrice\":\"704999.0\"," +
			"\"lowPrice\":\"702603.0\",\"highPrice\":\"730001.0\"," +
			"\"lastPrice\":\"720101.0\",\"volume\":\"891.8329\"," +
			"\"bidPrice\":\"720102.0\",\"askPrice\":\"722999.0\"," +
			"\"at\":1588829734}]";

		var ticker = WazirXRestClient
			.DeserializeTickers(json).Single();

		AreEqual("btcinr", ticker.Symbol);
		AreEqual(720101m, ticker.LastPrice);
		AreEqual(720102m, ticker.BidPrice);
		AreEqual(722999m, ticker.AskPrice);
		AreEqual(891.8329m, ticker.Volume);
	}

	[TestMethod]
	public void BookAndTradesUsePublishedShapes()
	{
		const string bookJson =
			"{\"lastUpdateAt\":1588831243," +
			"\"asks\":[[\"9291.0\",\"0.0119\"]]," +
			"\"bids\":[[\"9253.0\",\"1.0456\"]]}";
		const string tradesJson =
			"[{\"id\":28457,\"price\":\"4.00000100\"," +
			"\"qty\":\"12.00000000\",\"quoteQty\":\"48.000012\"," +
			"\"time\":1499865549590,\"isBuyerMaker\":true}]";

		var book = WazirXRestClient
			.DeserializeBook(bookJson, "wrxinr");
		var trade = WazirXRestClient
			.DeserializeTrades(tradesJson, "wrxinr").Single();

		AreEqual(9253m, book.Bids.Single().Price);
		AreEqual(1.0456m, book.Bids.Single().Volume);
		AreEqual(9291m, book.Asks.Single().Price);
		AreEqual(Sides.Sell, trade.Side);
		AreEqual(12m, trade.Volume);
	}

	[TestMethod]
	public void CandlesUseSecondsInPublishedRestShape()
	{
		const string json =
			"[[1647822960,20,21,19,20.5,3]]";

		var candle = WazirXRestClient
			.DeserializeCandles(json).Single();

		AreEqual(
			DateTimeOffset.FromUnixTimeSeconds(1647822960)
				.UtcDateTime,
			candle.OpenTime);
		AreEqual(20m, candle.Open);
		AreEqual(21m, candle.High);
		AreEqual(19m, candle.Low);
		AreEqual(20.5m, candle.Close);
		AreEqual(3m, candle.Volume);
	}

	[TestMethod]
	public void OrdersAndBalancesUsePublishedFields()
	{
		const string orderJson =
			"{\"id\":28,\"clientOrderId\":\"client-1\"," +
			"\"symbol\":\"wrxinr\",\"price\":\"9293.0\"," +
			"\"origQty\":\"10.0\",\"executedQty\":\"8.2\"," +
			"\"status\":\"wait\",\"type\":\"limit\"," +
			"\"side\":\"sell\",\"createdTime\":1499827319559," +
			"\"updatedTime\":1499827319559}";
		const string balanceJson =
			"[{\"asset\":\"btc\",\"free\":\"9.0\"," +
			"\"locked\":\"1.0\"}]";

		var order = WazirXRestClient
			.DeserializeOrders(orderJson).Single();
		var balance = WazirXRestClient
			.DeserializeBalances(balanceJson).Single();

		AreEqual(28L, order.Id);
		AreEqual(OrderStates.Active, order.State);
		AreEqual(1.8m, order.RemainingVolume);
		AreEqual("btc", balance.Asset);
		AreEqual(9m, balance.Available);
		AreEqual(1m, balance.Locked);
	}

	[TestMethod]
	public void WebSocketSubscriptionUsesPublishedProtocol()
	{
		AreEqual(
			"{\"event\":\"subscribe\",\"streams\":[" +
				"\"btcinr@trades\"],\"auth_key\":\"auth\"}",
			WazirXWsClient.CreateSubscription(
				["btcinr@trades"], true, "auth"));
	}

	[TestMethod]
	public void WebSocketParsesPublicMarketMessages()
	{
		var message = WazirXWsClient.DeserializeMessage(
			"{\"data\":{\"trades\":[{\"E\":1631681323000," +
			"\"m\":true,\"p\":\"7.0\",\"q\":\"15.0\"," +
			"\"s\":\"btcinr\",\"t\":17376030}]}," +
			"\"stream\":\"btcinr@trades\"}");

		AreEqual(1, message.Trades.Length);
		AreEqual("btcinr", message.Trades[0].Symbol);
		AreEqual(17376030L, message.Trades[0].Id);
		AreEqual(Sides.Sell, message.Trades[0].Side);
	}

	[TestMethod]
	public void WebSocketParsesPrivateMessages()
	{
		var message = WazirXWsClient.DeserializeMessage(
			"{\"data\":{\"E\":1631683058904," +
			"\"O\":1631683058000,\"S\":\"sell\"," +
			"\"V\":\"70.0\",\"X\":\"wait\",\"i\":26946170," +
			"\"c\":\"client-1\",\"o\":\"limit\",\"p\":\"5.0\"," +
			"\"q\":\"70.0\",\"s\":\"wrxinr\",\"z\":\"0.0\"}," +
			"\"stream\":\"orderUpdate\"}");

		AreEqual(26946170L, message.Order.Id);
		AreEqual("wrxinr", message.Order.Symbol);
		AreEqual(Sides.Sell, message.Order.Side);
		AreEqual(OrderStates.Active, message.Order.State);
	}
}
