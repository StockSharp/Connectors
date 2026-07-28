namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Quidax;
using StockSharp.Quidax.Native;
using StockSharp.Quidax.Native.Model;

[TestClass]
public class QuidaxTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddress()
	{
		var adapter = new QuidaxMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://openapi.quidax.io/exchange-open-api/api/v1",
			adapter.RestEndpoint);
		AreEqual("me", adapter.UserId);
		AreEqual(
			TimeSpan.FromSeconds(5),
			adapter.PollingInterval);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new QuidaxMessageAdapter(
			new IncrementalIdGenerator())
		{
			Token = "secret-token".Secure(),
			UserId = "sub-user-42",
			RestEndpoint = "https://api.example.test/root/",
			PollingInterval = TimeSpan.FromSeconds(9),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new QuidaxMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("secret-token", target.Token.UnSecure());
		AreEqual("sub-user-42", target.UserId);
		AreEqual(
			"https://api.example.test/root",
			target.RestEndpoint);
		AreEqual(
			TimeSpan.FromSeconds(9),
			target.PollingInterval);
	}

	[TestMethod]
	public void BearerAuthorizationUsesToken()
	{
		AreEqual(
			"Bearer secret-token",
			QuidaxRestClient.CreateAuthorizationValue(
				"secret-token"));
	}

	[TestMethod]
	public void MarketModelUsesPublishedTradingRules()
	{
		const string json =
			"{\"status\":\"success\",\"message\":\"Successful\"," +
			"\"data\":[{\"id\":\"btcngn\",\"name\":\"BTC/NGN\"," +
			"\"base_unit\":\"BTC\",\"quote_unit\":\"NGN\"," +
			"\"trading_rules\":{\"base_precision\":8," +
			"\"quote_precision\":2,\"price_precision\":0," +
			"\"minimum_order_size\":1250}}]}";

		var markets = QuidaxRestClient.Deserialize<
			QuidaxMarket[]>(json);

		AreEqual(1, markets.Length);
		AreEqual("btcngn", markets[0].Id);
		AreEqual("BTC/NGN", markets[0].SecurityCode);
		AreEqual(1m, markets[0].PriceStep);
		AreEqual(0.00000001m, markets[0].VolumeStep);
		AreEqual(1250m, markets[0].MinimumOrderValue);
	}

	[TestMethod]
	public void TickerModelUsesMarketKeyedEnvelope()
	{
		const string json =
			"{\"status\":\"success\",\"message\":\"Successful\"," +
			"\"data\":{\"btcngn\":{\"ticker\":{" +
			"\"high\":\"91418611\",\"vol\":\"1.16849188\"," +
			"\"last\":87913723,\"low\":\"87913723\"," +
			"\"buy\":87961190,\"sell\":88353534," +
			"\"open\":\"90677265\"},\"at\":1785207460000}}}";

		var ticker = QuidaxRestClient.DeserializeTicker(
			json,
			"btcngn");

		AreEqual(87913723m, ticker.LastPrice);
		AreEqual(87961190m, ticker.BidPrice);
		AreEqual(88353534m, ticker.AskPrice);
		AreEqual(1785207460000L, ticker.Timestamp);
	}

	[TestMethod]
	public void DepthAndTradesUsePublishedShapes()
	{
		const string depthJson =
			"{\"status\":\"success\",\"message\":\"Successful\"," +
			"\"data\":{\"asks\":[[88353534,0.93459798]]," +
			"\"bids\":[[87961190,0.26951042]]," +
			"\"timestamp\":1785207462000}}";
		const string tradesJson =
			"{\"status\":\"success\",\"message\":\"Successful\"," +
			"\"data\":[{\"type\":\"BUY\",\"price\":\"0.28032\"," +
			"\"timestamp\":1771227245," +
			"\"base_volume\":\"7.13470319\"," +
			"\"quote_volume\":\"1.9999999982208\"," +
			"\"trade_id\":45124120}]}";

		var depth = QuidaxRestClient.Deserialize<
			QuidaxDepth>(depthJson);
		var trades = QuidaxRestClient.DeserializePublicTrades(
			tradesJson);

		AreEqual(88353534m, depth.Asks[0][0]);
		AreEqual(0.26951042m, depth.Bids[0][1]);
		AreEqual("45124120", trades[0].TradeId);
		AreEqual(Sides.Buy, trades[0].Type.ToSide());
		AreEqual(7.13470319m, trades[0].BaseVolume);
	}

	[TestMethod]
	public void UnexpectedPublicTradePayloadIsIgnored()
	{
		const string json =
			"{\"status\":\"success\",\"message\":\"Successful\"," +
			"\"data\":{\"asks\":[],\"bids\":[]}}";

		var trades = QuidaxRestClient.DeserializePublicTrades(json);

		AreEqual(0, trades.Length);
	}

	[TestMethod]
	public void CandleModelUsesPublishedArrayOrder()
	{
		const string json =
			"{\"status\":\"success\",\"message\":\"Successful\"," +
			"\"data\":[[1785200400000,\"88600000\"," +
			"\"88008380\",\"88600000\",\"88008380\"," +
			"\"0.0420124\"]]}";

		var candles = QuidaxRestClient.Deserialize<
			QuidaxCandle[]>(json);

		AreEqual(1785200400000L, candles[0].Timestamp);
		AreEqual(88600000m, candles[0].Open);
		AreEqual(88008380m, candles[0].Close);
		AreEqual(88600000m, candles[0].High);
		AreEqual(88008380m, candles[0].Low);
		AreEqual(0.0420124m, candles[0].Volume);
	}

	[TestMethod]
	public void OrderModelExposesStateAndNestedTrade()
	{
		const string json =
			"{\"status\":\"success\",\"message\":\"Successful\"," +
			"\"data\":{\"id\":\"order-1\"," +
			"\"market\":{\"id\":\"btcusdt\"," +
			"\"base_unit\":\"BTC\",\"quote_unit\":\"USDT\"}," +
			"\"side\":\"buy\",\"price\":{\"unit\":\"USDT\"," +
			"\"amount\":\"61000\"},\"status\":\"wait\"," +
			"\"order_type\":\"limit\"," +
			"\"origin_volume\":{\"unit\":\"BTC\"," +
			"\"amount\":\"0.05\"}," +
			"\"executed_volume\":{\"unit\":\"BTC\"," +
			"\"amount\":\"0.02\"},\"created_at\":" +
			"\"2026-02-23T10:47:10.148Z\"," +
			"\"trades\":[{\"id\":\"trade-1\"," +
			"\"price\":{\"unit\":\"USDT\",\"amount\":\"61000\"}," +
			"\"volume\":{\"unit\":\"BTC\",\"amount\":\"0.02\"}," +
			"\"created_at\":\"2026-02-23T10:47:11Z\"}]}}";

		var order = QuidaxRestClient.Deserialize<
			QuidaxOrder>(json);

		AreEqual(OrderStates.Active, order.Status.ToOrderState());
		AreEqual(OrderTypes.Limit, order.OrderType.ToOrderType());
		AreEqual(0.03m, order.RemainingVolume);
		AreEqual("trade-1", order.Trades[0].Id);
	}

	[TestMethod]
	public void WalletModelExposesAvailableAndLockedFunds()
	{
		const string json =
			"{\"status\":\"success\",\"message\":\"Successful\"," +
			"\"data\":[{\"id\":\"pmmope46\",\"name\":\"Bitcoin\"," +
			"\"currency\":\"btc\",\"balance\":\"0.45\"," +
			"\"locked\":\"0.12\",\"staked\":\"0.01\"}]}";

		var wallet = QuidaxRestClient.Deserialize<
			QuidaxWallet[]>(json)[0];

		AreEqual("btc", wallet.Currency);
		AreEqual(0.45m, wallet.Available);
		AreEqual(0.12m, wallet.Locked);
		AreEqual(0.58m, wallet.Total);
	}

	[TestMethod]
	public void OrderRequestUsesPublishedFieldNames()
	{
		var body = QuidaxRestClient.SerializeBody(
			new QuidaxPlaceOrderRequest
			{
				Market = "btcusdt",
				Side = "buy",
				OrderType = "limit",
				Price = "61000",
				Volume = "0.01",
			});

		AreEqual(
			"{\"market\":\"btcusdt\",\"side\":\"buy\"," +
			"\"ord_type\":\"limit\",\"price\":\"61000\"," +
			"\"volume\":\"0.01\"}",
			body);
	}

	[TestMethod]
	public void SymbolsIntervalsAndStatesUseQuidaxFormats()
	{
		AreEqual("btcngn", "BTC/NGN".ToQuidaxSymbol());
		AreEqual("BTC/NGN", QuidaxExtensions.CreateSecurityCode(
			"btc",
			"ngn"));
		AreEqual(
			60,
			TimeSpan.FromHours(1).ToQuidaxPeriod());
		AreEqual(
			TimeSpan.FromDays(7),
			10080.ToQuidaxTimeFrame());
		AreEqual(
			OrderStates.Done,
			"partially_filled_before_cancelled"
				.ToOrderState());
	}
}
