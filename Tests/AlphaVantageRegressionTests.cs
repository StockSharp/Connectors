namespace StockSharp.Connectors.Tests;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.AlphaVantage;
using StockSharp.AlphaVantage.Native;
using StockSharp.Messages;

[TestClass]
[CLSCompliant(false)]
public class AlphaVantageRegressionTests : HistoryTestBase
{
	[TestMethod]
	[DataRow(SecurityTypes.Stock, 10080, "2025-07-18", "2025-07-14")]
	[DataRow(SecurityTypes.Stock, 43200, "2025-07-31", "2025-07-01")]
	[DataRow(SecurityTypes.CryptoCurrency, 10080, "2025-07-20", "2025-07-14")]
	[DataRow(SecurityTypes.CryptoCurrency, 43200, "2025-07-31", "2025-07-01")]
	[DataRow(SecurityTypes.Stock, 1440, "2025-07-18", "2025-07-18")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandleOpenTimeUsesTheBeginningOfItsPeriod(SecurityTypes securityType, int minutes, string responseDate, string expectedDate)
	{
		await using var server = new HistoryHttpServer(() =>
			"{\"Time Series\":{\"" + responseDate + "\":{\"1. open\":\"1\",\"2. high\":\"3\",\"3. low\":\"0.5\",\"4. close\":\"2\",\"5. volume\":\"10\"}}}", CancellationToken);
		var adapter = CreateAdapter<AlphaVantageMessageAdapter>(a =>
		{
			a.QueryEndpoint = server.Address.ToString();
			a.Token = "test-key".Secure();
			a.IterationInterval = TimeSpan.Zero;
		});
		var timeFrame = TimeSpan.FromMinutes(minutes);
		var expected = expectedDate.ToDateTime("yyyy-MM-dd").UtcKind();
		var securityId = new SecurityId
		{
			SecurityCode = securityType == SecurityTypes.CryptoCurrency ? "BTC/USD" : "AAPL",
			BoardCode = BoardCodes.AlphaVantage,
		};
		var count = 0;
		await foreach (var candle in DownloadCandles(adapter, securityId, timeFrame, expected,
			expected + timeFrame - TimeSpan.FromTicks(1), count: 1, secType: securityType).WithCancellation(CancellationToken))
		{
			AreEqual(expected, candle.OpenTime);
			AreEqual(2m, candle.ClosePrice);
			count++;
		}

		AreEqual(1, count);
	}

	[TestMethod]
	[DataRow("Information")]
	[DataRow("Note")]
	[DataRow("Error Message")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task LookupReportsApiErrors(string field)
	{
		await using var server = new HistoryHttpServer(() => $"{{\"{field}\":\"Request limit reached.\"}}", CancellationToken);
		using var client = new AlphaClient(server.Address.ToString(), TimeSpan.FromSeconds(1));
		var error = await ThrowsAsync<InvalidOperationException>(async () =>
			await client.Lookup("AAPL", "test-key".Secure(), CancellationToken));
		IsTrue(error.Message.Contains("Request limit reached."));
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task RequestsFromDifferentClientsAreSpaced()
	{
		var timer = Stopwatch.StartNew();
		TimeSpan? lastRequest = null;
		var interval = TimeSpan.MaxValue;
		await using var server = new HistoryHttpServer(() =>
		{
			if (lastRequest is { } last)
				interval = timer.Elapsed - last;
			lastRequest = timer.Elapsed;
			return "{\"bestMatches\":[]}";
		}, CancellationToken);
		using var first = new AlphaClient(server.Address.ToString(), TimeSpan.FromSeconds(1));
		using var second = new AlphaClient(server.Address.ToString(), TimeSpan.FromSeconds(1));
		await first.Lookup("AAPL", "test-key".Secure(), CancellationToken);
		await second.Lookup("AAPL", "test-key".Secure(), CancellationToken);
		IsTrue(interval >= TimeSpan.FromSeconds(1), $"Requests were only {interval} apart.");
	}
}
