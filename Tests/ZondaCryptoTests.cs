namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.ZondaCrypto;
using StockSharp.ZondaCrypto.Native;

[TestClass]
public class ZondaCryptoTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddresses()
	{
		var adapter = new ZondaCryptoMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://api.zondacrypto.exchange/rest",
			adapter.RestEndpoint);
		AreEqual(
			"wss://api.zondacrypto.exchange/websocket/",
			adapter.WebSocketEndpoint);
		AreEqual(TimeSpan.FromSeconds(10),
			adapter.PrivatePollingInterval);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new ZondaCryptoMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "public".Secure(),
			Secret = "secret".Secure(),
			RestEndpoint = "https://rest.example.test/",
			WebSocketEndpoint = "wss://ws.example.test/",
			PrivatePollingInterval = TimeSpan.FromSeconds(17),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new ZondaCryptoMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("public", target.Key.UnSecure());
		AreEqual("secret", target.Secret.UnSecure());
		AreEqual(
			"https://rest.example.test", target.RestEndpoint);
		AreEqual(
			"wss://ws.example.test", target.WebSocketEndpoint);
		AreEqual(TimeSpan.FromSeconds(17),
			target.PrivatePollingInterval);
	}

	[TestMethod]
	public void AuthenticatorSignsKeyTimestampAndBody()
	{
		AreEqual(
			"2651ad427d40f02fcf6e94c7187fd70a5b13916e60bb7c2d67f9d935" +
			"6e125786bbe1c2d901aa0c76a992ee0a4a65a112b289a9f8f1a90e5d" +
			"73f98fd56abb40fb",
			ZondaCryptoAuthenticator.Sign(
				"public",
				"secret",
				"1700000000000",
				"{\"offerType\":\"BUY\"}"));
	}

	[TestMethod]
	public void TickersExposeMarketMetadataAndQuotes()
	{
		const string json =
			"{\"status\":\"Ok\",\"ticker\":{\"market\":{" +
			"\"code\":\"ETH-PLN\",\"amountPrecision\":8," +
			"\"pricePrecision\":2,\"ratePrecision\":4," +
			"\"first\":{\"currency\":\"ETH\"," +
			"\"minOffer\":\"0.00045\",\"scale\":8},\"second\":{" +
			"\"currency\":\"PLN\",\"minOffer\":\"5\",\"scale\":2}}," +
			"\"time\":\"1576846031093\",\"highestBid\":\"491.44\"," +
			"\"lowestAsk\":\"495\",\"rate\":\"494.5\"," +
			"\"previousRate\":\"499.42\"}}";

		var ticker = ZondaCryptoRestClient
			.DeserializeTickers(json).Single();

		AreEqual("ETH/PLN", ticker.Market.SecurityCode);
		AreEqual(8, ticker.Market.AmountPrecision);
		AreEqual(2, ticker.Market.PricePrecision);
		AreEqual(4, ticker.Market.RatePrecision);
		AreEqual(491.44m, ticker.BidPrice);
		AreEqual(495m, ticker.AskPrice);
		AreEqual(494.5m, ticker.LastPrice);
	}

	[TestMethod]
	public void OrderBookReadsPublishedObjectRows()
	{
		const string json =
			"{\"status\":\"Ok\",\"sell\":[{\"ra\":\"101\"," +
			"\"ca\":\"2\",\"co\":1}],\"buy\":[{\"ra\":\"99\"," +
			"\"ca\":\"3\",\"co\":2}],\"timestamp\":\"1576847127883\"," +
			"\"seqNo\":\"40019280\"}";

		var book = ZondaCryptoRestClient.DeserializeOrderBook(json);

		AreEqual(99m, book.Bids[0].Price);
		AreEqual(3m, book.Bids[0].Volume);
		AreEqual(101m, book.Asks[0].Price);
		AreEqual(2m, book.Asks[0].Volume);
		AreEqual(40019280L, book.Sequence);
	}

	[TestMethod]
	public void TradesReadPublishedShortFieldNames()
	{
		const string json =
			"{\"status\":\"Ok\",\"items\":[{\"id\":\"trade-1\"," +
			"\"t\":\"1576846031093\",\"a\":\"0.25\",\"r\":\"491.5\"," +
			"\"ty\":\"Buy\"}]}";

		var trades = ZondaCryptoRestClient.DeserializeTrades(
			json, "ETH-PLN");

		AreEqual(1, trades.Length);
		AreEqual("trade-1", trades[0].Id);
		AreEqual(Sides.Buy, trades[0].Side);
		AreEqual(0.25m, trades[0].Volume);
		AreEqual(491.5m, trades[0].Price);
	}

	[TestMethod]
	public void WalletsExposeAvailableAndLockedFunds()
	{
		const string json =
			"{\"status\":\"Ok\",\"balances\":[{\"id\":\"wallet-1\"," +
			"\"availableFunds\":\"10.5\",\"totalFunds\":\"12\"," +
			"\"lockedFunds\":\"1.5\",\"currency\":\"BTC\"," +
			"\"type\":\"CRYPTO\",\"name\":\"trading\"}]}";

		var wallets = ZondaCryptoRestClient.DeserializeWallets(json);

		AreEqual(1, wallets.Length);
		AreEqual("BTC", wallets[0].Currency);
		AreEqual(10.5m, wallets[0].Available);
		AreEqual(1.5m, wallets[0].Locked);
		AreEqual(12m, wallets[0].Total);
	}

	[TestMethod]
	public void ActiveOffersMapOrderFields()
	{
		const string json =
			"{\"status\":\"Ok\",\"items\":[{\"id\":\"offer-1\"," +
			"\"market\":\"ETH-PLN\",\"offerType\":\"SELL\"," +
			"\"mode\":\"limit\",\"rate\":\"500\",\"startAmount\":\"2\"," +
			"\"currentAmount\":\"0.75\",\"status\":\"ACTIVE\"," +
			"\"time\":\"1576846031093\"}]}";

		var offers = ZondaCryptoRestClient.DeserializeOffers(json);

		AreEqual(1, offers.Length);
		AreEqual("offer-1", offers[0].Id);
		AreEqual(Sides.Sell, offers[0].Side);
		AreEqual(OrderTypes.Limit, offers[0].OrderType);
		AreEqual(OrderStates.Active, offers[0].State);
		AreEqual(2m, offers[0].OriginalAmount);
		AreEqual(0.75m, offers[0].RemainingAmount);
	}

	[TestMethod]
	public void WebSocketSubscriptionUsesPublishedEnvelope()
	{
		AreEqual(
			"{\"action\":\"subscribe-public\",\"module\":\"trading\"," +
			"\"path\":\"ticker/eth-pln\"}",
			ZondaCryptoWsClient.CreateSubscriptionJson(
				true, false, "trading", "ticker/ETH-PLN",
				null, null, "1700000000000"));
	}

	[TestMethod]
	public void WebSocketOrderBookPushParsesChanges()
	{
		const string json =
			"{\"action\":\"push\",\"topic\":" +
			"\"trading/orderbook/eth-pln\",\"message\":{" +
			"\"changes\":[{\"marketCode\":\"ETH-PLN\"," +
			"\"entryType\":\"Buy\",\"rate\":\"490\"," +
			"\"action\":\"update\",\"state\":{\"ra\":\"490\"," +
			"\"ca\":\"1.25\"}}],\"timestamp\":\"1576847016253\"}," +
			"\"timestamp\":\"1576847016253\",\"seqNo\":40018807}";

		var message = ZondaCryptoWsClient.DeserializeMessage(json);

		AreEqual("trading/orderbook/eth-pln", message.Topic);
		AreEqual(40018807L, message.Sequence);
		AreEqual(1, message.BookChanges.Length);
		AreEqual(Sides.Buy, message.BookChanges[0].Side);
		AreEqual(490m, message.BookChanges[0].Price);
		AreEqual(1.25m, message.BookChanges[0].Volume);
	}
}
