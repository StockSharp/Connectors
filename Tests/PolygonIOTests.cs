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

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.BusinessEntities;
using StockSharp.Messages;
using StockSharp.PolygonIO;
using StockSharp.PolygonIO.Native;

/// <summary>
/// Tests for the PolygonIO connector.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PolygonIOTests : HistoryTestBase
{
	[TestMethod]
	public void DefaultRequestIntervalRespectsBasicPlan()
	{
		using var adapter = new PolygonIOMessageAdapter(new IncrementalIdGenerator());
		IsTrue(adapter.IterationInterval >= TimeSpan.FromMinutes(1) / 5);
	}

	[TestMethod]
	[DataRow("quote", 150.0)]
	[DataRow("parallel", 150.0)]
	[DataRow("reconnect", 150.0)]
	[DataRow("pagination", 150.0)]
	[DataRow("quote", 0.5)]
	[DataRow("parallel", 0.5)]
	[DataRow("reconnect", 0.5)]
	[DataRow("pagination", 0.5)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task EveryRestRequestHonorsTheConfiguredInterval(string scenario, double milliseconds)
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
		var delay = TimeSpan.FromMilliseconds(milliseconds);
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

	// The token comes from the environment. A machine without the variable reports the tests
	// inconclusive rather than failing them on a null token.
	private static IMessageAdapter CreatePolygon()
	{
		var token = Env("POLYGON_TOKEN");

		if (token.IsEmpty())
			Inconclusive("POLYGON_TOKEN is not set.");

		return CreateAdapter<PolygonIOMessageAdapter>(a =>
		{
			a.Token = token.Secure();
			a.ConnectionType = PolygonIOConnectionTypes.History;
		});
	}

	private static void SkipUnlessPaid()
	{
		SkipUnlessLive();

		if (!Env("POLYGON_PAID_TESTS").EqualsIgnoreCase("1"))
			Inconclusive("Trade and quote history requires a paid Massive stocks plan. Set POLYGON_PAID_TESTS=1 when it is available.");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task PolygonLookup()
	{
		SkipUnlessLive();

		var criteria = LookupAll;
		criteria.Count = 2000;
		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreatePolygon(), criteria).WithCancellation(CancellationToken))
			counter++;
		AreEqual(2000, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task PolygonLookupMulti()
	{
		SkipUnlessLive();

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreatePolygon(), new() { SecurityId = new() { SecurityCode = "AAPL" }, SecurityTypes = [SecurityTypes.Stock, SecurityTypes.Currency] }).WithCancellation(CancellationToken))
			counter++;
		IsLess(counter, 10);
		IsGreater(counter, 0);

		counter = 0;
		await foreach (var _ in DownloadSecurities(CreatePolygon(), new() { SecurityId = new() { SecurityCode = "A" }, SecurityTypes = [SecurityTypes.Stock, SecurityTypes.Currency], Count = 200 }).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 100);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[TestCategory("Paid")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task PolygonTicksAapl()
	{
		SkipUnlessPaid();

		var secId = new Security
		{
			Id = $"AAPL@{BoardCodes.StockSharp}",
		}.ToSecurityId();

		async Task download(DateTime from, int count)
		{
			var counter = 0;
			var lastTime = DateTime.MinValue;
			await foreach (var msg in DownloadTicks(CreatePolygon(), secId, from.UtcKind(), DateTime.UtcNow, count).WithCancellation(CancellationToken))
			{
				Validate(msg, secId, from.UtcKind(), DateTime.UtcNow, ref lastTime);
				counter++;
			}
			AreEqual(count, counter);
		}

		await download(new DateTime(2015, 1, 1), 60000);
		await download(new DateTime(1975, 1, 1), 1);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[TestCategory("Paid")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task PolygonQuotesAapl()
	{
		SkipUnlessPaid();

		var secId = new Security
		{
			Id = $"AAPL@{BoardCodes.StockSharp}",
		}.ToSecurityId();

		async Task download(DateTime from, int count)
		{
			var counter = 0;
			var lastTime = DateTime.MinValue;
			await foreach (var msg in DownloadLevel1(CreatePolygon(), secId, from.UtcKind(), DateTime.UtcNow, count).WithCancellation(CancellationToken))
			{
				Validate(msg, secId, from.UtcKind(), DateTime.UtcNow, ref lastTime);
				counter++;
			}
			AreEqual(count, counter);
		}

		await download(new DateTime(2015, 1, 1), 60000);
		await download(new DateTime(1975, 1, 1), 1);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[DataRow(1)]
	[DataRow(30)]
	[DataRow(60)]
	[DataRow(1440)]
	[DataRow(10080)]
	[DataRow(43200)]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task PolygonCandlesAapl(int minutes)
	{
		SkipUnlessLive();

		var secId = $"AAPL@{BoardCodes.StockSharp}".ToSecurityId();
		var from = new DateTime(PrevYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var to = from.AddMonths(3).AddTicks(-1);
		var timeFrame = TimeSpan.FromMinutes(minutes);
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadCandles(CreatePolygon(), secId, timeFrame, from, to, count: 2).WithCancellation(CancellationToken))
		{
			AreEqual(timeFrame.TimeFrame(), msg.DataType);
			AreEqual(CandleStates.Finished, msg.State);
			IsGreater(msg.OpenTime, lastTime);
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(2, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task PolygonDiv()
	{
		SkipUnlessLive();

		var secId = new Security { Id = "AAPL@STSH" }.ToSecurityId();
		var from = new DateTime(PrevYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var to = from.AddYears(1).AddTicks(-1);

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadLevel1(CreatePolygon(), secId, from, to, fields: [Level1Fields.Dividend]).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			IsGreater((decimal)msg.Changes[Level1Fields.Dividend], 0m);
			counter++;
		}
		AreEqual(4, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task PolygonNews()
	{
		SkipUnlessLive();

		var mdMsg = new MarketDataMessage
		{
			IsSubscribe = true,
			DataType2 = DataType.News,
			From = DateTime.UtcNow.Date.AddMonths(-1),
			To = DateTime.UtcNow,
			Count = 100,
		};

		var counter = 0;
		await foreach (var _ in Download<NewsMessage>(CreatePolygon(), mdMsg).WithCancellation(CancellationToken))
			counter++;
		AreEqual(100, counter);
	}
}
