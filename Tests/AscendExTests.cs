namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.AscendEx;
using StockSharp.AscendEx.Native;
using StockSharp.AscendEx.Native.Model;
using StockSharp.Messages;

[TestClass]
public class AscendExTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUseOfficialServiceAddresses()
	{
		var adapter = new AscendExMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://ascendex.com", adapter.RestEndpoint);
		AreEqual(
			"wss://ascendex.com/0/api/pro/v1/stream",
			adapter.SpotWebSocketEndpoint);
		AreEqual(
			"wss://ascendex.com/api/pro/v2/stream",
			adapter.FuturesWebSocketEndpoint);
		AreEqual(0, adapter.AccountGroup);
		AreEqual(AscendExSpotAccountTypes.Cash,
			adapter.SpotAccountType);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new AscendExMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "public-key".Secure(),
			Secret = "private-secret".Secure(),
			AccountGroup = 7,
			SpotAccountType = AscendExSpotAccountTypes.Margin,
			RestEndpoint = "https://rest.example.test/",
			SpotWebSocketEndpoint =
				"wss://spot.example.test/stream/",
			FuturesWebSocketEndpoint =
				"wss://futures.example.test/stream/",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new AscendExMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("public-key", target.Key.UnSecure());
		AreEqual("private-secret", target.Secret.UnSecure());
		AreEqual(7, target.AccountGroup);
		AreEqual(AscendExSpotAccountTypes.Margin,
			target.SpotAccountType);
		AreEqual("https://rest.example.test",
			target.RestEndpoint);
		AreEqual("wss://spot.example.test/stream",
			target.SpotWebSocketEndpoint);
		AreEqual("wss://futures.example.test/stream",
			target.FuturesWebSocketEndpoint);
	}

	[TestMethod]
	public void SignatureMatchesOfficialExample()
	{
		AreEqual(
			"/pwaAgWZQ1Xd/J4yZ4ReHSPQxd3ORP/YR8TvAttqqYM=",
			AscendExAuthenticator.CreateSignature(
				"hV8FgjyJtpvVeAcMAgzgAFQCN36wmbWuN7o3WPcYcYhFd8qvE43gzFGVsFcCqMNk",
				1608133910000, "info"));
	}

	[TestMethod]
	public void SpotProductUsesDocumentedTradingFilters()
	{
		const string json =
			"{\"code\":0,\"data\":[{\"symbol\":\"BTC/USDT\"," +
			"\"displayName\":\"BTC/USDT\"," +
			"\"tradingStartTime\":1546300800000," +
			"\"minQty\":\"0.000000001\",\"maxQty\":\"1000000000\"," +
			"\"minNotional\":\"5\",\"maxNotional\":\"400000\"," +
			"\"statusCode\":\"Normal\",\"tickSize\":\"0.01\"," +
			"\"lotSize\":\"0.00001\",\"qtyScale\":5," +
			"\"priceScale\":2}]}";

		var products = AscendExRestClient.Deserialize<
			AscendExSpotProduct[]>(json);

		AreEqual(1, products.Length);
		AreEqual("BTC/USDT", products[0].SecurityCode);
		AreEqual(0.01m, products[0].PriceStep);
		AreEqual(0.00001m, products[0].VolumeStep);
		AreEqual(5m, products[0].MinimumNotional);
		IsTrue(products[0].IsTrading);
	}

	[TestMethod]
	public void FuturesContractUsesDocumentedFilters()
	{
		const string json =
			"{\"code\":0,\"data\":[{\"symbol\":\"BTC-PERP\"," +
			"\"status\":\"Normal\",\"displayName\":\"BTCUSDT\"," +
			"\"settlementAsset\":\"USDT\"," +
			"\"underlying\":\"BTC/USDT\"," +
			"\"priceFilter\":{\"minPrice\":\"0.25\"," +
			"\"maxPrice\":\"1000000\",\"tickSize\":\"0.25\"}," +
			"\"lotSizeFilter\":{\"minQty\":\"0.0001\"," +
			"\"maxQty\":\"1000000000\",\"lotSize\":\"0.0001\"}}]}";

		var contracts = AscendExRestClient.Deserialize<
			AscendExFuturesContract[]>(json);

		AreEqual(1, contracts.Length);
		AreEqual("BTC-PERP", contracts[0].SecurityCode);
		AreEqual("BTC/USDT", contracts[0].Underlying);
		AreEqual(0.25m, contracts[0].PriceStep);
		AreEqual(0.0001m, contracts[0].VolumeStep);
		IsTrue(contracts[0].IsTrading);
	}

	[TestMethod]
	public void PublicMarketModelsUseNestedResponseShapes()
	{
		const string tickerJson =
			"{\"code\":0,\"data\":{\"symbol\":\"BTC/USDT\"," +
			"\"open\":\"59488\",\"close\":\"56716\"," +
			"\"high\":\"59724\",\"low\":\"56672\"," +
			"\"volume\":\"208.7414\"," +
			"\"ask\":[\"56720\",\"0.2315\"]," +
			"\"bid\":[\"56712\",\"0.0024\"]}}";
		const string depthJson =
			"{\"code\":0,\"data\":{\"m\":\"depth-snapshot\"," +
			"\"symbol\":\"BTC/USDT\",\"data\":{\"seqnum\":5068757," +
			"\"ts\":1573165838976," +
			"\"asks\":[[\"56720\",\"0.2315\"]]," +
			"\"bids\":[[\"56712\",\"0.0024\"]]}}}";
		const string tradesJson =
			"{\"code\":0,\"data\":{\"m\":\"trades\"," +
			"\"symbol\":\"BTC/USDT\",\"data\":[{" +
			"\"seqnum\":144115191800016553," +
			"\"p\":\"56716\",\"q\":\"0.5\"," +
			"\"ts\":1573165890854,\"bm\":false}]}}";

		var ticker = AscendExRestClient.Deserialize<
			AscendExTicker>(tickerJson);
		var depth = AscendExRestClient.Deserialize<
			AscendExMarketEnvelope<AscendExOrderBook>>(depthJson);
		var trades = AscendExRestClient.Deserialize<
			AscendExMarketEnvelope<AscendExTrade[]>>(tradesJson);

		AreEqual(56716m, ticker.Close);
		AreEqual(56712m, ticker.Bid.Price);
		AreEqual(0.2315m, ticker.Ask.Volume);
		AreEqual(5068757L, depth.Data.Sequence);
		AreEqual(56720m, depth.Data.Asks[0][0]);
		AreEqual(144115191800016553L,
			trades.Data[0].Sequence);
		IsTrue(trades.Data[0].IsBuyer == true);
	}

	[TestMethod]
	public void BarModelsUseCurrentIntervalFormat()
	{
		const string json =
			"{\"code\":0,\"data\":[{\"m\":\"bar\"," +
			"\"s\":\"BTC/USDT\",\"data\":{\"i\":\"1\"," +
			"\"ts\":1575409260000,\"o\":\"0.05019\"," +
			"\"c\":\"0.05020\",\"h\":\"0.05027\"," +
			"\"l\":\"0.05017\",\"v\":\"1612\"}}]}";

		var bars = AscendExRestClient.Deserialize<
			AscendExBarEnvelope[]>(json);

		AreEqual(1, bars.Length);
		AreEqual("BTC/USDT", bars[0].Symbol);
		AreEqual("1", bars[0].Data.Interval);
		AreEqual(0.05019m, bars[0].Data.Open);
		AreEqual(1575409260000L, bars[0].Data.Timestamp);
	}

	[TestMethod]
	public void WebSocketModelsHandleAllPublicChannels()
	{
		const string bboJson =
			"{\"m\":\"bbo\",\"symbol\":\"BTC/USDT\"," +
			"\"data\":{\"ts\":1573068442532," +
			"\"bid\":[\"9309.11\",\"0.0197172\"]," +
			"\"ask\":[\"9309.12\",\"0.8851266\"]}}";
		const string barJson =
			"{\"m\":\"bar\",\"s\":\"BTC-PERP\",\"data\":{" +
			"\"i\":\"1\",\"ts\":1575398940000,\"o\":\"0.04993\"," +
			"\"c\":\"0.04970\",\"h\":\"0.04993\"," +
			"\"l\":\"0.04970\",\"v\":\"8052\"}}";

		var bbo = AscendExWsClient.DeserializeMessage(bboJson);
		var bar = AscendExWsClient.DeserializeMessage(barJson);

		AreEqual("bbo", bbo.Topic);
		AreEqual("BTC/USDT", bbo.Symbol);
		AreEqual(9309.11m,
			bbo.Data.ToObject<AscendExBbo>().Bid.Price);
		AreEqual("bar", bar.Topic);
		AreEqual("BTC-PERP", bar.Symbol);
		AreEqual("1",
			bar.Data.ToObject<AscendExBar>().Interval);
	}

	[TestMethod]
	public void SymbolsAndIntervalsUseAscendExFormats()
	{
		AreEqual("BTC/USDT",
			"btc_usdt".ToAscendExSpotSymbol());
		AreEqual("BTC-PERP",
			"btc-perp".ToAscendExFuturesSymbol());
		AreEqual("1",
			TimeSpan.FromMinutes(1).ToAscendExInterval());
		AreEqual(TimeSpan.FromDays(1),
			"1d".ToAscendExTimeFrame());
		IsTrue(AscendExExtensions.TimeFrames.Contains(
			TimeSpan.FromDays(7)));
	}

	[TestMethod]
	public void OrderConditionKeepsSpotAndFuturesParameters()
	{
		var condition = new AscendExOrderCondition
		{
			StopPrice = 56000m,
			PostOnly = true,
			ReduceOnly = true,
			Trigger = AscendExStopTriggers.MarkPrice,
		};

		AreEqual(56000m, condition.StopPrice);
		IsTrue(condition.PostOnly);
		IsTrue(condition.ReduceOnly);
		AreEqual(AscendExStopTriggers.MarkPrice,
			condition.Trigger);
	}
}
