namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.NovaDax;
using StockSharp.NovaDax.Native;
using StockSharp.NovaDax.Native.Model;

[TestClass]
public class NovaDaxTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddresses()
	{
		var adapter = new NovaDaxMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://api.novadax.com",
			adapter.RestEndpoint);
		AreEqual(
			"wss://api.novadax.com",
			adapter.WebSocketEndpoint);
		AreEqual(3, adapter.EngineIoVersion);
		IsTrue(adapter.AccountId.IsEmpty());
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new NovaDaxMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "access-key".Secure(),
			Secret = "secret-key".Secure(),
			AccountId = "CA123456",
			RestEndpoint = "https://rest.example.test/",
			WebSocketEndpoint = "wss://stream.example.test/",
			EngineIoVersion = 4,
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new NovaDaxMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("access-key", target.Key.UnSecure());
		AreEqual("secret-key", target.Secret.UnSecure());
		AreEqual("CA123456", target.AccountId);
		AreEqual(
			"https://rest.example.test",
			target.RestEndpoint);
		AreEqual(
			"wss://stream.example.test",
			target.WebSocketEndpoint);
		AreEqual(4, target.EngineIoVersion);
	}

	[TestMethod]
	public void GetSignatureUsesSortedEncodedQuery()
	{
		var signature = NovaDaxAuthenticator.CreateSignature(
			"secret",
			"GET",
			"/v1/orders/get",
			"birthday=2017-08-01&cpf=123456&name=joao",
			null,
			1564988445199);

		AreEqual(
			"a45f24a55d32c48efbc45186c669de7c1803d64da25c54c29474f7527da3a241",
			signature);
	}

	[TestMethod]
	public void PostSignatureHashesExactJsonBody()
	{
		const string body =
			"{\"name\":\"joao\",\"cpf\":\"123456\"," +
			"\"birthday\":\"2017-08-01\"}";

		AreEqual(
			"7d8f374d786079cfade9d1c2a358137c",
			NovaDaxAuthenticator.CreateContentHash(body));
		AreEqual(
			"227721a7fbe124796fbdccfb6649532ecfb7e2ef069b1f293283c9683b65be0e",
			NovaDaxAuthenticator.CreateSignature(
				"secret",
				"POST",
				"/v1/order/create",
				null,
				body,
				1564988445199));
	}

	[TestMethod]
	public void SymbolModelUsesPublishedPrecisionAndLimits()
	{
		const string json =
			"{\"code\":\"A10000\",\"data\":[{" +
			"\"symbol\":\"BTC_BRL\",\"baseCurrency\":\"BTC\"," +
			"\"quoteCurrency\":\"BRL\",\"amountPrecision\":4," +
			"\"pricePrecision\":2,\"valuePrecision\":4," +
			"\"minOrderAmount\":\"0.001\"," +
			"\"minOrderValue\":\"5\"}]," +
			"\"message\":\"Success\"}";

		var symbols = NovaDaxRestClient.Deserialize<
			NovaDaxSymbol[]>(json);

		AreEqual(1, symbols.Length);
		AreEqual("BTC_BRL", symbols[0].Symbol);
		AreEqual("BTC", symbols[0].BaseCurrency);
		AreEqual("BRL", symbols[0].QuoteCurrency);
		AreEqual(0.01m, symbols[0].PriceStep);
		AreEqual(0.0001m, symbols[0].AmountStep);
		AreEqual(0.001m, symbols[0].MinimumAmount);
		AreEqual(5m, symbols[0].MinimumValue);
	}

	[TestMethod]
	public void MarketModelsUsePublishedShapes()
	{
		const string tickerJson =
			"{\"code\":\"A10000\",\"data\":{" +
			"\"ask\":\"34708.15\",\"baseVolume24h\":\"34.08\"," +
			"\"bid\":\"34621.74\",\"high24h\":\"35079.77\"," +
			"\"lastPrice\":\"34669.81\",\"low24h\":\"34330.64\"," +
			"\"open24h\":\"34492.08\",\"symbol\":\"BTC_BRL\"," +
			"\"timestamp\":1571112216346},\"message\":\"Success\"}";
		const string depthJson =
			"{\"code\":\"A10000\",\"data\":{" +
			"\"asks\":[[\"43687.16\",\"0.5194\"]]," +
			"\"bids\":[[\"43657.57\",\"0.6135\"]]," +
			"\"timestamp\":1565057338020},\"message\":\"Success\"}";
		const string tradesJson =
			"{\"code\":\"A10000\",\"data\":[{" +
			"\"price\":\"43657.57\",\"amount\":\"1\"," +
			"\"side\":\"SELL\",\"timestamp\":1565007823401}]," +
			"\"message\":\"Success\"}";

		var ticker = NovaDaxRestClient.Deserialize<
			NovaDaxTicker>(tickerJson);
		var depth = NovaDaxRestClient.Deserialize<
			NovaDaxOrderBook>(depthJson);
		var trades = NovaDaxRestClient.Deserialize<
			NovaDaxTrade[]>(tradesJson);

		AreEqual(34669.81m, ticker.LastPrice);
		AreEqual(34621.74m, ticker.Bid);
		AreEqual(43687.16m, depth.Asks[0][0]);
		AreEqual(0.6135m, depth.Bids[0][1]);
		AreEqual(Sides.Sell, trades[0].Side.ToSide());
	}

	[TestMethod]
	public void CandleModelUsesUnixSeconds()
	{
		const string json =
			"{\"code\":\"A10000\",\"data\":[{" +
			"\"amount\":8.257091,\"closePrice\":62553.20," +
			"\"count\":29,\"highPrice\":62592.87," +
			"\"lowPrice\":62553.20,\"openPrice\":62554.23," +
			"\"score\":1602501480,\"symbol\":\"BTC_BRL\"," +
			"\"vol\":516784.2504}],\"message\":\"Success\"}";

		var candles = NovaDaxRestClient.Deserialize<
			NovaDaxCandle[]>(json);

		AreEqual(62554.23m, candles[0].Open);
		AreEqual(62553.20m, candles[0].Close);
		AreEqual(1602501480L, candles[0].Timestamp);
		AreEqual(8.257091m, candles[0].Amount);
	}

	[TestMethod]
	public void TradingModelsExposeOrderAndBalanceState()
	{
		const string orderJson =
			"{\"code\":\"A10000\",\"data\":{" +
			"\"id\":\"633679992971251712\"," +
			"\"clientOrderId\":\"client_order_id_123456\"," +
			"\"symbol\":\"BTC_BRL\",\"type\":\"MARKET\"," +
			"\"side\":\"SELL\",\"averagePrice\":\"34669.81\"," +
			"\"amount\":\"0.123\",\"filledAmount\":\"0.1\"," +
			"\"filledFee\":\"0.0001\",\"status\":\"PARTIAL_FILLED\"," +
			"\"timestamp\":1565165945588},\"message\":\"Success\"}";
		const string balanceJson =
			"{\"code\":\"A10000\",\"data\":[{" +
			"\"available\":\"1.23\",\"balance\":\"2.23\"," +
			"\"currency\":\"BTC\",\"hold\":\"1\"}]," +
			"\"message\":\"Success\"}";

		var order = NovaDaxRestClient.Deserialize<
			NovaDaxOrder>(orderJson);
		var balance = NovaDaxRestClient.Deserialize<
			NovaDaxBalance[]>(balanceJson)[0];

		AreEqual(OrderStates.Active, order.Status.ToOrderState());
		AreEqual(0.023m, order.RemainingAmount);
		AreEqual(2.23m, balance.Total);
		AreEqual(1m, balance.Hold);
	}

	[TestMethod]
	public void SocketIoProtocolCreatesAndParsesFrames()
	{
		AreEqual(
			"wss://api.novadax.com/socket.io/?EIO=3&transport=websocket",
			NovaDaxSocketProtocol.CreateEndpoint(
				"wss://api.novadax.com"));
		AreEqual(
			"wss://api.novadax.com/socket.io/?EIO=4&transport=websocket",
			NovaDaxSocketProtocol.CreateEndpoint(
				"wss://api.novadax.com", 4));
		AreEqual(
			"42[\"SUBSCRIBE\",[\"MARKET.BTC_BRL.TICKER\"]]",
			NovaDaxSocketProtocol.EncodeEvent(
				"SUBSCRIBE",
				new[] { "MARKET.BTC_BRL.TICKER" }));

		IsTrue(NovaDaxSocketProtocol.TryParseEvent(
			"42[\"MARKET.BTC_BRL.TICKER\",{" +
			"\"symbol\":\"BTC_BRL\",\"lastPrice\":\"34669.81\"}]",
			out var eventName,
			out var payload));
		AreEqual("MARKET.BTC_BRL.TICKER", eventName);
		AreEqual(
			34669.81m,
			payload.ToObject<NovaDaxTicker>().LastPrice);
	}

	[TestMethod]
	public void SymbolsIntervalsAndConditionUseNovaDaxFormats()
	{
		AreEqual("BTC_BRL", "btc/brl".ToNovaDaxSymbol());
		AreEqual(
			"ONE_MIN",
			TimeSpan.FromMinutes(1).ToNovaDaxInterval());
		AreEqual(
			TimeSpan.FromDays(7),
			"ONE_WEE".ToNovaDaxTimeFrame());

		var condition = new NovaDaxOrderCondition
		{
			StopPrice = 34000m,
			Operator = NovaDaxStopOperators.LessOrEqual,
		};
		AreEqual(34000m, condition.StopPrice);
		AreEqual(
			NovaDaxStopOperators.LessOrEqual,
			condition.Operator);
	}
}
