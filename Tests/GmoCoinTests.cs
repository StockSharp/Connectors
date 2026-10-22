namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.GmoCoin;
using StockSharp.GmoCoin.Native;
using StockSharp.GmoCoin.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Tests for the GmoCoin connector.
/// </summary>
[TestClass]
public class GmoCoinTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void RestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var candle = new GmoCoinCandle { OpenTime = (long)open.ToUnix(false), Open = 100, High = 110, Low = 90, Close = 105, Volume = 12.5m };

		var message = candle.ToCandleMessage(default, TimeSpan.FromMinutes(1), 42, open.AddMilliseconds(elapsedMilliseconds));

		AreEqual(expected, message.State);
		AreEqual(open, message.OpenTime);
		AreEqual(105m, message.ClosePrice);
		AreEqual(12.5m, message.TotalVolume);
	}

	[TestMethod]
	public void MonthlyRestCandleClosesAtTheNextTradingMonth()
	{
		var open = new DateTime(2024, 2, 29, 21, 0, 0, DateTimeKind.Utc);
		var close = new DateTime(2024, 3, 31, 21, 0, 0, DateTimeKind.Utc);
		var candle = new GmoCoinCandle { OpenTime = (long)open.ToUnix(false) };

		AreEqual(CandleStates.Active, candle.ToCandleMessage(default, TimeSpan.FromDays(30), 42, close.AddMilliseconds(-1)).State);
		AreEqual(CandleStates.Finished, candle.ToCandleMessage(default, TimeSpan.FromDays(30), 42, close).State);
		AreEqual(close, candle.ToCandleMessage(default, TimeSpan.FromDays(30), 42, close).CloseTime);
	}

	[TestMethod]
	[DataRow(3, 0, "20261020")]
	[DataRow(15, 30, "20261020")]
	[DataRow(20, 59, "20261020")]
	[DataRow(21, 0, "20261021")]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task IntradayHistoryUsesTheTradingDateStartingAtSixInJapan(int hour, int minute, string expectedDate)
	{
		var to = new DateTime(2026, 10, 20, hour, minute, 0, DateTimeKind.Utc);
		string actualDate = null;
		var candles = await DownloadCandlesAsync(to.AddMinutes(-1), to, 1, request =>
		{
			actualDate = HttpUtility.ParseQueryString(request.RequestUri.Query)["date"];
			return actualDate == expectedDate
				? CandleResponse(to)
				: new(HttpStatusCode.NotFound)
				{
					Content = new StringContent("""{"status":2,"messages":[{"message_code":"ERR-5207","message_string":"Not found"}]}"""),
				};
		});

		AreEqual(expectedDate, actualDate);
		AreEqual(1, candles.Length);
		AreEqual(to, DateTime.UnixEpoch.AddMilliseconds(candles[0].OpenTime));
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task IntradayHistoryReadsBothTradingDatesAcrossTheSixAmBoundary()
	{
		var from = new DateTime(2026, 10, 20, 20, 59, 0, DateTimeKind.Utc);
		var to = from.AddMinutes(1);
		var dates = new List<string>();
		var candles = await DownloadCandlesAsync(from, to, 2, request =>
		{
			var date = HttpUtility.ParseQueryString(request.RequestUri.Query)["date"];
			dates.Add(date);
			return CandleResponse(date == "20261021" ? to : from);
		});

		AreEqual(2, candles.Length);
		AreEqual(2, dates.Count);
		AreEqual("20261021", dates[0]);
		AreEqual("20261020", dates[1]);
		AreEqual(from, DateTime.UnixEpoch.AddMilliseconds(candles[0].OpenTime));
		AreEqual(to, DateTime.UnixEpoch.AddMilliseconds(candles[1].OpenTime));
	}

	private async ValueTask<GmoCoinCandle[]> DownloadCandlesAsync(DateTime from, DateTime to, int count,
		Func<HttpRequestMessage, HttpResponseMessage> respond)
	{
		using var client = new GmoCoinRestClient("https://api.example.test", null, null);
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(respond)), "_http");
		using var adapter = new GmoCoinMessageAdapter(new IncrementalIdGenerator());
		var adapterType = typeof(GmoCoinMessageAdapter);
		adapterType.GetField("_restClient", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(adapter, client);
		var marketType = adapterType.GetNestedType("MarketDefinition", BindingFlags.NonPublic);
		var market = Activator.CreateInstance(marketType, true);
		marketType.GetProperty("Symbol").SetValue(market, "BTC");
		var loading = (ValueTask<GmoCoinCandle[]>)adapterType.GetMethod("DownloadCandlesAsync", BindingFlags.Instance | BindingFlags.NonPublic)
			.Invoke(adapter, [market, "1min", TimeSpan.FromMinutes(1), from, to, count, CancellationToken]);
		return await loading;
	}

	private static HttpResponseMessage CandleResponse(DateTime time)
		=> Response(JsonConvert.SerializeObject(new
		{
			status = 0,
			data = new[]
			{
				new { openTime = (long)(time - DateTime.UnixEpoch).TotalMilliseconds, open = "100", high = "110", low = "90", close = "105", volume = "1" },
			},
		}));

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

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GmoCoinMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC",
		BoardCode = BoardCodes.GmoCoin,
	};
}
