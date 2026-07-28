namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.DexScreener;
using StockSharp.DexScreener.Native;

[TestClass]
public class DexScreenerTests : BaseTestClass
{
	private const string _pairJson =
		"{\"chainId\":\"solana\",\"dexId\":\"orca\"," +
		"\"pairAddress\":\"PAIR123\",\"baseToken\":{" +
		"\"address\":\"BASE123\",\"name\":\"Wrapped SOL\"," +
		"\"symbol\":\"SOL\"},\"quoteToken\":{" +
		"\"address\":\"QUOTE123\",\"name\":\"USD Coin\"," +
		"\"symbol\":\"USDC\"},\"priceNative\":\"123.45\"," +
		"\"priceUsd\":\"124.10\",\"txns\":{\"h24\":{" +
		"\"buys\":321,\"sells\":123}},\"volume\":{" +
		"\"h24\":456789.12},\"priceChange\":{\"h24\":2.5}," +
		"\"liquidity\":{\"usd\":1000000,\"base\":4000," +
		"\"quote\":500000},\"fdv\":50000000," +
		"\"marketCap\":40000000," +
		"\"pairCreatedAt\":1722168000000}";

	[TestMethod]
	public void DefaultsUsePublicApiAndPublishedThrottle()
	{
		var adapter = new DexScreenerMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://api.dexscreener.com",
			adapter.RestEndpoint);
		AreEqual("USDC", adapter.SearchQuery);
		IsTrue(adapter.PriceInUsd);
		AreEqual(
			TimeSpan.FromMilliseconds(200),
			adapter.RequestInterval);
		AreEqual(
			TimeSpan.FromSeconds(30),
			adapter.PollingInterval);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsLookupMode()
	{
		var source = new DexScreenerMessageAdapter(
			new IncrementalIdGenerator())
		{
			RestEndpoint = "https://example.test/",
			ChainId = "solana",
			TokenAddress = "token",
			SearchQuery = "SOL/USDC",
			PriceInUsd = false,
			RequestInterval = TimeSpan.FromSeconds(1),
			PollingInterval = TimeSpan.FromMinutes(2),
			MaximumItems = 55,
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new DexScreenerMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual(
			"https://example.test",
			target.RestEndpoint);
		AreEqual("solana", target.ChainId);
		AreEqual("token", target.TokenAddress);
		AreEqual("SOL/USDC", target.SearchQuery);
		IsFalse(target.PriceInUsd);
		AreEqual(55, target.MaximumItems);
	}

	[TestMethod]
	public void SearchEnvelopeMapsPoolSnapshot()
	{
		var pair = DexScreenerRestClient.DeserializePairs(
			$"{{\"schemaVersion\":\"1.0\",\"pairs\":[{_pairJson}]}}")
			.Single();

		AreEqual("solana:PAIR123", pair.NativeId);
		AreEqual("SOL/USDC@orca:solana", pair.Symbol);
		AreEqual(123.45m, pair.PriceNative);
		AreEqual(124.10m, pair.PriceUsd);
		AreEqual(456789.12m, pair.Volume24Hours);
		AreEqual(2.5m, pair.PriceChange24Hours);
		AreEqual(1000000m, pair.LiquidityUsd);
		AreEqual(321, pair.Buys24Hours);
		AreEqual(123, pair.Sells24Hours);
	}

	[TestMethod]
	public void TokenPairsArrayUsesSameSchema()
	{
		var pair = DexScreenerRestClient.DeserializePairs(
			$"[{_pairJson}]").Single();

		AreEqual("BASE123", pair.BaseAddress);
		AreEqual("QUOTE123", pair.QuoteAddress);
		AreEqual("Wrapped SOL", pair.BaseName);
		AreEqual("USD Coin", pair.QuoteName);
		AreEqual(50000000m, pair.FullyDilutedValue);
		AreEqual(40000000m, pair.MarketCap);
	}

	[TestMethod]
	public void MissingRequiredPoolIdentityIsIgnored()
	{
		var pairs = DexScreenerRestClient.DeserializePairs(
			"{\"schemaVersion\":\"1.0\",\"pairs\":[{" +
			"\"chainId\":\"solana\",\"dexId\":\"orca\"," +
			"\"baseToken\":{\"symbol\":\"SOL\"}," +
			"\"quoteToken\":{\"symbol\":\"USDC\"}}]}");

		AreEqual(0, pairs.Length);
	}
}
