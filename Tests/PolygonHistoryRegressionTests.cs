namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Messages;
using StockSharp.PolygonIO;
using StockSharp.PolygonIO.Native;

[TestClass]
public class PolygonHistoryRegressionTests : BaseTestClass
{
	[TestMethod]
	public void DefaultRequestIntervalRespectsBasicPlan()
	{
		using var adapter = new PolygonIOMessageAdapter(new IncrementalIdGenerator());
		IsTrue(adapter.IterationInterval >= TimeSpan.FromMinutes(1) / 5);
	}

	[TestMethod]
	[DataRow("quote")]
	[DataRow("parallel")]
	[DataRow("reconnect")]
	[DataRow("pagination")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task EveryRestRequestHonorsTheConfiguredInterval(string scenario)
	{
		var timer = Stopwatch.StartNew();
		var requestTimes = new ConcurrentQueue<TimeSpan>();
		using var http = new HttpClient(new HistoryHttpHandler(_ =>
		{
			requestTimes.Enqueue(timer.Elapsed);
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent(scenario == "pagination" && requestTimes.Count == 1
					? """{"status":"OK","results":[],"next_url":"https://api.massive.com/v3/reference/tickers?cursor=next"}"""
					: """{"status":"OK","results":[]}"""),
			};
		}));
		var client = new RestClient(http, "massive.com");
		var delay = TimeSpan.FromMilliseconds(150);
		Task lookup(RestClient source) => source.GetTickers(delay, null, null, true, 1, "test-key", CancellationToken).ToArrayAsync(CancellationToken).AsTask();

		switch (scenario)
		{
			case "parallel":
				await lookup(client);
				await Task.WhenAll(lookup(client), lookup(client));
				break;
			case "quote":
				await client.GetQuotes(TimeSpan.Zero, "AAPL", DateTime.UtcNow, "asc", 1, "test-key", CancellationToken).ToArrayAsync(CancellationToken);
				requestTimes.Clear();
				await lookup(client);
				await client.GetQuotes(delay, "AAPL", DateTime.UtcNow, "asc", 1, "test-key", CancellationToken).ToArrayAsync(CancellationToken);
				break;
			case "reconnect":
				await lookup(client);
				await lookup(new RestClient(http, "massive.com"));
				break;
			case "pagination":
				await lookup(client);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(scenario));
		}

