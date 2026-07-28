namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Buda;
using StockSharp.Buda.Native;
using StockSharp.Buda.Native.Model;
using StockSharp.Messages;

[TestClass]
public class BudaTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddresses()
	{
		var adapter = new BudaMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://www.buda.com/api/v2",
			adapter.RestEndpoint);
		AreEqual(
			"wss://realtime.buda.com/sub",
			adapter.WebSocketEndpoint);
		AreEqual(TimeSpan.FromSeconds(10),
			adapter.PrivatePollingInterval);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new BudaMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "key".Secure(),
			Secret = "secret".Secure(),
			RestEndpoint = "https://rest.example.test/",
			WebSocketEndpoint = "wss://ws.example.test/",
			PrivatePollingInterval = TimeSpan.FromSeconds(17),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new BudaMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("key", target.Key.UnSecure());
		AreEqual("secret", target.Secret.UnSecure());
		AreEqual(
			"https://rest.example.test", target.RestEndpoint);
		AreEqual(
			"wss://ws.example.test", target.WebSocketEndpoint);
		AreEqual(TimeSpan.FromSeconds(17),
			target.PrivatePollingInterval);
	}

	[TestMethod]
	public void AuthenticatorSignsMethodPathBodyAndNonce()
	{
		const string body =
			"{\"type\":\"Bid\",\"amount\":0.05}";

		AreEqual(
			"e6ba294c8e7d88f43f4799ad9d7879ce41605dad8eb68b6b4259257e" +
			"1f7777d1309492de9d7da28fb91263e5a14810db",
			BudaAuthenticator.Sign(
				"secret",
				"POST",
				"/api/v2/markets/btc-clp/orders",
				body,
				"1700000000000000"));
	}

	[TestMethod]
	public void MarketsUsePublishedAmountShape()
	{
		const string json =
			"{\"markets\":[{\"id\":\"BTC-CLP\"," +
			"\"name\":\"btc-clp\",\"base_currency\":\"BTC\"," +
			"\"quote_currency\":\"CLP\"," +
			"\"minimum_order_amount\":[\"0.001\",\"BTC\"]}]}";

		var markets = BudaRestClient.DeserializeMarkets(json);

		AreEqual(1, markets.Length);
		AreEqual("BTC/CLP", markets[0].SecurityCode);
		AreEqual(0.001m, markets[0].MinimumOrderAmount);
		AreEqual(BoardCodes.Buda,
			markets[0].ToStockSharp().BoardCode);
	}

	[TestMethod]
	public void TickerUsesCurrencyAmountArrays()
	{
		const string json =
			"{\"ticker\":{\"last_price\":[\"879789.0\",\"CLP\"]," +
			"\"market_id\":\"BTC-CLP\"," +
			"\"max_bid\":[\"879658.0\",\"CLP\"]," +
			"\"min_ask\":[\"880000.0\",\"CLP\"]," +
			"\"volume\":[\"102.0\",\"BTC\"]}}";

		var ticker = BudaRestClient.DeserializeTicker(json);

		AreEqual(879789m, ticker.LastPrice);
		AreEqual(879658m, ticker.BidPrice);
		AreEqual(880000m, ticker.AskPrice);
		AreEqual(102m, ticker.Volume);
	}

	[TestMethod]
	public void OrderBookReadsPriceAndAmountRows()
	{
		const string json =
			"{\"order_book\":{\"asks\":[[\"101\",\"2\"]]," +
			"\"bids\":[[\"99\",\"3\"]]}}";

		var book = BudaRestClient.DeserializeOrderBook(json);

		AreEqual(99m, book.Bids[0].Price);
		AreEqual(3m, book.Bids[0].Volume);
		AreEqual(101m, book.Asks[0].Price);
		AreEqual(2m, book.Asks[0].Volume);
	}

	[TestMethod]
	public void TradesReadTimestampVolumePriceSideAndId()
	{
		const string json =
			"{\"trades\":{\"market_id\":\"BTC-CLP\"," +
			"\"entries\":[[\"1476905551687\",\"0.00984662\"," +
			"\"435447.12\",\"buy\",\"trade-1\"]]}}";

		var trades = BudaRestClient.DeserializeTrades(json);

		AreEqual(1, trades.Length);
		AreEqual("trade-1", trades[0].Id);
		AreEqual(Sides.Buy, trades[0].Side);
		AreEqual(435447.12m, trades[0].Price);
		AreEqual(0.00984662m, trades[0].Volume);
	}

	[TestMethod]
	public void BalancesExposeAvailableAndBlockedAmounts()
	{
		const string json =
			"{\"balances\":[{\"id\":\"BTC\"," +
			"\"amount\":[\"11.5274815\",\"BTC\"]," +
			"\"available_amount\":[\"10.5274815\",\"BTC\"]," +
			"\"frozen_amount\":[\"1.0\",\"BTC\"]," +
			"\"pending_withdraw_amount\":[\"0.0\",\"BTC\"]}]}";

		var balances = BudaRestClient.DeserializeBalances(json);

		AreEqual(1, balances.Length);
		AreEqual("BTC", balances[0].Currency);
		AreEqual(10.5274815m, balances[0].Available);
		AreEqual(1m, balances[0].Blocked);
	}

	[TestMethod]
	public void OrdersMapSideTypeStateAndAmounts()
	{
		const string json =
			"{\"orders\":[{\"id\":2,\"type\":\"Ask\"," +
			"\"state\":\"traded\",\"market_id\":\"BTC-CLP\"," +
			"\"price_type\":\"limit\",\"order_type\":\"ioc\"," +
			"\"limit\":[\"700000.0\",\"CLP\"]," +
			"\"amount\":[\"0.0\",\"BTC\"]," +
			"\"original_amount\":[\"5.0\",\"BTC\"]," +
			"\"traded_amount\":[\"5.0\",\"BTC\"]," +
			"\"created_at\":\"2017-03-10T21:11:42.131Z\"}]}";

		var orders = BudaRestClient.DeserializeOrders(json);

		AreEqual(1, orders.Length);
		AreEqual("2", orders[0].Id);
		AreEqual(Sides.Sell, orders[0].Side);
		AreEqual(OrderStates.Done, orders[0].State);
		AreEqual(OrderTypes.Limit, orders[0].OrderType);
		AreEqual(5m, orders[0].OriginalAmount);
		AreEqual(0m, orders[0].RemainingAmount);
	}

	[TestMethod]
	public void WebSocketChannelsAndTradeEventsUseNchanShape()
	{
		AreEqual(
			"wss://realtime.buda.com/sub?channel=trades%40btcclp",
			BudaWsClient.CreateChannelEndpoint(
				"wss://realtime.buda.com/sub",
				"trades@btcclp"));

		const string json =
			"{\"ev\":\"trade-created\",\"mk\":\"BTC-CLP\"," +
			"\"trade\":[\"1476905551687\",\"0.00984662\"," +
			"\"435447.12\",\"sell\",\"trade-2\"]}";
		var message = BudaWsClient.DeserializeMessage(json);

		AreEqual("trade-created", message.Event);
		AreEqual("BTC-CLP", message.MarketId);
		AreEqual("trade-2", message.Trade.Id);
		AreEqual(Sides.Sell, message.Trade.Side);
	}
}
