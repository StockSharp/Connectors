namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.DeltaExchangeIndia;
using StockSharp.DeltaExchangeIndia.Native;
using StockSharp.Messages;

[TestClass]
public class DeltaExchangeIndiaTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUseCurrentIndiaServiceAddresses()
	{
		var adapter = new DeltaExchangeIndiaMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://api.india.delta.exchange",
			adapter.RestEndpoint);
		AreEqual(
			"wss://public-socket.india.delta.exchange",
			adapter.PublicWebSocketEndpoint);
		AreEqual(
			"wss://socket.india.delta.exchange",
			adapter.PrivateWebSocketEndpoint);
		AreEqual(
			TimeSpan.FromSeconds(10),
			adapter.PrivatePollingInterval);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsAllAddresses()
	{
		var source = new DeltaExchangeIndiaMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "key".Secure(),
			Secret = "secret".Secure(),
			RestEndpoint = "https://rest.example.test/",
			PublicWebSocketEndpoint =
				"wss://public.example.test/",
			PrivateWebSocketEndpoint =
				"wss://private.example.test/",
			PrivatePollingInterval = TimeSpan.FromSeconds(17),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new DeltaExchangeIndiaMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("key", target.Key.UnSecure());
		AreEqual("secret", target.Secret.UnSecure());
		AreEqual(
			"https://rest.example.test",
			target.RestEndpoint);
		AreEqual(
			"wss://public.example.test",
			target.PublicWebSocketEndpoint);
		AreEqual(
			"wss://private.example.test",
			target.PrivateWebSocketEndpoint);
		AreEqual(
			TimeSpan.FromSeconds(17),
			target.PrivatePollingInterval);
	}

	[TestMethod]
	public void RestSignatureUsesPublishedConcatenation()
	{
		AreEqual(
			"4d3079fcf13e7ca4fc028930f257c5a393f7ec22" +
				"8996729ad284030e337d9324",
			DeltaExchangeIndiaRestClient.GenerateSignature(
				"POST",
				"1700000000",
				"/v2/orders",
				string.Empty,
				"{\"product_id\":27,\"size\":10}",
				"secret"));
	}

	[TestMethod]
	public void ProductsMapFuturesAndOptions()
	{
		const string json =
			"{\"success\":true,\"result\":[{" +
			"\"id\":27,\"symbol\":\"BTCUSD\"," +
			"\"description\":\"Bitcoin Perpetual\"," +
			"\"contract_type\":\"perpetual_futures\"," +
			"\"state\":\"live\",\"trading_status\":\"operational\"," +
			"\"tick_size\":\"0.5\",\"contract_value\":\"0.001\"," +
			"\"underlying_asset\":{\"symbol\":\"BTC\"}," +
			"\"quoting_asset\":{\"symbol\":\"USD\"}," +
			"\"settling_asset\":{\"symbol\":\"USD\"}},{" +
			"\"id\":40,\"symbol\":\"C-BTC-70000-310726\"," +
			"\"contract_type\":\"call_options\",\"state\":\"live\"," +
			"\"trading_status\":\"operational\"," +
			"\"strike_price\":\"70000\"," +
			"\"settlement_time\":\"2026-07-31T12:00:00Z\"}]}";

		var products =
			DeltaExchangeIndiaRestClient
				.DeserializeProducts(json);

		AreEqual(2, products.Length);
		AreEqual(SecurityTypes.Future, products[0].SecurityType);
		AreEqual(0.5m, products[0].PriceStep);
		AreEqual(SecurityTypes.Option, products[1].SecurityType);
		AreEqual(OptionTypes.Call, products[1].OptionType);
		AreEqual(70000m, products[1].Strike);
	}

	[TestMethod]
	public void PublicRestShapesAreParsed()
	{
		const string tickerJson =
			"{\"success\":true,\"result\":{\"symbol\":\"BTCUSD\"," +
			"\"open\":65000,\"high\":66000,\"low\":63000," +
			"\"close\":64000,\"mark_price\":\"64001.5\"," +
			"\"spot_price\":\"64010\",\"volume\":12.5," +
			"\"oi_contracts\":\"1234\",\"funding_rate\":\"0.01\"," +
			"\"timestamp\":1700000000000000,\"quotes\":{" +
			"\"best_bid\":\"63999.5\",\"best_ask\":\"64000.5\"," +
			"\"bid_size\":\"20\",\"ask_size\":\"30\"}}}";
		const string bookJson =
			"{\"success\":true,\"result\":{\"symbol\":\"BTCUSD\"," +
			"\"last_updated_at\":1700000000000000," +
			"\"buy\":[{\"price\":\"10\",\"size\":2}]," +
			"\"sell\":[{\"price\":\"11\",\"size\":3}]}}";
		const string tradeJson =
			"{\"success\":true,\"result\":[{\"symbol\":\"BTCUSD\"," +
			"\"price\":\"10.5\",\"size\":4," +
			"\"buyer_role\":\"taker\"," +
			"\"timestamp\":1700000000000000}]}";
		const string candleJson =
			"{\"success\":true,\"result\":[{\"time\":1700000000," +
			"\"open\":10,\"high\":12,\"low\":9," +
			"\"close\":11,\"volume\":5}]}";

		var ticker =
			DeltaExchangeIndiaRestClient
				.DeserializeTicker(tickerJson);
		var book =
			DeltaExchangeIndiaRestClient
				.DeserializeBook(bookJson);
		var trade =
			DeltaExchangeIndiaRestClient
				.DeserializeTrades(tradeJson, "BTCUSD")
				.Single();
		var candle =
			DeltaExchangeIndiaRestClient
				.DeserializeCandles(
					candleJson,
					"BTCUSD",
					TimeSpan.FromMinutes(1))
				.Single();

		AreEqual(64000m, ticker.Last);
		AreEqual(63999.5m, ticker.BestBid);
		AreEqual(2m, book.Bids.Single().Volume);
		AreEqual(11m, book.Asks.Single().Price);
		AreEqual(Sides.Buy, trade.Side);
		AreEqual(4m, trade.Volume);
		AreEqual(12m, candle.High);
		AreEqual(5m, candle.Volume);
	}

	[TestMethod]
	public void PrivateRestShapesAreParsed()
	{
		const string orderJson =
			"{\"success\":true,\"result\":{\"id\":123," +
			"\"product_id\":27,\"product_symbol\":\"BTCUSD\"," +
			"\"size\":10,\"unfilled_size\":2,\"side\":\"buy\"," +
			"\"order_type\":\"limit_order\"," +
			"\"limit_price\":\"59000\",\"state\":\"open\"," +
			"\"client_order_id\":\"ss-9\"," +
			"\"created_at\":\"1725865012000000\"}}";
		const string balanceJson =
			"{\"success\":true,\"result\":[{" +
			"\"asset_symbol\":\"USD\",\"balance\":\"100\"," +
			"\"available_balance\":\"70\"," +
			"\"blocked_margin\":\"30\"}]}";
		const string positionJson =
			"{\"success\":true,\"result\":[{" +
			"\"product_id\":27,\"product_symbol\":\"BTCUSD\"," +
			"\"size\":-10,\"entry_price\":\"60000\"," +
			"\"liquidation_price\":\"65000\",\"margin\":\"20\"}]}";
		const string fillJson =
			"{\"success\":true,\"result\":[{" +
			"\"id\":\"fill-1\",\"order_id\":123,\"product_id\":27," +
			"\"product_symbol\":\"BTCUSD\",\"side\":\"buy\"," +
			"\"price\":\"59000\",\"size\":8," +
			"\"commission\":\"1.2\"," +
			"\"created_at\":1725865012000000}]}";

		var order =
			DeltaExchangeIndiaRestClient
				.DeserializeOrders(orderJson).Single();
		var balance =
			DeltaExchangeIndiaRestClient
				.DeserializeBalances(balanceJson).Single();
		var position =
			DeltaExchangeIndiaRestClient
				.DeserializePositions(positionJson).Single();
		var fill =
			DeltaExchangeIndiaRestClient
				.DeserializeFills(fillJson).Single();

		AreEqual(OrderStates.Active, order.State);
		AreEqual(2m, order.Balance);
		AreEqual(70m, balance.Available);
		AreEqual(-10m, position.Size);
		AreEqual("fill-1", fill.Id);
		AreEqual(1.2m, fill.Commission);
	}

	[TestMethod]
	public void WebSocketRequestsUseCurrentProtocol()
	{
		AreEqual(
			"{\"type\":\"subscribe\",\"payload\":{\"channels\":[" +
				"{\"name\":\"trades\",\"symbols\":[\"BTCUSD\"]}]}}",
			DeltaExchangeIndiaWsClient.CreateSubscription(
				true, "trades", ["btcusd"]));
		AreEqual(
			"{\"type\":\"key-auth\",\"payload\":{\"api-key\":\"key\"," +
				"\"signature\":\"" +
				"21c22df1589945dad979a72af38c6c065dfd7ab8" +
				"501a6fca580308cd02aed4f9\"," +
				"\"timestamp\":1700000000}}",
			DeltaExchangeIndiaWsClient.CreateAuthentication(
				"key", "secret", 1700000000));
	}

	[TestMethod]
	public void WebSocketParsesCompactPublicMessages()
	{
		var ticker = DeltaExchangeIndiaWsClient
			.DeserializeMessage(
				"{\"type\":\"ticker\",\"ts\":1700000000000000," +
				"\"sp\":\"64010\",\"d\":[{\"s\":\"BTCUSD\"," +
				"\"m\":\"64001\",\"ohlc\":[63000,65000,62000,64000]," +
				"\"oi\":[\"123\",null]," +
				"\"q\":[\"64001\",\"3\",\"64000\",\"2\",null]}]}")
			.Tickers.Single();
		var book = DeltaExchangeIndiaWsClient
			.DeserializeMessage(
				"{\"type\":\"ob_l2\",\"sy\":\"BTCUSD\"," +
				"\"ts\":1700000000000000," +
				"\"a\":[[\"11\",\"3\"]],\"b\":[[\"10\",\"2\"]]}")
			.Book;
		var trade = DeltaExchangeIndiaWsClient
			.DeserializeMessage(
				"{\"type\":\"trades\",\"sy\":\"BTCUSD\"," +
				"\"p\":\"10.5\",\"s\":4,\"r\":\"t\"," +
				"\"t\":1700000000000000}")
			.Trade;
		var candle = DeltaExchangeIndiaWsClient
			.DeserializeMessage(
				"{\"type\":\"candlestick_1m\",\"sy\":\"BTCUSD\"," +
				"\"ts\":1700000000000000,\"o\":10,\"h\":12," +
				"\"l\":9,\"c\":11,\"v\":5}")
			.Candle;

		AreEqual(64000m, ticker.Last);
		AreEqual(64000m, ticker.BestBid);
		AreEqual(3m, book.Asks.Single().Volume);
		AreEqual(Sides.Buy, trade.Side);
		AreEqual(TimeSpan.FromMinutes(1), candle.TimeFrame);
		AreEqual(11m, candle.Close);
	}

	[TestMethod]
	public void WebSocketParsesPrivateMessages()
	{
		var order = DeltaExchangeIndiaWsClient
			.DeserializeMessage(
				"{\"type\":\"orders\",\"action\":\"update\"," +
				"\"symbol\":\"BTCUSD\",\"product_id\":27," +
				"\"order_id\":123,\"size\":10,\"unfilled_size\":2," +
				"\"side\":\"sell\",\"order_type\":\"limit_order\"," +
				"\"limit_price\":\"10\",\"state\":\"open\"," +
				"\"timestamp\":1700000000000000}")
			.Orders.Single();
		var fill = DeltaExchangeIndiaWsClient
			.DeserializeMessage(
				"{\"type\":\"v2/user_trades\",\"sy\":\"BTCUSD\"," +
				"\"f\":\"fill-1\",\"o\":123,\"S\":\"sell\"," +
				"\"s\":8,\"p\":\"10\",\"c\":\"ss-9\"," +
				"\"t\":1700000000000000}")
			.Fill;

		AreEqual(123L, order.Id);
		AreEqual(Sides.Sell, order.Side);
		AreEqual(OrderStates.Active, order.State);
		AreEqual("fill-1", fill.Id);
		AreEqual(8m, fill.Volume);
	}
}
