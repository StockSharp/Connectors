namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Lfj.Native;

[TestClass]
public class LfjRegressionTests : BaseTestClass
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task RpcReadHonorsRetryAfter()
	{
		var timer = Stopwatch.StartNew();
		var requests = new List<TimeSpan>();
		using var client = new LfjRpcClient("https://rpc.example.test", null, null);
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(_ =>
		{
			requests.Add(timer.Elapsed);
			if (requests.Count == 1)
			{
				var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
				{
					Content = new StringContent("rate limited"),
				};
				response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
				return response;
			}
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":"0x64"}"""),
			};
		})), "_http");

		var block = await client.GetLatestBlockNumberAsync(CancellationToken);

		AreEqual(new BigInteger(100), block);
		AreEqual(2, requests.Count);
		IsTrue(requests[1] - requests[0] >= TimeSpan.FromMilliseconds(950),
			$"RPC retried before Retry-After elapsed: {requests[1] - requests[0]}.");
	}
}
