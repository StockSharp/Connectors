namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.EdgeX.Native.Common;

[TestClass]
[CLSCompliant(false)]
public class EdgeXRegressionTests : BaseTestClass
{
	[TestMethod]
	[DataRow(nameof(EdgeXRestClient.GetMetaDataAsync), "/api/v2/public/meta/getMetaData")]
	[DataRow(nameof(EdgeXRestClient.GetServerTimeAsync), "/api/v2/public/meta/getServerTime")]
	[DataRow(nameof(EdgeXRestClient.GetTickerAsync), "/api/v2/public/quote/getTicker")]
	[DataRow(nameof(EdgeXRestClient.GetDepthAsync), "/api/v2/public/quote/getDepth")]
	[DataRow(nameof(EdgeXRestClient.GetKlineAsync), "/api/v2/public/quote/getKline")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task PublicRequestsUseCurrentApiRoutes(string operation, string expectedPath)
	{
		await using var server = new HistoryHttpServer(
			() => """{"code":"SUCCESS","data":{"ready":true}}""", CancellationToken);
		using var client = new EdgeXRestClient(server.Address.ToString(), null, null, null, null);

		var response = await (operation switch
		{
			nameof(EdgeXRestClient.GetMetaDataAsync) => client.GetMetaDataAsync(CancellationToken),
			nameof(EdgeXRestClient.GetServerTimeAsync) => client.GetServerTimeAsync(CancellationToken),
			nameof(EdgeXRestClient.GetTickerAsync) => client.GetTickerAsync("30000001", CancellationToken),
			nameof(EdgeXRestClient.GetDepthAsync) => client.GetDepthAsync("30000001", 15, CancellationToken),
			_ => client.GetKlineAsync("30000001", "MINUTE_1", "LAST_PRICE", 10, null, null, CancellationToken),
		});

		AreEqual("SUCCESS", response["code"].ToString());
		AreEqual(expectedPath, server.RequestTarget.Split('?')[0]);
	}
}
