namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.BitoPro;
using StockSharp.BitoPro.Native;
using StockSharp.BitoPro.Native.Model;
using StockSharp.Messages;

[TestClass]
public class BitoProTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUseOfficialServiceAddresses()
	{
		var adapter = new BitoProMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.bitopro.com/v3", adapter.RestEndpoint);
		AreEqual("wss://stream.bitopro.com:443/ws",
			adapter.WebSocketEndpoint);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new BitoProMessageAdapter(
			new IncrementalIdGenerator())
		{
			Email = "trader@example.test",
			Key = "public-key".Secure(),
			Secret = "private-secret".Secure(),
			RestEndpoint = "https://rest.example.test/v3/",
			WebSocketEndpoint = "wss://stream.example.test/ws/",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new BitoProMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("trader@example.test", target.Email);
		AreEqual("public-key", target.Key.UnSecure());
		AreEqual("private-secret", target.Secret.UnSecure());
		AreEqual("https://rest.example.test/v3", target.RestEndpoint);
		AreEqual("wss://stream.example.test/ws",
			target.WebSocketEndpoint);
	}

	[TestMethod]
	public void SignatureMatchesOfficialHmacSha384Example()
	{
		const string payload =
			"eyJpZGVudGl0eSI6ImhjbWxpbmpAZ21haWwuY29tIiwibm9uY2Ui" +
			"OjE1NTQzODA5MDkxMzF9";

		AreEqual(payload, BitoProAuthenticator.CreateGetPayload(
			"hcmlinj@gmail.com", 1554380909131));
		AreEqual(
			"01a85a9083db47c20da7196380598f3feacd3c76a9077aaf7" +
			"ffaf08ce0091abf65b61778792607b010921adfe1c2941a",
			BitoProAuthenticator.CreateSignature("bitopro", payload));
	}

	[TestMethod]
	public void OrderBodyUsesDocumentedWireNames()
	{
		var json = BitoProRestClient.SerializeBody(
			new BitoProPlaceOrderRequest
			{
				Action = "BUY",
				Amount = "0.25",
				Price = "2060000",
				Timestamp = 1710000000123,
				Type = "LIMIT",
				TimeInForce = "POST_ONLY",
				ClientId = 42,
			});

		IsTrue(json.Contains("\"action\":\"BUY\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"amount\":\"0.25\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"timeInForce\":\"POST_ONLY\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"clientId\":42",
			StringComparison.Ordinal));
		IsFalse(json.Contains("\"Action\":", StringComparison.Ordinal));
	}

	[TestMethod]
	public void PublicModelsUseCurrentResponseShape()
	{
		const string pairsJson =
			"{\"data\":[{\"pair\":\"btc_twd\",\"base\":\"btc\"," +
			"\"quote\":\"twd\",\"basePrecision\":\"8\"," +
			"\"quotePrecision\":\"0\",\"minLimitBaseAmount\":\"0.0001\"," +
			"\"maxLimitBaseAmount\":\"100000000\"," +
			"\"orderBookQuotePrecision\":\"0\"," +
			"\"orderBookQuoteScaleLevel\":\"5\"," +
			"\"amountPrecision\":\"4\",\"maintain\":false}]}";
		const string tickerJson =
			"{\"data\":{\"pair\":\"btc_twd\"," +
			"\"lastPrice\":\"2066168.00000000\",\"isBuyer\":true," +
			"\"priceChange24hr\":\"-2.39\",\"volume24hr\":\"6.19827360\"," +
			"\"high24hr\":\"2128835\",\"low24hr\":\"2058320\"}}";
		const string candlesJson =
			"{\"data\":[{\"timestamp\":1716288000000," +
			"\"open\":\"65000.1\",\"high\":\"65500.2\"," +
			"\"low\":\"64800.3\",\"close\":\"65346.1\"," +
			"\"volume\":\"12.5\"}]}";

		var pairs = JsonConvert.DeserializeObject<
			BitoProDataResponse<BitoProSymbol[]>>(pairsJson);
		var ticker = JsonConvert.DeserializeObject<
			BitoProDataResponse<BitoProTicker>>(tickerJson);
		var candles = JsonConvert.DeserializeObject<
			BitoProDataResponse<BitoProCandle[]>>(candlesJson);

		AreEqual("BTC/TWD", pairs.Data[0].SecurityCode);
		AreEqual(4, pairs.Data[0].AmountPrecision);
		AreEqual(2066168m, ticker.Data.LastPrice);
		AreEqual(-2.39m, ticker.Data.PriceChange);
		AreEqual(1716288000000L, candles.Data[0].Timestamp);
		AreEqual(65500.2m, candles.Data[0].High);
	}

	[TestMethod]
	public void WebSocketModelsUseEventEnvelope()
	{
		const string bookJson =
			"{\"event\":\"ORDER_BOOK\",\"eventID\":\"book-1\"," +
			"\"timestamp\":1716288547800,\"pair\":\"BTC_TWD\"," +
			"\"limit\":5,\"bids\":[{\"price\":\"65345.9\"," +
			"\"amount\":\"0.3\",\"count\":1,\"total\":\"0.3\"}]," +
			"\"asks\":[{\"price\":\"65346.2\",\"amount\":\"0.4\"," +
			"\"count\":1,\"total\":\"0.4\"}]}";
		const string tradeJson =
			"{\"event\":\"TRADE\",\"eventID\":\"trade-1\"," +
			"\"pair\":\"BTC_TWD\",\"timestamp\":1716288547800," +
			"\"data\":[{\"timestamp\":1716288547,\"price\":\"65346.1\"," +
			"\"amount\":\"0.00911238\",\"isBuyer\":true}]}";

		var book = BitoProWsClient.DeserializeMessage<
			BitoProOrderBook>(bookJson);
		var trades = BitoProWsClient.DeserializeMessage<
			BitoProTradePush>(tradeJson);

		AreEqual("ORDER_BOOK", book.Event);
		AreEqual("BTC_TWD", book.Pair);
		AreEqual(65345.9m, book.Bids[0].Price);
		AreEqual(65346.2m, book.Asks[0].Price);
		AreEqual("TRADE", trades.Event);
		AreEqual(65346.1m, trades.Data[0].Price);
		IsTrue(trades.Data[0].IsBuyer);
	}

	[TestMethod]
	public void SymbolsAndIntervalsUseBitoProFormat()
	{
		AreEqual("BTC/TWD", "btc_twd".ToBitoProSecurityCode());
		AreEqual("BTC_TWD", "BTC/TWD".ToBitoProSymbol());
		AreEqual(new SecurityId
		{
			SecurityCode = "BTC/TWD",
			BoardCode = BoardCodes.BitoPro,
		}, "btc_twd".ToBitoProSecurityId());
		AreEqual("1m",
			TimeSpan.FromMinutes(1).ToBitoProResolution());
		AreEqual("4h",
			TimeSpan.FromHours(4).ToBitoProResolution());
		AreEqual("1M",
			TimeSpan.FromTicks(TimeHelper.TicksPerMonth)
				.ToBitoProResolution());
	}

	[TestMethod]
	public void OrderConditionKeepsTriggerParameters()
	{
		var condition = new BitoProOrderCondition
		{
			TriggerPrice = 65000.5m,
			TriggerOnGreaterOrEqual = false,
		};

		AreEqual(65000.5m, condition.TriggerPrice);
		IsFalse(condition.TriggerOnGreaterOrEqual);
	}
}
