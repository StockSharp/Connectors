namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.OkexHistory;

/// <summary>
/// Tests for the OkexHistory connector.
/// </summary>
[TestClass]
[CLSCompliant(false)]
public class OkexHistoryTests : LiveMarketDataTestBase
{
	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task OkxHistLookupAll()
	{
		SkipUnlessLive();

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAdapter<OkexHistoryMessageAdapter>(), LookupAll).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 300);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task OkxHistLookupLimit()
	{
		SkipUnlessLive();

		const int count = 300;

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAdapter<OkexHistoryMessageAdapter>(), new() { Count = count }).WithCancellation(CancellationToken))
			counter++;
		AreEqual(count, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task OkxHistTicksSpot()
	{
		SkipUnlessLive();

		var secId = $"BTC-USD@{BoardCodes.Okex}".ToSecurityId();
		// Use a recent date that should have data available
		var from = new DateTime(2025, 12, 01).UtcKind();
		var to = from.EndOfDay();

		const int maxCount = 1000;
		var allMessages = new List<Message>();
		var adapter = CreateAdapter<OkexHistoryMessageAdapter>();

		var counter = 0;

		await foreach (var msg in adapter.ConnectAndDownloadAsync<Message>(new MarketDataMessage
		{
			SecurityId = secId,
			IsSubscribe = true,
			DataType2 = DataType.Ticks,
			From = from,
			To = to,
			Count = maxCount,
		}).WithCancellation(CancellationToken))
		{
			allMessages.Add(msg);
			if (msg is ExecutionMessage exec && exec.DataTypeEx == DataType.Ticks)
				counter++;
		}

		var msgTypes = allMessages.GroupBy(m => m.GetType().Name).Select(g => $"{g.Key}:{g.Count()}");
		IsGreater(counter, 0, $"Expected at least 1 tick but got 0. All messages: {msgTypes.JoinCommaSpace()}");
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task OkxHistTicksSwap()
	{
		SkipUnlessLive();

		var secId = $"BTC-USD@{BoardCodes.Okex}".ToSecurityId();
		// Use a recent date that should have data available
		var from = new DateTime(2025, 12, 01).UtcKind();
		var to = from.EndOfDay();

		const int maxCount = 1000;
		var allMessages = new List<Message>();
		var adapter = CreateAdapter<OkexHistoryMessageAdapter>();

		var counter = 0;

		await foreach (var msg in adapter.ConnectAndDownloadAsync<Message>(new MarketDataMessage
		{
			SecurityId = secId,
			IsSubscribe = true,
			DataType2 = DataType.Ticks,
			SecurityType = SecurityTypes.Swap,
			From = from,
			To = to,
			Count = maxCount,
		}).WithCancellation(CancellationToken))
		{
			allMessages.Add(msg);
			if (msg is ExecutionMessage exec && exec.DataTypeEx == DataType.Ticks)
				counter++;
		}

		var msgTypes = allMessages.GroupBy(m => m.GetType().Name).Select(g => $"{g.Key}:{g.Count()}");
		IsGreater(counter, 0, $"Expected at least 1 tick but got 0. All messages: {msgTypes.JoinCommaSpace()}");
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task OkxHistTicksFutures()
	{
		SkipUnlessLive();

		var secId = $"BTC-USD@{BoardCodes.Okex}".ToSecurityId();
		// Use a recent date that should have data available
		var from = new DateTime(2025, 12, 01).UtcKind();
		var to = from.EndOfDay();
		// Use an expiry date that should have been available (quarterly futures)
		//var expiryDate = new DateTime(2025, 12, 27).UtcKind();

		const int maxCount = 1000;
		var allMessages = new List<Message>();
		var adapter = CreateAdapter<OkexHistoryMessageAdapter>();

		var counter = 0;

		await foreach (var msg in adapter.ConnectAndDownloadAsync<Message>(new MarketDataMessage
		{
			SecurityId = secId,
			IsSubscribe = true,
			DataType2 = DataType.Ticks,
			SecurityType = SecurityTypes.Future,
			//ExpiryDate = expiryDate,
			From = from,
			To = to,
			Count = maxCount,
		}).WithCancellation(CancellationToken))
		{
			allMessages.Add(msg);
			if (msg is ExecutionMessage exec && exec.DataTypeEx == DataType.Ticks)
				counter++;
		}

		var msgTypes = allMessages.GroupBy(m => m.GetType().Name).Select(g => $"{g.Key}:{g.Count()}");
		IsGreater(counter, 0, $"Expected at least 1 tick but got 0. All messages: {msgTypes.JoinCommaSpace()}");
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task OkxHistCandlesSpot()
	{
		SkipUnlessLive();

		var secId = $"BTC-USDT@{BoardCodes.Okex}".ToSecurityId();
		var from = new DateTime(2025, 12, 01).UtcKind();
		var to = from.AddDays(1);

		const int maxCount = 100;
		var allMessages = new List<Message>();
		var adapter = CreateAdapter<OkexHistoryMessageAdapter>();

		var counter = 0;

		await foreach (var msg in adapter.ConnectAndDownloadAsync<Message>(new MarketDataMessage
		{
			SecurityId = secId,
			IsSubscribe = true,
			DataType2 = TimeSpan.FromMinutes(1).TimeFrame(),
			From = from,
			To = to,
			Count = maxCount,
		}).WithCancellation(CancellationToken))
		{
			allMessages.Add(msg);
			if (msg is TimeFrameCandleMessage)
				counter++;
		}

		var msgTypes = allMessages.GroupBy(m => m.GetType().Name).Select(g => $"{g.Key}:{g.Count()}");
		IsGreater(counter, 0, $"Expected at least 1 candle but got 0. All messages: {msgTypes.JoinCommaSpace()}");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task OkxHistCandlesSwap()
	{
		SkipUnlessLive();

		var secId = $"BTC-USDT@{BoardCodes.Okex}".ToSecurityId();
		var from = new DateTime(2025, 12, 01).UtcKind();
		var to = from.AddDays(1);

		const int maxCount = 100;
		var allMessages = new List<Message>();
		var adapter = CreateAdapter<OkexHistoryMessageAdapter>();

		var counter = 0;

		await foreach (var msg in adapter.ConnectAndDownloadAsync<Message>(new MarketDataMessage
		{
			SecurityId = secId,
			IsSubscribe = true,
			DataType2 = TimeSpan.FromMinutes(1).TimeFrame(),
			SecurityType = SecurityTypes.Swap,
			From = from,
			To = to,
			Count = maxCount,
		}).WithCancellation(CancellationToken))
		{
			allMessages.Add(msg);
			if (msg is TimeFrameCandleMessage)
				counter++;
		}

		var msgTypes = allMessages.GroupBy(m => m.GetType().Name).Select(g => $"{g.Key}:{g.Count()}");
		IsGreater(counter, 0, $"Expected at least 1 candle but got 0. All messages: {msgTypes.JoinCommaSpace()}");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task OkxHistCandlesFutures()
	{
		SkipUnlessLive();

		var secId = $"BTC-USDT@{BoardCodes.Okex}".ToSecurityId();
		var from = new DateTime(2025, 12, 01).UtcKind();
		var to = from.AddDays(1);
		//var expiryDate = new DateTime(2025, 12, 27).UtcKind();

		const int maxCount = 100;
		var allMessages = new List<Message>();
		var adapter = CreateAdapter<OkexHistoryMessageAdapter>();

		var counter = 0;

		await foreach (var msg in adapter.ConnectAndDownloadAsync<Message>(new MarketDataMessage
		{
			SecurityId = secId,
			IsSubscribe = true,
			DataType2 = TimeSpan.FromMinutes(1).TimeFrame(),
			SecurityType = SecurityTypes.Future,
			//ExpiryDate = expiryDate,
			From = from,
			To = to,
			Count = maxCount,
		}).WithCancellation(CancellationToken))
		{
			allMessages.Add(msg);
			if (msg is TimeFrameCandleMessage)
				counter++;
		}

		var msgTypes = allMessages.GroupBy(m => m.GetType().Name).Select(g => $"{g.Key}:{g.Count()}");
		IsGreater(counter, 0, $"Expected at least 1 candle but got 0. All messages: {msgTypes.JoinCommaSpace()}");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task OkxHistOrderBookSpot()
	{
		SkipUnlessLive();

		var secId = $"BTC-USDT@{BoardCodes.Okex}".ToSecurityId();
		var from = new DateTime(2026, 01, 18).UtcKind();
		var to = from.AddHours(1); // Only 1 hour of data to limit download size

		const int maxCount = 100;
		var allMessages = new List<Message>();
		var adapter = CreateAdapter<OkexHistoryMessageAdapter>();

		var counter = 0;

		await foreach (var msg in adapter.ConnectAndDownloadAsync<Message>(new MarketDataMessage
		{
			SecurityId = secId,
			IsSubscribe = true,
			DataType2 = DataType.MarketDepth,
			From = from,
			To = to,
			Count = maxCount,
			MaxDepth = 20,
		}).WithCancellation(CancellationToken))
		{
			allMessages.Add(msg);
			if (msg is QuoteChangeMessage)
				counter++;
		}

		var msgTypes = allMessages.GroupBy(m => m.GetType().Name).Select(g => $"{g.Key}:{g.Count()}");
		IsGreater(counter, 0, $"Expected at least 1 order book snapshot but got 0. All messages: {msgTypes.JoinCommaSpace()}");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task OkxHistOrderBookSwap()
	{
		SkipUnlessLive();

		var secId = $"BTC-USDT@{BoardCodes.Okex}".ToSecurityId();
		var from = new DateTime(2026, 01, 18).UtcKind();
		var to = from.AddHours(1);

		const int maxCount = 100;
		var allMessages = new List<Message>();
		var adapter = CreateAdapter<OkexHistoryMessageAdapter>();

		var counter = 0;

		await foreach (var msg in adapter.ConnectAndDownloadAsync<Message>(new MarketDataMessage
		{
			SecurityId = secId,
			IsSubscribe = true,
			DataType2 = DataType.MarketDepth,
			SecurityType = SecurityTypes.Swap,
			From = from,
			To = to,
			Count = maxCount,
			MaxDepth = 20,
		}).WithCancellation(CancellationToken))
		{
			allMessages.Add(msg);
			if (msg is QuoteChangeMessage)
				counter++;
		}

		var msgTypes = allMessages.GroupBy(m => m.GetType().Name).Select(g => $"{g.Key}:{g.Count()}");
		IsGreater(counter, 0, $"Expected at least 1 order book snapshot but got 0. All messages: {msgTypes.JoinCommaSpace()}");
	}

	[TestMethod]
	[DataRow(10)]
	[DataRow(11)]
	[DataRow(21)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CatalogRequestsCoverTheRangeInAtMostTenDayPages(int days)
	{
		var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
		var to = from.AddDays(days - 1).AddHours(1);
		var windows = new List<(DateTime From, DateTime To)>();
		using var zip = new MemoryStream();
		using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
		{
			using var writer = new StreamWriter(archive.CreateEntry("trades.csv").Open(), new UTF8Encoding(false));
			await writer.WriteAsync($"instrument_name,trade_id,side,price,size,created_time\nBTC-USDT,1,buy,60000,2,{(long)from.ToUnix(false)}\n");
		}
		var adapter = CreateAdapter<OkexHistoryMessageAdapter>(a => a.CheckDates = true);
		HistoryHttpHandler.ReplaceClient(adapter.FindAdapter<OkexHistoryMessageAdapter>(),
			new(new HistoryHttpHandler(request =>
			{
				if (request.RequestUri.AbsolutePath == "/api/v5/public/market-data-history")
				{
					var query = request.RequestUri.Query.TrimStart('?').Split('&')
						.Select(item => item.Split('=', 2)).ToDictionary(item => item[0], item => item[1]);
					windows.Add((long.Parse(query["begin"]).FromUnixAuto(), long.Parse(query["end"]).FromUnixAuto()));
					return new(HttpStatusCode.OK)
					{
						Content = new StringContent("""{"code":"0","data":[{"details":[{"groupDetails":[{"filename":"BTC-USDT-trades-2026-08-01.zip","dateTs":"1785542400000","url":"https://static.okx.com/trades.zip"}]}]}]}"""),
					};
				}
				return new(HttpStatusCode.OK) { Content = new ByteArrayContent(zip.ToArray()) };
			})));

		var trades = await DownloadTicks(adapter, SecurityId, from, to, 1).ToArrayAsync(CancellationToken);

		AreEqual(1, trades.Length);
		AreEqual((days + 9) / 10, windows.Count);
		AreEqual(from, windows[0].From);
		AreEqual(to.Date, windows[^1].To);
		for (var i = 0; i < windows.Count; i++)
		{
			IsTrue(windows[i].To - windows[i].From <= TimeSpan.FromDays(9), "A catalog request exceeds ten inclusive calendar days.");
			if (i > 0)
				AreEqual(windows[i - 1].To.AddDays(1), windows[i].From);
		}
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OrderBookUsesTheDownloadUrlProvidedByTheCatalog(bool proArchive)
	{
		var from = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
		var path = "https://static.okx.com/cdn/okx/match/orderbook/" +
			(proArchive ? "pro/" : "") + "L2/400lv/daily/20261012/BTC-USDT-L2orderbook-400lv-2026-10-12.tar.gz";
		using var archive = new MemoryStream();
		using (var gzip = new GZipStream(archive, CompressionMode.Compress, true))
		using (var tar = new TarWriter(gzip, leaveOpen: true))
		using (var content = new MemoryStream(Encoding.UTF8.GetBytes($$"""{"ts":"{{(long)from.ToUnix(false)}}","action":"snapshot","bids":[["60000","2","1"]],"asks":[["60001","3","1"]]}""" + "\n")))
		{
			tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "book.json") { DataStream = content });
		}
		var downloads = new List<string>();
		var adapter = CreateAdapter<OkexHistoryMessageAdapter>();
		HistoryHttpHandler.ReplaceClient(adapter.FindAdapter<OkexHistoryMessageAdapter>(),
			new(new HistoryHttpHandler(request =>
			{
				if (request.RequestUri.AbsolutePath == "/api/v5/public/market-data-history")
					return new(HttpStatusCode.OK)
					{
						Content = new StringContent($$"""{"code":"0","data":[{"details":[{"groupDetails":[{"filename":"BTC-USDT-L2orderbook-400lv-2026-10-12.tar.gz","dateTs":"1791763200000","url":"{{path}}"}]}]}]}"""),
					};
				downloads.Add(request.RequestUri.AbsoluteUri);
				return new(request.RequestUri.AbsoluteUri == path ? HttpStatusCode.OK : HttpStatusCode.NotFound)
				{
					Content = new ByteArrayContent(archive.ToArray()),
				};
			})));

		var books = await DownloadOrderBook(adapter, SecurityId, from, from.AddHours(1), 1).ToArrayAsync(CancellationToken);

		AreEqual(1, books.Length);
		AreEqual(60000m, books[0].Bids.Single().Price);
		AreEqual(2m, books[0].Bids.Single().Volume);
		AreEqual(60001m, books[0].Asks.Single().Price);
		AreEqual(3m, books[0].Asks.Single().Volume);
		AreEqual(1, downloads.Count);
		AreEqual(path, downloads[0]);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OrderBookCatalogHttpErrorsArePropagated()
	{
		var adapter = CreateAdapter<OkexHistoryMessageAdapter>();
		HistoryHttpHandler.ReplaceClient(adapter.FindAdapter<OkexHistoryMessageAdapter>(),
			new(new HistoryHttpHandler(request => new(request.RequestUri.AbsolutePath == "/api/v5/public/market-data-history"
				? HttpStatusCode.Forbidden : HttpStatusCode.NotFound))));
		var from = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);

		var error = await ThrowsAsync<HttpRequestException>(async () =>
			await DownloadOrderBook(adapter, SecurityId, from, from.AddHours(1), 1).ToArrayAsync(CancellationToken));

		AreEqual(HttpStatusCode.Forbidden, error.StatusCode.Value);
	}

	private static SecurityId SecurityId => new() { SecurityCode = "BTC-USDT", BoardCode = BoardCodes.Okex };

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new OkexHistoryMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USDT",
		BoardCode = BoardCodes.Okex,
	};

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		// Daily archives are published after the trading day closes. Start at midnight so
		// the first record also verifies the request without scanning most of a large file.
		message.From = DateTime.UtcNow.Date.AddDays(-3);
		message.To = message.From.Value.AddDays(1);
		message.Count = 1;
		if (message.DataType2 == DataType.MarketDepth)
			message.MaxDepth = 20;
	}
}
