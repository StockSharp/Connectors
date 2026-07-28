namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Messages;
using StockSharp.Tokocrypto;
using StockSharp.Tokocrypto.Native;
using StockSharp.Tokocrypto.Native.Model;

[TestClass]
public class TokocryptoTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUseOfficialServiceAddresses()
	{
		var adapter = new TokocryptoMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://www.tokocrypto.com",
			adapter.AccountRestEndpoint);
		AreEqual("https://www.tokocrypto.site/api/v3",
			adapter.MarketDataRestEndpoint);
		AreEqual("wss://stream-cloud.tokocrypto.site/stream",
			adapter.WebSocketEndpoint);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new TokocryptoMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "public-key".Secure(),
			Secret = "private-secret".Secure(),
			AccountRestEndpoint = "https://account.example.test/",
			MarketDataRestEndpoint =
				"https://market.example.test/api/v3/",
			WebSocketEndpoint = "wss://stream.example.test/",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new TokocryptoMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("public-key", target.Key.UnSecure());
		AreEqual("private-secret", target.Secret.UnSecure());
		AreEqual("https://account.example.test",
			target.AccountRestEndpoint);
		AreEqual("https://market.example.test/api/v3",
			target.MarketDataRestEndpoint);
		AreEqual("wss://stream.example.test",
			target.WebSocketEndpoint);
	}

	[TestMethod]
	public void SignatureMatchesOfficialHmacExample()
	{
		const string query =
			"symbol=BTC_USDT&side=0&type=1&quantity=0.16&" +
			"price=7500&timestamp=1581720670624&recvWindow=5000";
		const string secret =
			"f9AbA6a8AD6bC2a97294a212244dda04ETfl0kc4BSUGOtL7m7rNELpt3Jh25SiP";

		AreEqual(
			"33824b5160daefc34257ab9cd3c3db7a0158a446674f896c9fc3b122ae656bfa",
			TokocryptoAuthenticator.CreateSignature(secret, query));
	}

	[TestMethod]
	public void SymbolsUseCurrentEnvelopeAndFilters()
	{
		const string json =
			"{\"code\":0,\"msg\":\"Success\",\"data\":{\"list\":[{" +
			"\"type\":1,\"symbol\":\"BTC_USDT\",\"baseAsset\":\"BTC\"," +
			"\"basePrecision\":8,\"quoteAsset\":\"USDT\"," +
			"\"quotePrecision\":8,\"filters\":[{" +
			"\"filterType\":\"PRICE_FILTER\",\"minPrice\":\"0.01\"," +
			"\"maxPrice\":\"1000000\",\"tickSize\":\"0.01\"},{" +
			"\"filterType\":\"LOT_SIZE\",\"minQty\":\"0.00001\"," +
			"\"maxQty\":\"1000\",\"stepSize\":\"0.00001\"}]," +
			"\"orderTypes\":[\"LIMIT\",\"MARKET\"]," +
			"\"spotTradingEnable\":1}]},\"timestamp\":1785197620955}";

		var response = JsonConvert.DeserializeObject<
			TokocryptoResponse<TokocryptoSymbolList>>(json);
		var symbol = response.Data.List[0];

		AreEqual("BTC/USDT", symbol.SecurityCode);
		AreEqual(0.01m, symbol.PriceStep);
		AreEqual(0.00001m, symbol.VolumeStep);
		AreEqual(0.00001m, symbol.MinimumVolume);
		IsTrue(symbol.IsSpotTradingEnabled);
	}

	[TestMethod]
	public void PublicModelsUseCurrentRawMarketShape()
	{
		const string tickerJson =
			"{\"symbol\":\"BTCUSDT\",\"priceChange\":\"-1632.41\"," +
			"\"lastPrice\":\"63611.02\",\"bidPrice\":\"63611.02\"," +
			"\"bidQty\":\"4.52557\",\"askPrice\":\"63611.03\"," +
			"\"askQty\":\"3.74782\",\"openPrice\":\"65243.43\"," +
			"\"highPrice\":\"65744.6\",\"lowPrice\":\"63500\"," +
			"\"volume\":\"14873.44245\",\"closeTime\":1785197632010}";
		const string depthJson =
			"{\"lastUpdateId\":97919231662," +
			"\"bids\":[[\"63611.02\",\"1.49592\"]]," +
			"\"asks\":[[\"63611.03\",\"4.24874\"]]}";
		const string tradeJson =
			"[{\"id\":6537928873,\"price\":\"63611.02\"," +
			"\"qty\":\"0.00009\",\"time\":1785197630925," +
			"\"isBuyerMaker\":true,\"isBestMatch\":true}]";

		var ticker = JsonConvert.DeserializeObject<
			TokocryptoTicker>(tickerJson);
		var depth = JsonConvert.DeserializeObject<
			TokocryptoOrderBook>(depthJson);
		var trades = JsonConvert.DeserializeObject<
			TokocryptoTrade[]>(tradeJson);

		AreEqual(63611.02m, ticker.LastPrice);
		AreEqual(4.52557m, ticker.BidVolume);
		AreEqual(63611.03m, depth.Asks[0][0]);
		AreEqual(6537928873L, trades[0].Id);
		IsTrue(trades[0].IsBuyerMaker);
	}

	[TestMethod]
	public void KlinesUseBinanceCompatibleArrayShape()
	{
		const string json =
			"[[1785197520000,\"63616\",\"63672.67\",\"63616\"," +
			"\"63636.89\",\"9.2964\",1785197579999," +
			"\"591672.9295\",3813,\"4.56354\",\"290446.8868\",\"0\"]]";

		var candles = TokocryptoRestClient.DeserializeKlines(json);

		AreEqual(1, candles.Length);
		AreEqual(1785197520000L, candles[0].OpenTime);
		AreEqual(63672.67m, candles[0].High);
		AreEqual(9.2964m, candles[0].Volume);
	}

	[TestMethod]
	public void WebSocketModelsUseCombinedStreamEnvelope()
	{
		const string tradeJson =
			"{\"stream\":\"btcusdt@trade\",\"data\":{" +
			"\"e\":\"trade\",\"E\":1785197631437,\"s\":\"BTCUSDT\"," +
			"\"t\":6537928875,\"p\":\"63611.02\",\"q\":\"0.00077\"," +
			"\"T\":1785197631437,\"m\":true}}";
		const string depthJson =
			"{\"stream\":\"btcusdt@depth5@100ms\",\"data\":{" +
			"\"lastUpdateId\":97919249519," +
			"\"bids\":[[\"63634.32\",\"3.08138\"]]," +
			"\"asks\":[[\"63634.33\",\"2.38241\"]]}}";

		var trade = TokocryptoWsClient.DeserializeEnvelope<
			TokocryptoStreamTrade>(tradeJson);
		var depth = TokocryptoWsClient.DeserializeEnvelope<
			TokocryptoOrderBook>(depthJson);

		AreEqual("btcusdt@trade", trade.Stream);
		AreEqual(6537928875L, trade.Data.Id);
		AreEqual(63611.02m, trade.Data.Price);
		AreEqual("btcusdt@depth5@100ms", depth.Stream);
		AreEqual(63634.33m, depth.Data.Asks[0][0]);
	}

	[TestMethod]
	public void SymbolsAndIntervalsUseTokocryptoFormat()
	{
		AreEqual("BTC/USDT",
			"BTC_USDT".ToTokocryptoSecurityCode());
		AreEqual("BTC_USDT",
			"BTC/USDT".ToTokocryptoAccountSymbol());
		AreEqual("BTCUSDT",
			"BTC/USDT".ToTokocryptoMarketSymbol());
		AreEqual(new SecurityId
		{
			SecurityCode = "BTC/USDT",
			BoardCode = BoardCodes.Tokocrypto,
		}, new TokocryptoSymbol
		{
			Symbol = "BTC_USDT",
			BaseAsset = "BTC",
			QuoteAsset = "USDT",
		}.ToStockSharp());
		AreEqual("1m",
			TimeSpan.FromMinutes(1).ToTokocryptoInterval());
		AreEqual("1M",
			TimeSpan.FromTicks(TimeHelper.TicksPerMonth)
				.ToTokocryptoInterval());
	}

	[TestMethod]
	public void OrderConditionKeepsStopParameters()
	{
		var condition = new TokocryptoOrderCondition
		{
			StopPrice = 62000m,
			PostOnly = true,
		};

		AreEqual(62000m, condition.StopPrice);
		IsTrue(condition.PostOnly);
	}
}
