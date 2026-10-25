namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.KucoinHistory;
using StockSharp.Messages;

[TestClass]
public class KucoinHistoryTests : HistoryTestBase
{
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ArchiveBodyReadHonorsTheHttpTimeout(bool isDepth)
	{
		using var body = new AsyncArchiveStream(null);
		using var http = new HttpClient(new HistoryHttpHandler(_ => new(HttpStatusCode.OK)
		{
			Content = new StreamContent(body),
		})) { Timeout = TimeSpan.FromMilliseconds(250) };
		var adapter = CreateKucoinHistory();
		HistoryHttpHandler.ReplaceClient(adapter.FindAdapter<KucoinHistoryMessageAdapter>(), http);
		var from = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var security = new SecurityId { SecurityCode = "BTC-USDT", BoardCode = BoardCodes.Kucoin };
		IAsyncEnumerable<Message> messages = isDepth
			? DownloadOrderBook(adapter, security, from, from.EndOfDay(), 1)
			: DownloadTicks(adapter, security, from, from.EndOfDay(), 1);

		await ThrowsAsync<OperationCanceledException>(async () =>
			await messages.ToArrayAsync(CancellationToken).AsTask().WaitAsync(CancellationToken));

		IsFalse(CancellationToken.IsCancellationRequested);
		IsTrue(body.IsReadStarted);
		IsTrue(body.IsReadCanceled);
		IsTrue(body.IsDisposed);
	}

	private static IMessageAdapter CreateKucoinHistory()
		=> CreateAdapter<KucoinHistoryMessageAdapter>(a => a.CheckDates = false);

	private static IMessageAdapter CreateKucoinHistoryCheckDates()
		=> CreateAdapter<KucoinHistoryMessageAdapter>(a => a.CheckDates = true);

	#region Lookup

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task KucoinHistLookupAll()
	{
		SkipUnlessLive();

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateKucoinHistory(), LookupAll).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 500);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task KucoinHistLookupLimit()
	{
		SkipUnlessLive();

		const int count = 200;
		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateKucoinHistory(), new() { Count = count }).WithCancellation(CancellationToken))
			counter++;
		AreEqual(count, counter);
	}

	#endregion

	#region Spot

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task KucoinHistTicksSpot()
	{
		SkipUnlessLive();

		var secId = $"BTC-USDT@{BoardCodes.Kucoin}".ToSecurityId();
		var from = new DateTime(2024, 06, 01).UtcKind();
		var to = from.EndOfDay();
		const int maxCount = 5000;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadTicks(CreateKucoinHistory(), secId, from, to, maxCount).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(300000, CooperativeCancellation = true)]
	public Task KucoinHistCandlesSpot()
	{
		SkipUnlessLive();

		return DownloadCandlesMultiTf(CreateKucoinHistory, $"BTC-USDT@{BoardCodes.Kucoin}".ToSecurityId(), default, new DateTime(2024, 06, 01).UtcKind(), new()
		{
			{ TimeSpan.FromMinutes(1), 1440 },
			{ TimeSpan.FromMinutes(5), 864 },
			{ TimeSpan.FromMinutes(15), 288 },
			{ TimeSpan.FromHours(1), 72 },
			{ TimeSpan.FromDays(1), 7 },
		}, CancellationToken);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task KucoinHistOrderBookSpot()
	{
		SkipUnlessLive();

		var secId = $"BTC-USDT@{BoardCodes.Kucoin}".ToSecurityId();
		var from = new DateTime(2025, 04, 01).UtcKind(); // orderbook data available from 2025-03-31
		var to = from.EndOfDay();
		const int maxCount = 500;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadOrderBook(CreateKucoinHistory(), secId, from, to, maxCount).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	#endregion

	#region Futures

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task KucoinHistTicksFutures()
	{
		SkipUnlessLive();

		var secId = $"BTCUSDTM@{BoardCodes.KucoinFT}".ToSecurityId();
		var from = new DateTime(2024, 06, 01).UtcKind();
		var to = from.EndOfDay();
		const int maxCount = 5000;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadTicks(CreateKucoinHistory(), secId, from, to, maxCount, SecurityTypes.Future).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(300000, CooperativeCancellation = true)]
	public Task KucoinHistCandlesFutures()
	{
		SkipUnlessLive();

		return DownloadCandlesMultiTf(CreateKucoinHistory, $"BTCUSDTM@{BoardCodes.KucoinFT}".ToSecurityId(), SecurityTypes.Future, new DateTime(2024, 07, 01).UtcKind(), new()
		{
			{ TimeSpan.FromMinutes(1), 1440 },
			{ TimeSpan.FromMinutes(5), 864 },
			{ TimeSpan.FromMinutes(15), 288 },
			{ TimeSpan.FromHours(1), 72 },
			{ TimeSpan.FromDays(1), 7 },
		}, CancellationToken);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task KucoinHistOrderBookFutures()
	{
		SkipUnlessLive();

		var secId = $"BTCUSDTM@{BoardCodes.KucoinFT}".ToSecurityId();
		var from = new DateTime(2025, 05, 01).UtcKind(); // orderbook data available from 2025-03-31
		var to = from.EndOfDay();
		const int maxCount = 500;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadOrderBook(CreateKucoinHistory(), secId, from, to, maxCount, SecurityTypes.Future).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	#endregion

	#region CheckDates

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(300000, CooperativeCancellation = true)]
	public async Task KucoinHistTicksSpotCheckDates()
	{
		SkipUnlessLive();

		var secId = $"BTC-USDT@{BoardCodes.Kucoin}".ToSecurityId();
		var from = new DateTime(2024, 06, 01).UtcKind();
		var to = from.EndOfDay();
		const int maxCount = 1000;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadTicks(CreateKucoinHistoryCheckDates(), secId, from, to, maxCount).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(300000, CooperativeCancellation = true)]
	public async Task KucoinHistCandlesSpotCheckDates()
	{
		SkipUnlessLive();

		var secId = $"BTC-USDT@{BoardCodes.Kucoin}".ToSecurityId();
		var from = new DateTime(2024, 06, 01).UtcKind();
		var to = from.AddDays(1);
		const int maxCount = 100;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadCandles(CreateKucoinHistoryCheckDates(), secId, TimeSpan.FromHours(1), from, to, maxCount).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		IsGreater(counter, 0, "No candles found");
	}

	#endregion
}
