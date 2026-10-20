namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BigOne.Native;

[TestClass]
public class BigOneRegressionTests : BaseTestClass
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandleHistoryReadsBackwardsFromItsUpperBound()
	{
		var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var to = from.AddMinutes(2);
		using var client = new BigOneRestClient("https://spot.example.test", "https://contract.example.test", null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request =>
		{
			var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
			AreEqual(to, DateTime.Parse(query["time"]).ToUniversalTime());
			var body = query["direction"] == "DESC"
				? """{"code":0,"data":[{"time":"2026-01-01T00:02:00Z","open":"100","high":"110","low":"90","close":"105","volume":"1"},{"time":"2026-01-01T00:01:00Z","open":"100","high":"110","low":"90","close":"105","volume":"1"}]}"""
				: """{"code":0,"data":[]}""";
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
		})), "_http");

		var candles = await client.GetCandlesAsync("BTC-USDT", "min1", from, to, CancellationToken);

		AreEqual(2, candles.Length);
		AreEqual((long)(from.AddMinutes(1) - DateTime.UnixEpoch).TotalMilliseconds, candles[0].Timestamp);
		AreEqual((long)(to - DateTime.UnixEpoch).TotalMilliseconds, candles[1].Timestamp);
	}
}
