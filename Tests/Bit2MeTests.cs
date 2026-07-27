namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Bit2Me;
using StockSharp.Bit2Me.Native;
using StockSharp.Bit2Me.Native.Model;
using StockSharp.Messages;

[TestClass]
public class Bit2MeTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUseOfficialServiceAddresses()
	{
		var adapter = new Bit2MeMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://gateway.bit2me.com", adapter.RestEndpoint);
		AreEqual("wss://ws.bit2me.com/v1/trading",
			adapter.WebSocketEndpoint);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new Bit2MeMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "api-key".Secure(),
			Secret = "api-secret".Secure(),
			RestEndpoint = "https://gateway.example.test/",
			WebSocketEndpoint = "wss://stream.example.test/trading/",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new Bit2MeMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("api-key", target.Key.UnSecure());
		AreEqual("api-secret", target.Secret.UnSecure());
		AreEqual("https://gateway.example.test", target.RestEndpoint);
		AreEqual("wss://stream.example.test/trading",
			target.WebSocketEndpoint);
	}

	[TestMethod]
	public void SignatureMatchesOfficialSha256HmacSha512Scheme()
	{
		const string body =
			"{\"side\":\"sell\",\"symbol\":\"B2M/EUR\",\"price\":\"0.09999\"," +
			"\"amount\":\"5001.00000000\",\"orderType\":\"limit\"}";

		AreEqual(
			"AcdudzPxQu3wK97Q9JjRIAiOUgZMWucrM/AEIKqy6KEDVeSap0LBt2YCl72y+" +
			"12oE8crk3k4fcfm/eWrPqceng==",
			Bit2MeRestClient.CreateSignature("test-secret", 1687155308,
				"/v1/trading/order", body));
	}

	[TestMethod]
	public void OrderBodyUsesDocumentedWireNames()
	{
		var json = Bit2MeRestClient.SerializeBody(new Bit2MeOrderRequest
		{
			Side = Bit2MeSides.Buy,
			Symbol = "BTC/EUR",
			Price = "65000.5",
			Amount = "0.35",
			OrderType = Bit2MeOrderTypes.Limit,
			ClientOrderId = "ss-42",
			TimeInForce = Bit2MeTimeInForces.GoodTillCancelled,
		});

		IsTrue(json.Contains("\"side\":\"buy\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"symbol\":\"BTC/EUR\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"clientOrderId\":\"ss-42\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"timeInForce\":\"GTC\"",
			StringComparison.Ordinal));
		IsFalse(json.Contains("\"Side\":", StringComparison.Ordinal));
	}

	[TestMethod]
	public void ArrayMarketDataModelsUseDocumentedShape()
	{
		var candle = JsonConvert.DeserializeObject<Bit2MeCandle>(
			"[1716288000000,65000.1,65500.2,64800.3,65346.1,12.5]");
		var trade = JsonConvert.DeserializeObject<Bit2MePublicTrade>(
			"[\"buy\",65346.1,0.00911238,1716288547800]");
		var level = JsonConvert.DeserializeObject<Bit2MePriceLevel>(
			"[65300,9.56049018]");

		AreEqual(1716288000000L, candle.Timestamp);
		AreEqual(65500.2m, candle.High);
		AreEqual(Bit2MeSides.Buy, trade.Side);
		AreEqual(0.00911238m, trade.Amount);
		AreEqual(65300m, level.Price);
		AreEqual(9.56049018m, level.Volume);
	}

	[TestMethod]
	public void WebSocketOrderBookUsesOuterSymbolAndSnapshot()
	{
		const string payload =
			"{\"event\":\"order-book\",\"symbol\":\"BTC/EUR\",\"data\":{" +
			"\"bids\":[[65300,1.5]],\"asks\":[[65301,2.5]]," +
			"\"timestamp\":1716288634366,\"nonce\":1716283145466," +
			"\"symbol\":\"BTC/EUR\"}}";

		var envelope =
			Bit2MeWsClient.Deserialize<Bit2MeWsEnvelope<Bit2MeOrderBook>>(
				payload);

		AreEqual("order-book", envelope.Event);
		AreEqual("BTC/EUR", envelope.Symbol);
		AreEqual(1716283145466L, envelope.Data.Nonce);
		AreEqual(65300m, envelope.Data.Bids[0].Price);
		AreEqual(65301m, envelope.Data.Asks[0].Price);
	}

	[TestMethod]
	public void SymbolsAndIntervalsUseBit2MeFormat()
	{
		AreEqual("BTC/EUR", "btc-eur".NormalizeSymbol());
		AreEqual(new SecurityId
		{
			SecurityCode = "BTC/EUR",
			BoardCode = BoardCodes.Bit2Me,
		}, "BTC_EUR".ToStockSharp());
		AreEqual(1, TimeSpan.FromMinutes(1).ToBit2MeInterval());
		AreEqual(240, TimeSpan.FromHours(4).ToBit2MeInterval());
		AreEqual(1440, TimeSpan.FromDays(1).ToBit2MeInterval());
	}

	[TestMethod]
	public void OrderConditionKeepsTriggerPrice()
	{
		var condition = new Bit2MeOrderCondition
		{
			TriggerPrice = 65000.5m,
		};

		AreEqual(65000.5m, condition.TriggerPrice);
	}

}
