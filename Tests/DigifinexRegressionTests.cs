namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Digifinex.Native;

[TestClass]
public class DigifinexRegressionTests : BaseTestClass
{
	[TestMethod]
	[DataRow("spot", "{}")]
	[DataRow("margin", "{}")]
	[DataRow("spot", "[]")]
	[DataRow("margin", "[]")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task EmptySymbolListsUseThePublishedEmptyShapes(string market, string symbols)
	{
		await using var server = new HistoryHttpServer(() => $"{{\"code\":0,\"symbol_list\":{symbols}}}", CancellationToken);
		using var client = CreateClient(server.Address);

		var result = market == "spot" ? await client.GetSpotSymbols(CancellationToken) : await client.GetMarginSymbols(CancellationToken);

		AreEqual(0, result.Count());
		AreEqual($"/v3/{market}/symbols", server.RequestTarget);
	}

	[TestMethod]
	[DataRow("spot")]
	[DataRow("margin")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task NonEmptySymbolListsKeepTheirEntries(string market)
	{
		await using var server = new HistoryHttpServer(() => """{"code":0,"symbol_list":[{"symbol":"BTC_USDT","base_asset":"BTC","quote_asset":"USDT","price_precision":2}]}""", CancellationToken);
		using var client = CreateClient(server.Address);
		var result = (market == "spot" ? await client.GetSpotSymbols(CancellationToken) : await client.GetMarginSymbols(CancellationToken)).ToArray();

		AreEqual(1, result.Length);
		AreEqual("BTC_USDT", result[0].Code);
		AreEqual(2, result[0].PricePrecision);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task MalformedSymbolObjectsStillFail()
	{
		await using var server = new HistoryHttpServer(() => """{"code":0,"symbol_list":{"unexpected":true}}""", CancellationToken);
		using var client = CreateClient(server.Address);
		await ThrowsAsync<InvalidOperationException>(() => client.GetMarginSymbols(CancellationToken));
	}

	private static HttpClient CreateClient(Uri endpoint)
	{
		var client = new HttpClient(null, null, "com");
		typeof(HttpClient).GetField("_baseUrl", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(client, endpoint.AbsoluteUri.TrimEnd('/'));
		return client;
	}
}
