namespace StockSharp.Connectors.Tests;

using System.IO;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.Osmosis.Native;
using StockSharp.Osmosis.Native.Model;

[TestClass]
public class OsmosisRegressionTests : BaseTestClass
{
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ExactOutputRoutesAreReturnedInExecutionOrder(bool isMultiHop)
	{
		var pools = isMultiHop
			? """[{"id":3,"token_in_denom":"uatom"},{"id":1301,"token_in_denom":"uusdc"}]"""
			: """[{"id":1301,"token_in_denom":"uusdc"}]""";
		using var client = CreateClient(ExactOutputResponse(pools));

		var quote = await client.GetExactOutputQuoteAsync("uusdc", "uosmo", 1_000_000, CancellationToken);

		AreEqual(OsmosisSwapKinds.ExactOutput, quote.Kind);
		AreEqual(new BigInteger(48_450), quote.InputAmount);
		AreEqual(new BigInteger(1_000_000), quote.OutputAmount);
		AreEqual(isMultiHop ? 2 : 1, quote.Pools.Length);
		AreEqual(1301UL, quote.Pools[0].Id);
		AreEqual("uusdc", quote.Pools[0].InputDenomination);
		if (isMultiHop)
		{
			AreEqual(3UL, quote.Pools[1].Id);
			AreEqual("uatom", quote.Pools[1].InputDenomination);
		}
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ExactInputPreservesForwardPoolOrder()
	{
		using var client = CreateClient("""
			{"amount_in":{"denom":"uusdc","amount":"48450"},"amount_out":"1000000",
			 "route":[{"pools":[{"id":1301,"token_out_denom":"uatom"},{"id":3,"token_out_denom":"uosmo"}],
			 "in_amount":"48450","out_amount":"1000000"}]}
			""");

		var quote = await client.GetExactInputQuoteAsync("uusdc", "uosmo", 48_450, CancellationToken);

		AreEqual(OsmosisSwapKinds.ExactInput, quote.Kind);
		AreEqual(2, quote.Pools.Length);
		AreEqual(1301UL, quote.Pools[0].Id);
		AreEqual(3UL, quote.Pools[1].Id);
		AreEqual("uosmo", quote.Pools[1].OutputDenomination);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ExactOutputRejectsAQuoteForAnotherInput()
	{
		using var client = CreateClient(ExactOutputResponse("""[{"id":1301,"token_in_denom":"uion"}]"""));

		await ThrowsAsync<InvalidDataException>(async () =>
			await client.GetExactOutputQuoteAsync("uusdc", "uosmo", 1_000_000, CancellationToken));
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ExactOutputRejectsAMismatchedOutputAmount()
	{
		using var client = CreateClient(ExactOutputResponse("""[{"id":1301,"token_in_denom":"uusdc"}]"""));

		await ThrowsAsync<InvalidDataException>(async () =>
			await client.GetExactOutputQuoteAsync("uusdc", "uosmo", 999_999, CancellationToken));
	}

	private static string ExactOutputResponse(string pools)
		=> JsonConvert.SerializeObject(new
		{
			amount_in = "48450",
			amount_out = new { denom = "uosmo", amount = "1000000" },
			route = new[] { new { pools = JArray.Parse(pools), in_amount = "48450", out_amount = "1000000" } },
		});

	private static OsmosisApiClient CreateClient(string response)
	{
		const string endpoint = "https://api.example.test/";
		var client = new OsmosisApiClient(endpoint, endpoint, endpoint, endpoint);
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(_ => new(HttpStatusCode.OK)
		{
			Content = new StringContent(response),
		})) { BaseAddress = new(endpoint) }, "_sqsClient");
		return client;
	}
}
