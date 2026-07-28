namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Birdeye;
using StockSharp.Birdeye.Native;

[TestClass]
public class BirdeyeTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedPublicEndpoints()
	{
		var adapter = new BirdeyeMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://public-api.birdeye.so",
			adapter.RestEndpoint);
		AreEqual(
			"wss://public-api.birdeye.so/socket",
			adapter.WebSocketEndpoint);
		AreEqual(
			"https://birdeye.so",
			adapter.WebSocketOrigin);
		AreEqual("solana", adapter.Chain);
		IsFalse(adapter.StreamingEnabled);
		IsTrue(adapter.PriceInUsd);
		AreEqual(5000, adapter.HistoryLimit);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsHostsAndChain()
	{
		var source = new BirdeyeMessageAdapter(
			new IncrementalIdGenerator())
		{
			Token = "key".Secure(),
			RestEndpoint = "https://rest.example.test/",
			WebSocketEndpoint = "wss://ws.example.test/",
			WebSocketOrigin = "https://origin.example.test/",
			Chain = "Ethereum",
			TokenAddress = "0x1234",
			StreamingEnabled = true,
			PriceInUsd = false,
			MinimumLiquidity = 500,
			RequestInterval = TimeSpan.FromSeconds(2),
			PollingInterval = TimeSpan.FromMinutes(3),
			MaximumItems = 321,
			HistoryLimit = 123,
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new BirdeyeMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("key", target.Token.UnSecure());
		AreEqual(
			"https://rest.example.test",
			target.RestEndpoint);
		AreEqual(
			"wss://ws.example.test",
			target.WebSocketEndpoint);
		AreEqual(
			"https://origin.example.test",
			target.WebSocketOrigin);
		AreEqual("ethereum", target.Chain);
		AreEqual("0x1234", target.TokenAddress);
		IsTrue(target.StreamingEnabled);
		IsFalse(target.PriceInUsd);
		AreEqual(500m, target.MinimumLiquidity);
		AreEqual(321, target.MaximumItems);
		AreEqual(123, target.HistoryLimit);
	}

	[TestMethod]
	public void TokenListMapsMarketMetrics()
	{
		const string json =
			"{\"success\":true,\"data\":{\"tokens\":[{" +
			"\"address\":\"So11111111111111111111111111111111111111112\"," +
			"\"decimals\":9,\"symbol\":\"SOL\"," +
			"\"name\":\"Wrapped SOL\",\"price\":180.5," +
			"\"liquidity\":50000000,\"v24hUSD\":12000000," +
			"\"v24hChangePercent\":2.75,\"mc\":80000000000}]}}";

		var token = BirdeyeRestClient.DeserializeTokens(
			json, "solana").Single();

		AreEqual("SOL", token.Symbol);
		AreEqual("Wrapped SOL", token.Name);
		AreEqual(9, token.Decimals);
		AreEqual(180.5m, token.Price);
		AreEqual(50000000m, token.Liquidity);
		AreEqual(12000000m, token.Volume24Hours);
		AreEqual(2.75m, token.PriceChange24Hours);
	}

	[TestMethod]
	public void OverviewMapsCurrentSnapshot()
	{
		const string address =
			"So11111111111111111111111111111111111111112";
		const string json =
			"{\"success\":true,\"data\":{" +
			"\"address\":\"" + address + "\"," +
			"\"symbol\":\"SOL\",\"name\":\"Wrapped SOL\"," +
			"\"decimals\":9,\"price\":181.25," +
			"\"liquidity\":51000000,\"volume24hUSD\":13000000," +
			"\"priceChange24hPercent\":3.25," +
			"\"marketCap\":81000000000,\"fdv\":82000000000," +
			"\"lastTradeUnixTime\":1722168000}}";

		var token = BirdeyeRestClient.DeserializeOverview(
			json, "solana", address);

		AreEqual(address, token.Address);
		AreEqual(181.25m, token.Price);
		AreEqual(13000000m, token.Volume24Hours);
		AreEqual(3.25m, token.PriceChange24Hours);
		AreEqual(81000000000m, token.MarketCap);
		AreEqual(82000000000m, token.FullyDilutedValue);
	}

	[TestMethod]
	public void HistoricalOhlcvIsParsed()
	{
		const string address =
			"So11111111111111111111111111111111111111112";
		const string json =
			"{\"success\":true,\"data\":{\"items\":[{" +
			"\"unixTime\":1722168000,\"o\":180,\"h\":182," +
			"\"l\":179,\"c\":181,\"v\":1234.5," +
			"\"vUsd\":223456.7}]}}";

		var candle = BirdeyeRestClient.DeserializeCandles(
			json,
			address,
			TimeSpan.FromMinutes(1)).Single();

		AreEqual(address, candle.Address);
		AreEqual(180m, candle.Open);
		AreEqual(182m, candle.High);
		AreEqual(179m, candle.Low);
		AreEqual(181m, candle.Close);
		AreEqual(1234.5m, candle.Volume);
		AreEqual(223456.7m, candle.VolumeUsd);
	}

	[TestMethod]
	public void WebSocketPayloadContainsFullComplexQuery()
	{
		var json = BirdeyeWebSocketClient
			.BuildSubscriptionPayload(
				[
					(
						"So11111111111111111111111111111111111111112",
						"1m"),
					(
						"JUPyiwrYJFskUPiHa7hkeR8VUtAeFoSYbKedZNsDvCN",
						"5m"),
				],
				true);
		var message = JObject.Parse(json);
		var query = message["data"]?.Value<string>("query");

		AreEqual(
			"SUBSCRIBE_PRICE",
			message.Value<string>("type"));
		AreEqual("complex", message["data"]?
			.Value<string>("queryType"));
		IsTrue(query.Contains("chartType = 1m"));
		IsTrue(query.Contains("chartType = 5m"));
		IsTrue(query.Contains("currency = usd"));
	}

	[TestMethod]
	public void WebSocketPricePrefersScaledValues()
	{
		const string json =
			"{\"type\":\"PRICE_DATA\",\"data\":{" +
			"\"eventType\":\"ohlcv\",\"type\":\"1m\"," +
			"\"unixTime\":1779178680,\"vUsd\":845.46," +
			"\"address\":\"XsDoVfqeBukxuZHWhdvWHBhgEHjGNst4MLodqsJHzoB\"," +
			"\"o\":40,\"h\":41,\"l\":39,\"c\":40.5,\"v\":2," +
			"\"scaledO\":408.06,\"scaledH\":408.36," +
			"\"scaledL\":407.70,\"scaledC\":408.11," +
			"\"scaledV\":2.07}}";

		var candle =
			BirdeyeWebSocketClient.DeserializePrice(json);

		AreEqual(TimeSpan.FromMinutes(1), candle.TimeFrame);
		AreEqual(408.06m, candle.Open);
		AreEqual(408.36m, candle.High);
		AreEqual(407.70m, candle.Low);
		AreEqual(408.11m, candle.Close);
		AreEqual(2.07m, candle.Volume);
		AreEqual(845.46m, candle.VolumeUsd);
	}
}
