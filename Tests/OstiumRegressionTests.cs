namespace StockSharp.Connectors.Tests;

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Ostium;
using StockSharp.Ostium.Native;
using StockSharp.Ostium.Native.Model;

[TestClass]
public class OstiumRegressionTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUseThePublishedBuilderGateway()
	{
		using var adapter = new OstiumMessageAdapter(new IncrementalIdGenerator());

		AreEqual("https://builder.prod.bedrock.ostium.io", adapter.BuilderEndpoint);
		AreEqual("wss://builder.prod.bedrock.ostium.io/v1/prices/stream", adapter.PriceStreamEndpoint);
		AreEqual("https://builder.prod.bedrock.ostium.io/v1/subgraph/gn", OstiumEnvironments.Mainnet.GetNetwork().SubgraphEndpoint);
		AreEqual("https://builder.prod.bedrock.ostium.io/v1/subgraph/gn?chainId=421614", OstiumEnvironments.Testnet.GetNetwork().SubgraphEndpoint);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task PairLookupUsesQueriesAcceptedByTheBuilderGateway()
	{
		using var client = new OstiumApiClient("https://builder.example.test", "https://builder.example.test/v1/subgraph/gn");
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(request =>
		{
			var body = request.Content.ReadAsStringAsync(CancellationToken).GetAwaiter().GetResult();
			var query = (string)JObject.Parse(body)["query"];
			IsFalse(query.Contains("subgraphError"), "The builder gateway treats subgraphError as an unknown filter field.");
			IsFalse(query.Contains("spreadP"), "The current Pair schema does not publish spreadP.");
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent("""{"data":{"pairs":[{"id":"0","from":"BTC","to":"USD"}]}}"""),
			};
		})), "_http");

		var pairs = await client.GetPairsAsync(CancellationToken);

		AreEqual(1, pairs.Length);
		AreEqual("0", pairs[0].Id);
		AreEqual("BTC", pairs[0].From);
	}
}
