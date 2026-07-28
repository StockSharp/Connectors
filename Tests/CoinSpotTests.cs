namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinSpot;
using StockSharp.CoinSpot.Native;
using StockSharp.CoinSpot.Native.Model;
using StockSharp.Messages;

[TestClass]
public class CoinSpotTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddresses()
	{
		var adapter = new CoinSpotMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://www.coinspot.com.au/pubapi/v2",
			adapter.PublicEndpoint);
		AreEqual(
			"https://www.coinspot.com.au/api/v2",
			adapter.TradingEndpoint);
		AreEqual(
			"https://www.coinspot.com.au/api/v2/ro",
			adapter.ReadOnlyEndpoint);
		AreEqual(TimeSpan.FromSeconds(5), adapter.PollingInterval);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new CoinSpotMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "key".Secure(),
			Secret = "secret".Secure(),
			PublicEndpoint = "https://public.example.test/",
			TradingEndpoint = "https://trade.example.test/",
			ReadOnlyEndpoint = "https://read.example.test/",
			PollingInterval = TimeSpan.FromSeconds(17),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new CoinSpotMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("key", target.Key.UnSecure());
		AreEqual("secret", target.Secret.UnSecure());
		AreEqual(
			"https://public.example.test", target.PublicEndpoint);
		AreEqual(
			"https://trade.example.test", target.TradingEndpoint);
		AreEqual(
			"https://read.example.test", target.ReadOnlyEndpoint);
		AreEqual(TimeSpan.FromSeconds(17), target.PollingInterval);
	}

	[TestMethod]
	public void AuthenticatorSignsExactPostBodyWithSha512()
	{
		const string body =
			"{\"nonce\":1700000000000,\"cointype\":\"BTC\"," +
			"\"amount\":\"0.1\",\"rate\":\"50000\"," +
			"\"markettype\":\"AUD\"}";

		AreEqual(
			"1117eff6a1d16e197bdfe246032f317017718710b20118392b59c5fec" +
			"bb12bee46fd58de2ae098c70f3cb7d88019baab9e706b003651490911" +
			"3aaa5614ce5eca",
			CoinSpotAuthenticator.Sign("secret", body));
	}

	[TestMethod]
	public void LatestPricesDiscoverAudAndNamedMarkets()
	{
		const string json =
			"{\"status\":\"ok\",\"message\":\"ok\",\"prices\":{" +
			"\"btc\":{\"bid\":11111,\"ask\":22222,\"last\":15000}," +
			"\"btc_usdt\":{\"bid\":67000,\"ask\":67010,\"last\":67005}," +
			"\"eth\":{\"bid\":3000,\"ask\":3010,\"last\":3005}}}";

		var markets = CoinSpotRestClient.DeserializeMarkets(json);

		AreEqual(3, markets.Length);
		AreEqual("BTC/AUD", markets[0].SecurityCode);
		AreEqual("BTC/USDT", markets[1].SecurityCode);
		AreEqual(67005m, markets[1].Ticker.LastPrice);
		AreEqual("ETH/AUD", markets[2].SecurityCode);
	}

	[TestMethod]
	public void PublicOrderBookUsesCoinSpotSides()
	{
		const string json =
			"{\"status\":\"ok\",\"message\":\"ok\"," +
			"\"buyorders\":[{\"amount\":0.1,\"rate\":67000," +
			"\"total\":6700,\"coin\":\"BTC\",\"market\":\"BTC/AUD\"}]," +
			"\"sellorders\":[{\"amount\":0.2,\"rate\":67100," +
			"\"total\":13420,\"coin\":\"BTC\",\"market\":\"BTC/AUD\"}]}";

		var book = CoinSpotRestClient.DeserializeOrderBook(json);

		AreEqual(67000m, book.Bids[0].Price);
		AreEqual(0.1m, book.Bids[0].Volume);
		AreEqual(67100m, book.Asks[0].Price);
		AreEqual("BTC/AUD", book.Market);
	}

	[TestMethod]
	public void CompletedOrdersBecomePublicTrades()
	{
		const string json =
			"{\"status\":\"ok\",\"message\":\"ok\"," +
			"\"buyorders\":[{\"amount\":0.1,\"rate\":67000," +
			"\"coin\":\"BTC\",\"market\":\"BTC/AUD\"," +
			"\"solddate\":\"2026-07-28T10:00:00.000Z\"}]," +
			"\"sellorders\":[{\"amount\":0.2,\"rate\":67100," +
			"\"coin\":\"BTC\",\"market\":\"BTC/AUD\"," +
			"\"solddate\":\"2026-07-28T10:00:01.000Z\"}]}";

		var trades = CoinSpotRestClient.DeserializePublicTrades(json);

		AreEqual(2, trades.Length);
		AreEqual(Sides.Buy, trades[0].Side);
		AreEqual(Sides.Sell, trades[1].Side);
		AreEqual(67100m, trades[1].Price);
	}

	[TestMethod]
	public void NestedBalanceObjectsAreFlattened()
	{
		const string json =
			"{\"status\":\"ok\",\"message\":\"ok\",\"balances\":[" +
			"{\"AUD\":{\"balance\":1000.11,\"audbalance\":1000.11," +
			"\"rate\":1}},{\"BTC\":{\"balance\":1.25," +
			"\"audbalance\":100000,\"rate\":80000}}]}";

		var balances = CoinSpotRestClient.DeserializeBalances(json);

		AreEqual(2, balances.Length);
		AreEqual("AUD", balances[0].Currency);
		AreEqual(1000.11m, balances[0].Balance);
		AreEqual("BTC", balances[1].Currency);
		AreEqual(80000m, balances[1].Rate);
	}

	[TestMethod]
	public void PrivateOrdersRetainSideAndState()
	{
		const string json =
			"{\"status\":\"ok\",\"message\":\"ok\"," +
			"\"buyorders\":[{\"id\":\"buy-1\",\"coin\":\"BTC\"," +
			"\"market\":\"BTC/AUD\",\"amount\":0.1,\"rate\":67000," +
			"\"created\":\"2026-07-28T10:00:00.000Z\"}]," +
			"\"sellorders\":[{\"id\":\"sell-1\",\"coin\":\"BTC\"," +
			"\"market\":\"BTC/USDT\",\"amount\":0.2,\"rate\":67100," +
			"\"solddate\":\"2026-07-28T10:01:00.000Z\"}]}";

		var open = CoinSpotRestClient.DeserializeOrders(json, false);
		var history = CoinSpotRestClient.DeserializeOrders(json, true);

		AreEqual(Sides.Buy, open[0].Side);
		AreEqual(OrderStates.Active, open[0].State);
		AreEqual(Sides.Sell, history[1].Side);
		AreEqual(OrderStates.Done, history[1].State);
		AreEqual("BTC/USDT", history[1].Market);
	}

	[TestMethod]
	public void PlaceOrderResultUsesPublishedShape()
	{
		const string json =
			"{\"status\":\"ok\",\"message\":\"ok\",\"coin\":\"BTC\"," +
			"\"market\":\"BTC/AUD\",\"amount\":1.234,\"rate\":123.344," +
			"\"id\":\"12345678901234567890\"}";

		var order = CoinSpotRestClient.DeserializePlaceOrder(json);

		AreEqual("12345678901234567890", order.Id);
		AreEqual("BTC/AUD", order.Market);
		AreEqual(1.234m, order.Amount);
	}

	[TestMethod]
	public void MarketSymbolsUseCoinSpotConventions()
	{
		AreEqual(
			("BTC", "AUD"),
			"btc".ToCoinSpotCurrencies());
		AreEqual(
			("BTC", "USDT"),
			"btc_usdt".ToCoinSpotCurrencies());
		AreEqual(
			"BTC/USDT",
			CoinSpotExtensions.CreateSecurityCode("BTC", "USDT"));
		AreEqual(BoardCodes.CoinSpot, new CoinSpotMarket(
			"btc_usdt", 1m, 2m, 1.5m).ToStockSharp().BoardCode);
	}
}
