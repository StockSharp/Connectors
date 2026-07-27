namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.BtcTurk;
using StockSharp.BtcTurk.Native;
using StockSharp.BtcTurk.Native.Model;
using StockSharp.Messages;

[TestClass]
public class BtcTurkTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUseOfficialServiceAddresses()
	{
		var adapter = new BtcTurkMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.btcturk.com", adapter.RestEndpoint);
		AreEqual("https://graph-api.btcturk.com", adapter.GraphEndpoint);
		AreEqual("wss://ws-feed-pro.btcturk.com",
			adapter.WebSocketEndpoint);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new BtcTurkMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "public-key".Secure(),
			Secret = "private-key".Secure(),
			RestEndpoint = "https://rest.example.test/base/",
			GraphEndpoint = "https://graph.example.test/history/",
			WebSocketEndpoint = "wss://stream.example.test/feed/",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new BtcTurkMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("public-key", target.Key.UnSecure());
		AreEqual("private-key", target.Secret.UnSecure());
		AreEqual("https://rest.example.test/base", target.RestEndpoint);
		AreEqual("https://graph.example.test/history",
			target.GraphEndpoint);
		AreEqual("wss://stream.example.test/feed",
			target.WebSocketEndpoint);
	}

	[TestMethod]
	public void SignatureMatchesOfficialHmacSha256Scheme()
	{
		AreEqual(
			"24D7bBb13KfoyRwLS09OVMtKonfSUs6BgOBZ65m7r7E=",
			BtcTurkRestClient.CreateSignature(
				"public-key",
				"cHJpdmF0ZS1zZWNyZXQ=",
				1710000000123));
	}

	[TestMethod]
	public void OrderBodyUsesDocumentedWireNames()
	{
		var json = BtcTurkRestClient.SerializeBody(
			new BtcTurkOrderRequest
			{
				Quantity = "0.35",
				Price = "65000.5",
				StopPrice = "64000",
				ClientOrderId = "ss-42",
				Method = BtcTurkOrderMethods.StopLimit,
				Side = BtcTurkSides.Buy,
				PairSymbol = "BTCTRY",
			});

		IsTrue(json.Contains("\"quantity\":\"0.35\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"newOrderClientId\":\"ss-42\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"orderMethod\":\"stoplimit\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"orderType\":\"buy\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"pairSymbol\":\"BTCTRY\"",
			StringComparison.Ordinal));
		IsFalse(json.Contains("\"Method\":", StringComparison.Ordinal));
	}

	[TestMethod]
	public void PublicModelsUseDocumentedResponseShape()
	{
		const string exchangeInfo =
			"{\"data\":{\"timeZone\":\"UTC\",\"serverTime\":1645091654418," +
			"\"symbols\":[{\"id\":1,\"name\":\"BTCTRY\"," +
			"\"nameNormalized\":\"BTC_TRY\",\"status\":\"TRADING\"," +
			"\"numerator\":\"BTC\",\"denominator\":\"TRY\"," +
			"\"numeratorScale\":8,\"denominatorScale\":0," +
			"\"hasFraction\":false,\"filters\":[{" +
			"\"filterType\":\"PRICE_FILTER\",\"tickSize\":\"10\"," +
			"\"minExchangeValue\":\"99.91\"}]," +
			"\"orderMethods\":[\"MARKET\",\"LIMIT\",\"STOP_MARKET\"," +
			"\"STOP_LIMIT\"]}]},\"success\":true,\"code\":0}";
		const string kline =
			"{\"s\":\"ok\",\"t\":[1716288000],\"h\":[65500.2]," +
			"\"o\":[65000.1],\"l\":[64800.3],\"c\":[65346.1]," +
			"\"v\":[12.5]}";

		var info = JsonConvert.DeserializeObject<
			BtcTurkResponse<BtcTurkExchangeInfo>>(exchangeInfo);
		var candles = JsonConvert.DeserializeObject<BtcTurkKline>(kline);

		IsTrue(info.IsSuccess);
		AreEqual("BTC", info.Data.Symbols[0].Numerator);
		AreEqual(10m, info.Data.Symbols[0].Filters[0].TickSize);
		AreEqual(1716288000L, candles.Timestamps[0]);
		AreEqual(65500.2m, candles.Highs[0]);
	}

	[TestMethod]
	public void WebSocketModelsUseOuterTypeAndPayload()
	{
		const string bookPayload =
			"[431,{\"CS\":160194,\"PS\":\"BTCTRY\"," +
			"\"AO\":[{\"A\":\"2.5\",\"P\":\"65301\"}]," +
			"\"BO\":[{\"A\":\"1.5\",\"P\":\"65300\"}]," +
			"\"channel\":\"orderbook\",\"event\":\"BTCTRY\"," +
			"\"type\":431}]";
		const string tradePayload =
			"[422,{\"PS\":\"BTCTRY\",\"A\":\"0.00911238\",\"S\":0," +
			"\"D\":1716288547800,\"P\":\"65346.1\",\"I\":\"987\"," +
			"\"type\":422}]";

		var book = BtcTurkWsClient.DeserializeEnvelope<
			BtcTurkWsOrderBook>(bookPayload);
		var trade = BtcTurkWsClient.DeserializeEnvelope<
			BtcTurkWsTrade>(tradePayload);

		AreEqual(BtcTurkWsMessageTypes.OrderBook, book.Type);
		AreEqual(160194L, book.Data.ChangeSet);
		AreEqual(65300m, book.Data.Bids[0].Price);
		AreEqual(65301m, book.Data.Asks[0].Price);
		AreEqual(BtcTurkWsMessageTypes.Trade, trade.Type);
		AreEqual(BtcTurkSides.Buy, trade.Data.Side);
		AreEqual("987", trade.Data.Id);
	}

	[TestMethod]
	public void SymbolsAndIntervalsUseBtcTurkFormat()
	{
		AreEqual("BTC/TRY", "btc_try".NormalizeSymbol());
		AreEqual("BTCTRY", "BTC/TRY".ToNativeSymbol());
		AreEqual(new SecurityId
		{
			SecurityCode = "BTC/TRY",
			BoardCode = BoardCodes.BtcTurk,
		}, "BTC_TRY".ToStockSharp());
		AreEqual("1", TimeSpan.FromMinutes(1).ToBtcTurkResolution());
		AreEqual("240", TimeSpan.FromHours(4).ToBtcTurkResolution());
		AreEqual("1D", TimeSpan.FromDays(1).ToBtcTurkResolution());
	}

	[TestMethod]
	public void OrderConditionKeepsTriggerPrice()
	{
		var condition = new BtcTurkOrderCondition
		{
			TriggerPrice = 65000.5m,
		};

		AreEqual(65000.5m, condition.TriggerPrice);
	}
}
