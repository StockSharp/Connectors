namespace StockSharp.Connectors.Tests;

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.GmoCoin.Native;
using StockSharp.GmoCoin.Native.Model;

[TestClass]
public class GmoCoinRegressionTests : BaseTestClass
{
	private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK)
	{
		Content = new StringContent(body),
	};

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task PublicRequestsAreSpacedAcrossClients()
	{
		var timer = Stopwatch.StartNew();
		TimeSpan? previous = null;
		var interval = TimeSpan.Zero;
		HttpResponseMessage Respond(HttpRequestMessage request)
		{
			if (previous is { } last)
				interval = timer.Elapsed - last;
			previous = timer.Elapsed;
			return Response("""{"status":0,"data":{"status":"OPEN"}}""");
		}

		using var first = new GmoCoinRestClient("https://api.example.test", null, null);
		using var second = new GmoCoinRestClient("https://api.example.test", null, null);
		HistoryHttpHandler.ReplaceClient(first, new(new HistoryHttpHandler(Respond)), "_http");
		HistoryHttpHandler.ReplaceClient(second, new(new HistoryHttpHandler(Respond)), "_http");

		await first.GetStatusAsync(CancellationToken);
		await second.GetStatusAsync(CancellationToken);

		IsTrue(interval >= TimeSpan.FromMilliseconds(950), $"Public requests were only {interval} apart.");
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task PublicReadRetriesAnApiRateLimitResponse()
	{
		var calls = 0;
		using var client = new GmoCoinRestClient("https://api.example.test", null, null);
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(_ => Response(++calls == 1
			? """{"status":4,"messages":[{"message_code":"ERR-5003","message_string":"Requests are too many."}]}"""
			: """{"status":0,"data":{"status":"OPEN"}}"""))), "_http");

		var status = await client.GetStatusAsync(CancellationToken);

		AreEqual(2, calls);
		AreEqual(GmoCoinServiceStatuses.Open, status.Status);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task PublicReadStopsAfterItsRetryLimit()
	{
		var calls = 0;
		using var client = new GmoCoinRestClient("https://api.example.test", null, null);
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(_ =>
		{
			calls++;
			return Response("""{"status":4,"messages":[{"message_code":"ERR-5003","message_string":"Requests are too many."}]}""");
		})), "_http");

		var error = await ThrowsAsync<GmoCoinApiException>(async () => await client.GetStatusAsync(CancellationToken));

		AreEqual("ERR-5003", error.Code);
		AreEqual(4, calls);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task PrivateWriteIsNotRepeatedAfterAnApiRateLimitResponse()
	{
		var calls = 0;
		using var client = new GmoCoinRestClient("https://api.example.test", "test-key".Secure(), "test-secret".Secure());
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(_ =>
		{
			calls++;
			return Response("""{"status":4,"messages":[{"message_code":"ERR-5003","message_string":"Requests are too many."}]}""");
		})), "_http");

		await ThrowsAsync<GmoCoinApiException>(async () => await client.CreateWebSocketTokenAsync(CancellationToken));

		AreEqual(1, calls);
	}
}
