namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Coinstore;
using StockSharp.Coinstore.Native;
using StockSharp.Coinstore.Native.Model;
using StockSharp.Messages;

[TestClass]
public class CoinstoreTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUseOfficialServiceAddresses()
	{
		var adapter = new CoinstoreMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.coinstore.com/api",
			adapter.RestEndpoint);
		AreEqual("wss://ws.coinstore.com/s/ws",
			adapter.WebSocketEndpoint);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new CoinstoreMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "public-key".Secure(),
			Secret = "private-secret".Secure(),
			RestEndpoint = "https://rest.example.test/api/",
			WebSocketEndpoint = "wss://stream.example.test/ws/",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new CoinstoreMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("public-key", target.Key.UnSecure());
		AreEqual("private-secret", target.Secret.UnSecure());
		AreEqual("https://rest.example.test/api",
			target.RestEndpoint);
		AreEqual("wss://stream.example.test/ws",
			target.WebSocketEndpoint);
	}

	[TestMethod]
	public void SignatureUsesDocumentedThirtySecondKey()
	{
		const long expires = 1629291143107;

		AreEqual(
			"a27a05e9c182ca3fd7facd4dfc78a767f79eace381d4dee66db6c83729c6d088",
			CoinstoreAuthenticator.CreateSignature(
				"secret", expires,
				"{\"symbol\":\"BTCUSDT\",\"side\":\"BUY\"}"));
		AreEqual(
			"c8274c6824053e872e24f58c746819f22f4b5480da1a208a52813753a9de054b",
			CoinstoreAuthenticator.CreateSignature(
				"secret", expires, string.Empty));
	}

	[TestMethod]
	public void SymbolModelsUsePrecisionAndLimits()
	{
		const string json =
			"{\"code\":\"0\",\"message\":\"Succeed\",\"data\":[{" +
			"\"symbolId\":4,\"symbolCode\":\"btcUSDT\"," +
			"\"tradeCurrencyCode\":\"btc\"," +
			"\"quoteCurrencyCode\":\"USDT\",\"openTrade\":true," +
			"\"tickSz\":2,\"lotSz\":6,\"minLmtPr\":\"0.01\"," +
			"\"minLmtSz\":\"0.000001\",\"minMktVa\":\"0.1\"}]}";

		var symbols = CoinstoreRestClient.Deserialize<
			CoinstoreSymbol[]>(json);

		AreEqual(1, symbols.Length);
		AreEqual("BTC/USDT", symbols[0].SecurityCode);
		AreEqual(0.01m, symbols[0].PriceStep);
		AreEqual(0.000001m, symbols[0].VolumeStep);
		AreEqual(0.1m, symbols[0].MinimumMarketValue);
	}

	[TestMethod]
	public void PublicMarketModelsUseCurrentResponseShapes()
	{
		const string tickerJson =
			"{\"code\":0,\"data\":[{\"symbol\":\"BTCUSDT\"," +
			"\"instrumentId\":4,\"close\":\"63199.92\"," +
			"\"open\":\"64980.5\",\"high\":\"65724.35\"," +
			"\"low\":\"63023.61\",\"volume\":\"279.543054\"," +
			"\"amount\":\"18121124.5\",\"bid\":\"63198.86\"," +
			"\"bidSize\":\"3.471\",\"ask\":\"63201.95\"," +
			"\"askSize\":\"0.017269\"}]}";
		const string depthJson =
			"{\"code\":0,\"data\":{\"channel\":\"4@depth@5\"," +
			"\"a\":[[\"63201.95\",\"0.017269\",-1]]," +
			"\"b\":[[\"63198.86\",\"3.471\",1]],\"level\":5," +
			"\"lastPrice\":\"63199.92\",\"symbol\":\"BTCUSDT\"," +
			"\"instrumentId\":4}}";
		const string tradesJson =
			"{\"code\":0,\"data\":[{\"time\":1785201328," +
			"\"tradeId\":245459233,\"price\":\"63201.66\"," +
			"\"volume\":\"0.000214\",\"takerSide\":\"SELL\"," +
			"\"symbol\":\"BTCUSDT\",\"instrumentId\":4}]}";

		var tickers = CoinstoreRestClient.Deserialize<
			CoinstoreTicker[]>(tickerJson);
		var depth = CoinstoreRestClient.Deserialize<
			CoinstoreOrderBook>(depthJson);
		var trades = CoinstoreRestClient.Deserialize<
			CoinstoreTrade[]>(tradesJson);

		AreEqual(63199.92m, tickers[0].Close);
		AreEqual(63198.86m, depth.Bids[0][0]);
		AreEqual(0.017269m, depth.Asks[0][1]);
		AreEqual(245459233L, trades[0].TradeId);
		AreEqual("SELL", trades[0].TakerSide);
	}

	[TestMethod]
	public void CandleModelsUseSecondsAndDocumentedInterval()
	{
		const string json =
			"{\"code\":0,\"data\":{\"channel\":\"4@kline@min_1\"," +
			"\"item\":[{\"startTime\":1785201300," +
			"\"endTime\":1785201359,\"interval\":\"min_1\"," +
			"\"open\":\"63173.38\",\"high\":\"63202.75\"," +
			"\"low\":\"63172.18\",\"close\":\"63201.09\"," +
			"\"volume\":\"1.117271\",\"amount\":\"70609.3\"}]," +
			"\"symbol\":\"BTCUSDT\",\"instrumentId\":4}}";

		var candles = CoinstoreRestClient.Deserialize<
			CoinstoreKlineResult>(json);

		AreEqual(1, candles.Items.Length);
		AreEqual("min_1", candles.Items[0].Interval);
		AreEqual(63173.38m, candles.Items[0].Open);
		AreEqual(1785201300L, candles.Items[0].StartTime);
	}

	[TestMethod]
	public void WebSocketModelsHandleSnapshotAndIncrementalShapes()
	{
		const string tradeJson =
			"{\"S\":6,\"T\":\"trade\",\"data\":[{" +
			"\"channel\":\"4@trade\",\"time\":1785201381," +
			"\"tradeId\":245459384,\"price\":\"63203.83\"," +
			"\"volume\":\"0.000009\",\"takerSide\":\"BUY\"," +
			"\"symbol\":\"BTCUSDT\",\"instrumentId\":4}]}";
		const string depthJson =
			"{\"S\":12,\"T\":\"depth\",\"channel\":\"4@depth@5\"," +
			"\"level\":5,\"a\":[[\"63196.62\",\"0.010001\",-1]]," +
			"\"b\":[[\"63193.34\",\"0.036885\",1]]," +
			"\"symbol\":\"BTCUSDT\",\"instrumentId\":4}";

		var trade = CoinstoreWsClient.DeserializeMessage(tradeJson);
		var depth = CoinstoreWsClient.DeserializeMessage(depthJson);

		AreEqual("trade", trade.Type);
		AreEqual(1, trade.Data.Length);
		AreEqual(245459384L, trade.Data[0].TradeId);
		AreEqual("depth", depth.Type);
		AreEqual(63193.34m, depth.Bids[0][0]);
	}

	[TestMethod]
	public void SymbolsAndIntervalsUseCoinstoreFormats()
	{
		var symbol = new CoinstoreSymbol
		{
			SymbolId = 4,
			SymbolCode = "btcUSDT",
			BaseCurrency = "btc",
			QuoteCurrency = "USDT",
			TickPrecision = 2,
			LotPrecision = 6,
		};

		AreEqual(new SecurityId
		{
			SecurityCode = "BTC/USDT",
			BoardCode = BoardCodes.Coinstore,
		}, symbol.ToStockSharp());
		AreEqual("BTCUSDT", "BTC/USDT".ToCoinstoreSymbol());
		AreEqual("1min",
			TimeSpan.FromMinutes(1).ToCoinstoreRestPeriod());
		AreEqual("min_1",
			TimeSpan.FromMinutes(1).ToCoinstoreStreamPeriod());
		IsTrue(CoinstoreExtensions.TimeFrames.Contains(
			TimeSpan.FromDays(1)));
	}

	[TestMethod]
	public void OrderConditionKeepsCoinstoreParameters()
	{
		var condition = new CoinstoreOrderCondition
		{
			PostOnly = true,
			TimeInForce = CoinstoreTimeInForce.ImmediateOrCancel,
		};

		IsTrue(condition.PostOnly);
		AreEqual(CoinstoreTimeInForce.ImmediateOrCancel,
			condition.TimeInForce);
	}
}
