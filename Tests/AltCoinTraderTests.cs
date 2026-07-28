namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.AltCoinTrader;
using StockSharp.AltCoinTrader.Native;
using StockSharp.AltCoinTrader.Native.Model;
using StockSharp.Messages;

[TestClass]
public class AltCoinTraderTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddresses()
	{
		var adapter = new AltCoinTraderMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://api.altcointrader.co.za",
			adapter.RestEndpoint);
		AreEqual(
			"wss://api.altcointrader.co.za",
			adapter.WebSocketEndpoint);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new AltCoinTraderMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "access-key".Secure(),
			Secret = "secret-key".Secure(),
			RestEndpoint = "https://rest.example.test/",
			WebSocketEndpoint = "wss://stream.example.test/",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new AltCoinTraderMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("access-key", target.Key.UnSecure());
		AreEqual("secret-key", target.Secret.UnSecure());
		AreEqual(
			"https://rest.example.test",
			target.RestEndpoint);
		AreEqual(
			"wss://stream.example.test",
			target.WebSocketEndpoint);
	}

	[TestMethod]
	public void GetSignatureUsesPublishedPayload()
	{
		AreEqual(
			"e6d55111129b3678132ac9cac21b954d8861d94f02d8dca1ab7d21841789b8e2",
			AltCoinTraderAuthenticator.CreateSignature(
				"secret",
				1710000000,
				"GET",
				"/balances",
				null));
	}

	[TestMethod]
	public void PostSignatureUsesExactJsonBody()
	{
		const string body =
			"{\"market\":\"BTCZAR\",\"side\":\"buy\"," +
			"\"price\":\"1020000\",\"quantity\":\"0.01\"}";

		AreEqual(
			"d34b88ea2dbfac9a0acf57e7cea554e75ab604074bbdbff578b203f1a66f10c6",
			AltCoinTraderAuthenticator.CreateSignature(
				"secret",
				1710000000,
				"POST",
				"/orders",
				body));
	}

	[TestMethod]
	public void MarketModelUsesPublishedPrecisionAndLimits()
	{
		const string json =
			"[{\"symbol\":\"BTCZAR\",\"base\":\"BTC\"," +
			"\"quote\":\"ZAR\",\"status\":\"active\"," +
			"\"min_order_value\":\"5.00000000\"," +
			"\"price_precision\":8,\"quantity_precision\":8}]";

		var markets = AltCoinTraderRestClient.Deserialize<
			AltCoinTraderMarket[]>(json);

		AreEqual(1, markets.Length);
		AreEqual("BTCZAR", markets[0].Symbol);
		AreEqual("BTC", markets[0].Base);
		AreEqual("ZAR", markets[0].Quote);
		AreEqual(5m, markets[0].MinimumOrderValue);
		AreEqual(0.00000001m, markets[0].PriceStep);
		AreEqual(0.00000001m, markets[0].QuantityStep);
		IsTrue(markets[0].IsActive);
	}

	[TestMethod]
	public void MarketDataModelsUsePublishedShapes()
	{
		const string tickerJson =
			"{\"askPrice\":\"1074500.00000000\"," +
			"\"bidPrice\":\"1068361.12500000\"," +
			"\"change\":\"-33737.07359923\"," +
			"\"change_pct\":\"-3.05\"," +
			"\"high\":\"1107748.00000000\"," +
			"\"last\":\"1074010.92640077\"," +
			"\"low\":\"1072510.00000000\"," +
			"\"open\":\"1107748.00000000\"," +
			"\"symbol\":\"BTCZAR\",\"timestamp\":1785201851," +
			"\"volume\":\"1.72448508\"}";
		const string depthJson =
			"{\"asks\":[[\"1074500.00000000\",\"0.00016296\"]]," +
			"\"bids\":[[\"1068361.12500000\",\"0.00187203\"]]," +
			"\"symbol\":\"BTCZAR\",\"timestamp\":1785205748}";
		const string tradesJson =
			"[{\"market\":\"BTCZAR\"," +
			"\"price\":\"1074010.92640077\"," +
			"\"quantity\":\"0.01513924\",\"side\":\"buy\"," +
			"\"timestamp\":1785201851," +
			"\"trade_id\":\"pair:3:seq:22336847\"}]";

		var ticker = AltCoinTraderRestClient.Deserialize<
			AltCoinTraderTicker>(tickerJson);
		var depth = AltCoinTraderRestClient.Deserialize<
			AltCoinTraderOrderBook>(depthJson);
		var trades = AltCoinTraderRestClient.Deserialize<
			AltCoinTraderTrade[]>(tradesJson);

		AreEqual(1074010.92640077m, ticker.LastPrice);
		AreEqual(1068361.125m, ticker.BidPrice);
		AreEqual(1074500m, depth.Asks[0][0]);
		AreEqual(0.00187203m, depth.Bids[0][1]);
		AreEqual(Sides.Buy, trades[0].Side.ToSide());
	}

	[TestMethod]
	public void TradingModelsExposeOrderAndBalanceState()
	{
		const string orderJson =
			"{\"order_id\":\"order-001\"," +
			"\"client_order_id\":\"s0000000123\"," +
			"\"market\":\"BTCZAR\",\"side\":\"buy\"," +
			"\"type\":\"limit\",\"price\":\"1020000\"," +
			"\"quantity\":\"0.01\",\"filled\":\"0.004\"," +
			"\"remaining\":\"0.006\"," +
			"\"status\":\"partially_filled\"," +
			"\"time_in_force\":\"GTC\"," +
			"\"created_at\":1710000000," +
			"\"updated_at\":1710000001}";
		const string balanceJson =
			"{\"currency\":\"BTC\",\"available\":\"0.45\"," +
			"\"reserved\":\"0.12\",\"total\":\"0.57\"}";

		var order = AltCoinTraderRestClient.Deserialize<
			AltCoinTraderOrder>(orderJson);
		var balance = AltCoinTraderRestClient.Deserialize<
			AltCoinTraderBalance>(balanceJson);

		AreEqual(OrderStates.Active, order.Status.ToOrderState());
		AreEqual(OrderTypes.Limit, order.Type.ToOrderType());
		AreEqual(123L, order.TransactionId);
		AreEqual(0.006m, order.Remaining);
		AreEqual(0.57m, balance.Total);
		AreEqual(0.12m, balance.Reserved);
	}

	[TestMethod]
	public void WebSocketProtocolCreatesAndParsesFrames()
	{
		AreEqual(
			"wss://api.altcointrader.co.za/ws",
			AltCoinTraderWsProtocol.CreateEndpoint(
				"wss://api.altcointrader.co.za",
				false));
		AreEqual(
			"wss://api.altcointrader.co.za/ws/private",
			AltCoinTraderWsProtocol.CreateEndpoint(
				"wss://api.altcointrader.co.za",
				true));
		AreEqual(
			"{\"action\":\"subscribe\",\"channel\":\"orderbook\"," +
			"\"market\":\"BTCZAR\",\"limit\":25}",
			AltCoinTraderWsProtocol.CreateSubscription(
				"orderbook", "BTCZAR", 25, true));

		var frame = AltCoinTraderWsProtocol.DeserializeFrame(
			"{\"channel\":\"ticker\",\"market\":\"BTCZAR\"," +
			"\"data\":{\"last\":\"1074010.92640077\"," +
			"\"timestamp\":1785201851}}");

		AreEqual("ticker", frame.Channel);
		AreEqual("BTCZAR", frame.Market);
		AreEqual(
			1074010.92640077m,
			frame.Data.ToObject<AltCoinTraderTicker>().LastPrice);
	}

	[TestMethod]
	public void PrivateFillUsesIncrementalQuantity()
	{
		const string json =
			"{\"trade_id\":\"fill-1\",\"order_id\":\"order-1\"," +
			"\"client_order_id\":\"s0000000042\"," +
			"\"market\":\"BTCZAR\",\"side\":\"buy\"," +
			"\"price\":\"1050000\",\"fill_delta\":\"0.025\"," +
			"\"filled\":\"0.075\",\"remaining\":\"0.125\"," +
			"\"quantity\":\"0.025\",\"fee\":\"157.5\"," +
			"\"timestamp\":1784289007}";

		var fill = AltCoinTraderRestClient.Deserialize<
			AltCoinTraderUserTrade>(json);

		AreEqual("fill-1", fill.TradeId);
		AreEqual(0.025m, fill.ExecutionQuantity);
		AreEqual(42L, fill.TransactionId);
	}

	[TestMethod]
	public void SymbolsAndTimeInForceUseAltCoinTraderFormats()
	{
		AreEqual("BTCZAR", "btczar".ToAltCoinTraderSymbol());
		AreEqual("GTC", ((TimeInForce?)null).ToAltCoinTrader());
		AreEqual(
			"IOC",
			TimeInForce.CancelBalance.ToAltCoinTrader());
		AreEqual(
			"FOK",
			TimeInForce.MatchOrCancel.ToAltCoinTrader());
		AreEqual(
			TimeInForce.CancelBalance,
			"IOC".ToTimeInForce());
	}
}
