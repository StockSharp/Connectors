namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.CoinTR;
using StockSharp.CoinTR.Native;
using StockSharp.CoinTR.Native.Model;
using StockSharp.Messages;

[TestClass]
public class CoinTRTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUseOfficialServiceAddresses()
	{
		var adapter = new CoinTRMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.cointr.com", adapter.RestEndpoint);
		AreEqual("wss://ws.cointr.com/v2/ws/public",
			adapter.PublicWebSocketEndpoint);
		AreEqual("wss://ws.cointr.com/v2/ws/private",
			adapter.PrivateWebSocketEndpoint);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new CoinTRMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "public-key".Secure(),
			Secret = "private-secret".Secure(),
			Passphrase = "passphrase".Secure(),
			RestEndpoint = "https://rest.example.test/base/",
			PublicWebSocketEndpoint = "wss://public.example.test/feed/",
			PrivateWebSocketEndpoint = "wss://private.example.test/feed/",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new CoinTRMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("public-key", target.Key.UnSecure());
		AreEqual("private-secret", target.Secret.UnSecure());
		AreEqual("passphrase", target.Passphrase.UnSecure());
		AreEqual("https://rest.example.test/base", target.RestEndpoint);
		AreEqual("wss://public.example.test/feed",
			target.PublicWebSocketEndpoint);
		AreEqual("wss://private.example.test/feed",
			target.PrivateWebSocketEndpoint);
	}

	[TestMethod]
	public void SignatureUsesSortedQueryAndExactBody()
	{
		var query = new Dictionary<string, string>
		{
			["symbol"] = "BTCUSDT",
			["limit"] = "20",
		};
		var prehash = CoinTRAuthenticator.BuildPrehash(
			1710000000123,
			"get",
			"/api/v2/spot/trade/unfilled-orders",
			query,
			string.Empty);

		AreEqual(
			"1710000000123GET/api/v2/spot/trade/unfilled-orders" +
			"?limit=20&symbol=BTCUSDT",
			prehash);
		AreEqual(
			"YM5roV5J7xeHP8rv7Q2Th0eTWWiBxzfZzvDJua97azY=",
			CoinTRAuthenticator.CreateSignature(
				"private-secret", prehash));

		const string body = "{\"symbol\":\"BTCUSDT\",\"size\":\"1\"}";
		AreEqual(
			"1710000000123POST/api/v2/spot/trade/place-order" + body,
			CoinTRAuthenticator.BuildPrehash(
				1710000000123,
				"POST",
				"/api/v2/spot/trade/place-order",
				null,
				body));
	}

	[TestMethod]
	public void PublicModelsUseDocumentedResponseShape()
	{
		const string symbolsJson =
			"{\"code\":\"00000\",\"msg\":\"success\",\"data\":[{" +
			"\"symbol\":\"BTCUSDT\",\"baseCoin\":\"BTC\"," +
			"\"quoteCoin\":\"USDT\",\"minTradeAmount\":\"0.0001\"," +
			"\"maxTradeAmount\":\"100\",\"pricePrecision\":\"2\"," +
			"\"quantityPrecision\":\"6\",\"quotePrecision\":\"2\"," +
			"\"status\":\"online\"}]}";
		const string candleJson =
			"{\"code\":\"00000\",\"msg\":\"success\",\"data\":[[" +
			"\"1716288000000\",\"65000.1\",\"65500.2\",\"64800.3\"," +
			"\"65346.1\",\"12.5\",\"816826.25\",\"816826.25\"]]}";

		var symbols = JsonConvert.DeserializeObject<
			CoinTRResponse<CoinTRSymbol[]>>(symbolsJson);
		var candles = JsonConvert.DeserializeObject<
			CoinTRResponse<CoinTRCandle[]>>(candleJson);

		AreEqual("BTC", symbols.Data[0].BaseCoin);
		AreEqual(2, symbols.Data[0].PricePrecision);
		AreEqual(1716288000000L, candles.Data[0].Timestamp);
		AreEqual(65500.2m, candles.Data[0].High);
		AreEqual(12.5m, candles.Data[0].BaseVolume);
	}

	[TestMethod]
	public void WebSocketModelsUseChannelEnvelope()
	{
		const string tickerJson =
			"{\"action\":\"snapshot\",\"arg\":{\"instType\":\"SPOT\"," +
			"\"channel\":\"ticker\",\"instId\":\"BTCUSDT\"},\"data\":[{" +
			"\"instId\":\"BTCUSDT\",\"lastPr\":\"65346.1\"," +
			"\"bidPr\":\"65345.9\",\"askPr\":\"65346.2\"," +
			"\"bidSz\":\"0.3\",\"askSz\":\"0.4\"," +
			"\"baseVolume\":\"12.5\",\"ts\":\"1716288547800\"}]," +
			"\"ts\":1716288547801}";
		const string bookJson =
			"{\"action\":\"snapshot\",\"arg\":{\"instType\":\"SPOT\"," +
			"\"channel\":\"books15\",\"instId\":\"BTCUSDT\"},\"data\":[{" +
			"\"bids\":[[\"65345.9\",\"0.3\"]]," +
			"\"asks\":[[\"65346.2\",\"0.4\"]]," +
			"\"ts\":\"1716288547800\"}]}";

		var ticker = CoinTRWsClient.DeserializePush<
			CoinTRTicker>(tickerJson);
		var book = CoinTRWsClient.DeserializePush<
			CoinTROrderBook>(bookJson);

		AreEqual("ticker", ticker.Argument.Channel);
		AreEqual("BTCUSDT", ticker.Argument.InstrumentId);
		AreEqual(65346.1m, ticker.Data[0].LastPrice);
		AreEqual(1716288547800L, ticker.Data[0].Timestamp);
		AreEqual(65345.9m, book.Data[0].Bids[0].Price);
		AreEqual(0.4m, book.Data[0].Asks[0].Size);
	}

	[TestMethod]
	public void TradingModelsUseDocumentedWireNames()
	{
		const string orderJson =
			"{\"code\":\"00000\",\"msg\":\"success\",\"data\":[{" +
			"\"symbol\":\"BTCUSDT\",\"orderId\":\"2222222\"," +
			"\"clientOid\":\"ss-42\",\"price\":\"65000\"," +
			"\"priceAvg\":\"64990\",\"size\":\"0.5\"," +
			"\"orderType\":\"limit\",\"side\":\"buy\"," +
			"\"status\":\"partially_filled\",\"baseVolume\":\"0.2\"," +
			"\"quoteVolume\":\"12998\",\"cTime\":\"1716288000000\"," +
			"\"uTime\":\"1716288547800\"}]}";

		var response = JsonConvert.DeserializeObject<
			CoinTRResponse<CoinTROrder[]>>(orderJson);
		var order = response.Data[0];

		AreEqual("2222222", order.OrderId);
		AreEqual(0.2m, order.BaseVolume);
		AreEqual(OrderStates.Active, order.Status.ToOrderState());
		AreEqual(Sides.Buy, order.Side.ToSide());
	}

	[TestMethod]
	public void SymbolsAndIntervalsUseCoinTRFormat()
	{
		var symbol = new CoinTRSymbol
		{
			Symbol = "BTCUSDT",
			BaseCoin = "BTC",
			QuoteCoin = "USDT",
		};

		AreEqual("BTC/USDT", symbol.SecurityCode);
		AreEqual(new SecurityId
		{
			SecurityCode = "BTC/USDT",
			BoardCode = BoardCodes.CoinTR,
		}, symbol.ToStockSharp());
		AreEqual("1min",
			TimeSpan.FromMinutes(1).ToCoinTRGranularity());
		AreEqual("4H",
			TimeSpan.FromHours(4).ToCoinTRWebSocketInterval());
		AreEqual(TimeSpan.FromDays(1),
			"1D".ToCoinTRTimeFrame());
	}
}