		var times = requestTimes.ToArray();
		AreEqual(scenario == "parallel" ? 3 : 2, times.Length);
		for (var i = 1; i < times.Length; i++)
			IsTrue(times[i] - times[i - 1] >= delay, $"Requests were only {times[i] - times[i - 1]} apart.");
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task LookupReadsNamedFieldsAndFollowsPagination()
	{
		var requests = 0;
		using var http = new HttpClient(new HistoryHttpHandler(_ => new(HttpStatusCode.OK)
		{
			Content = new StringContent(++requests == 1
				? "{\"status\":\"OK\",\"results\":[{\"ticker\":\"AAPL\",\"primary_exchange\":\"XNAS\"}],\"next_url\":\"https://api.massive.com/v3/reference/tickers?cursor=next\"}"
				: "{\"status\":\"OK\",\"results\":[{\"ticker\":\"MSFT\",\"primary_exchange\":\"XNAS\"}]}"),
		}));
		var client = new RestClient(http, "massive.com");
		var tickers = await client.GetTickers(TimeSpan.Zero, null, null, true, 1, "test-key", CancellationToken).ToArrayAsync(CancellationToken);

		AreEqual(2, requests);
		AreEqual(2, tickers.Length);
		AreEqual("AAPL", tickers[0].Code);
		AreEqual("MSFT", tickers[1].Code);
		AreEqual("XNAS", tickers[0].PrimaryExchange);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandlesUseExchangeFieldNamesAndMillisecondTimestamps()
	{
		using var http = new HttpClient(new HistoryHttpHandler(_ => new(HttpStatusCode.OK)
		{
			Content = new StringContent("{\"status\":\"OK\",\"results\":[{\"t\":1704067200000,\"o\":100,\"h\":120,\"l\":90,\"c\":110,\"v\":25}]}"),
		}));
		var client = new RestClient(http, "massive.com");
		var from = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var bars = await client.GetBars(TimeSpan.Zero, "AAPL", "minute", 1, from, from.AddMinutes(1), true, "asc", 1, "test-key", CancellationToken)
			.ToArrayAsync(CancellationToken);

		AreEqual(1, bars.Length);
		AreEqual(from, bars[0].Time);
		AreEqual(100d, bars[0].Open);
		AreEqual(110d, bars[0].Close);
		AreEqual(25d, bars[0].Volume);
	}

	[TestMethod]
	[DataRow(30)]
	[DataRow(60)]
	[DataRow(10080)]
	[DataRow(43200)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandleCountDoesNotLimitTheBaseBarsNeededForAggregation(int minutes)
	{
		var timeFrame = TimeSpan.FromMinutes(minutes);
		var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var baseBars = timeFrame >= TimeSpan.FromDays(1) ? timeFrame.TotalDays : timeFrame.TotalMinutes;
		using var http = new HttpClient(new HistoryHttpHandler(request =>
		{
			var limit = HttpUtility.ParseQueryString(request.RequestUri.Query)["limit"].To<long>();
			var bars = limit >= 2 * baseBars
				? Enumerable.Range(0, 3).Select(i => new { t = (long)(from + timeFrame * i).ToUnix(false), o = 100, h = 120, l = 90, c = 110, v = 25 })
				: [];
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent(JsonConvert.SerializeObject(new { status = "OK", results = bars })),
			};
		}));
		using var adapter = CreateAdapter(http);
		var candles = new List<TimeFrameCandleMessage>();
		adapter.NewOutMessageAsync += (message, _) =>
		{
			if (message is TimeFrameCandleMessage candle)
				candles.Add(candle);
			return default;
		};
		await adapter.SendInMessageAsync(new MarketDataMessage
		{
			TransactionId = 1,
			SecurityId = new() { SecurityCode = "AAPL", BoardCode = BoardCodes.StockSharp },
			DataType2 = timeFrame.TimeFrame(),
			IsSubscribe = true,
			From = from,
			To = from.AddMonths(3),
			Count = 2,
		}, CancellationToken);

		AreEqual(2, candles.Count);
		AreEqual(from, candles[0].OpenTime);
		AreEqual(from + timeFrame, candles[1].OpenTime);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task NewsSubscriptionsUseOnlyRealTickerFilters(bool allNews)
	{
		var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var securityId = allNews ? SecurityId.News : new SecurityId { SecurityCode = "AAPL", BoardCode = BoardCodes.StockSharp };
		string requestedTicker = null;
		using var http = new HttpClient(new HistoryHttpHandler(request =>
		{
			requestedTicker = HttpUtility.ParseQueryString(request.RequestUri.Query)["ticker"];
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent(JsonConvert.SerializeObject(new
				{
					status = "OK",
					results = new[] { new { id = "news-1", title = "Market news", published_utc = from } },
				})),
			};
		}));
		using var adapter = CreateAdapter(http);
		var news = new List<NewsMessage>();
		adapter.NewOutMessageAsync += (message, _) =>
		{
			if (message is NewsMessage item)
				news.Add(item);
			return default;
		};
		await adapter.SendInMessageAsync(new MarketDataMessage
		{
			TransactionId = 1,
			SecurityId = securityId,
			DataType2 = DataType.News,
			IsSubscribe = true,
			From = from,
			To = from.AddDays(1),
			Count = 1,
		}, CancellationToken);

		AreEqual(allNews ? null : "AAPL", requestedTicker);
		AreEqual(1, news.Count);
		AreEqual("news-1", news[0].Id);
		AreEqual(from, news[0].ServerTime);
	}

	private static PolygonIOMessageAdapter CreateAdapter(HttpClient http)
	{
		var adapter = new PolygonIOMessageAdapter(new IncrementalIdGenerator())
		{
			Token = "test-key".Secure(),
			IterationInterval = TimeSpan.Zero,
			ConnectionType = PolygonIOConnectionTypes.History,
		};
		typeof(PolygonIOMessageAdapter).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)
			.SetValue(adapter, new RestClient(http, "massive.com"));
		return adapter;
	}

	[TestMethod]
	[DataRow(HttpStatusCode.Forbidden)]
	[DataRow(HttpStatusCode.OK)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ApiErrorsExplainMissingEntitlementWithoutLeakingKey(HttpStatusCode statusCode)
	{
		using var http = new HttpClient(new HistoryHttpHandler(_ => new(statusCode)
		{
			Content = new StringContent("{\"status\":\"NOT_AUTHORIZED\",\"message\":\"Your plan does not include this data.\"}"),
		}));
		var client = new RestClient(http, "massive.com");
		var error = await ThrowsAsync<InvalidOperationException>(async () =>
		{
			await foreach (var _ in client.GetTickers(TimeSpan.Zero, null, "AAPL", true, 1, "private-api-key", CancellationToken))
			{
			}
		});
		IsTrue(error.ToString().Contains("Your plan does not include this data."));
		IsFalse(error.ToString().Contains("private-api-key"));
	}
}
