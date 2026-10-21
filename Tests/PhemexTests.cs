namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Messages;
using StockSharp.Phemex;
using StockSharp.Phemex.Native;

/// <summary>
/// Tests for the Phemex connector.
/// </summary>
[TestClass]
public class PhemexTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandleLimitSelectsTheLatestRowsRegardlessOfWireOrder(bool isDescending)
	{
		const long start = 1767225600;
		var rows = Enumerable.Range(0, 500).Select(i => new object[] { start + i * 60, 60, "100", "100", "110", "90", "105", "1", "105", "BTCUSDT" });
		if (isDescending)
			rows = rows.Reverse();
		using var client = new PhemexRestClient("https://api.example.test", null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(request.RequestUri.AbsolutePath == "/public/products"
				? """{"code":0,"msg":"OK","data":{"perpProductsV2":[{"symbol":"BTCUSDT","type":"PerpetualV2","perpProductSubType":"Normal","baseCurrency":"BTC","quoteCurrency":"USDT","settleCurrency":"USDT","status":"Listed"}]}}"""
				: JsonConvert.SerializeObject(new { code = 0, msg = "OK", data = new { rows } })),
		})), "_http");

		var candles = await client.GetKlinesAsync("BTCUSDT", TimeSpan.FromMinutes(1), DateTime.UnixEpoch.AddSeconds(start + 499 * 60), 101, CancellationToken);

		AreEqual(101, candles.Length);
		AreEqual((start + 399 * 60) * 1000, candles[0].Time);
		AreEqual((start + 499 * 60) * 1000, candles[^1].Time);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PhemexMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [PhemexSections.Futures],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.PhemexFutures,
	};
}
