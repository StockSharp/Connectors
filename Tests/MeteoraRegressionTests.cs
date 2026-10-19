namespace StockSharp.Connectors.Tests;

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Meteora.Native;
using StockSharp.Meteora.Native.Model;

[TestClass]
public class MeteoraRegressionTests : BaseTestClass
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task TransactionRequestAcceptsVersionOne()
	{
		Task<string> body = null;
		using var http = new HttpClient(new HistoryHttpHandler(request =>
		{
			body = request.Content.ReadAsStringAsync(CancellationToken);
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent(
					"""{"jsonrpc":"2.0","id":1,"result":{"slot":123,"version":1,"blockTime":1760000000,"meta":{"err":null},"transaction":{"message":{"transactionConfig":{}}}}}"""),
			};
		}))
		{
			BaseAddress = new("https://rpc.example.test"),
		};
		using var client = new MeteoraRpcClient(http.BaseAddress.ToString(),
			MeteoraClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, http, "_httpClient");

		var transaction = await client.GetTransactionAsync(new string('1', 64), CancellationToken);
		var request = JObject.Parse(await body);

		AreEqual("getTransaction", request["method"].Value<string>());
		AreEqual(1, request["params"][1]["maxSupportedTransactionVersion"].Value<int>());
		AreEqual(123L, transaction.Slot);
	}
}
