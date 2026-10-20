namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BinanceHistory;
using StockSharp.Messages;

[TestClass]
public class BinanceHistoryTests : HistoryTestBase
{
	[TestMethod]
	[TestCategory("Integration")]
	[RegionalLiveTests]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task BinHistLookupAll()
	{
		SkipUnlessLive();

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAdapter<BinanceHistoryMessageAdapter>(), LookupAll).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 3000);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task BinHistLookupLimit()
	{
		SkipUnlessLive();

		const int count = 300;

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAdapter<BinanceHistoryMessageAdapter>(), new() { Count = count }).WithCancellation(CancellationToken))
			counter++;
		AreEqual(count, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(360000, CooperativeCancellation = true)]
	public Task BinHistCandlesFut()
	{
		SkipUnlessLive();

		return DownloadCandlesMultiTf<BinanceHistoryMessageAdapter>($"BTCUSDT@{BoardCodes.Binance}".ToSecurityId(), SecurityTypes.Future, new DateTime(2022, 11, 21).UtcKind(), new()
		{
			{ TimeSpan.FromSeconds(1), 86400 },
			{ TimeSpan.FromMinutes(1), 1440 },
			{ TimeSpan.FromMinutes(3), 1440 },
			{ TimeSpan.FromMinutes(5), 864 },
			{ TimeSpan.FromMinutes(15), 288 },
			{ TimeSpan.FromMinutes(30), 144 },
			{ TimeSpan.FromHours(1), 72 },
			{ TimeSpan.FromHours(2), 84 },
			{ TimeSpan.FromHours(4), 42 },
			{ TimeSpan.FromHours(6), 28 },
			{ TimeSpan.FromHours(12), 14 },
			{ TimeSpan.FromDays(1), 7 },
		}, CancellationToken);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(360000, CooperativeCancellation = true)]
	public Task BinHistCandlesFutCoin()
	{
		SkipUnlessLive();

		return DownloadCandlesMultiTf<BinanceHistoryMessageAdapter>($"BTCUSD_PERP@{BoardCodes.BinanceCoin}".ToSecurityId(), SecurityTypes.Future, new DateTime(2022, 11, 21).UtcKind(), new()
		{
			{ TimeSpan.FromSeconds(1), 0 },
			{ TimeSpan.FromMinutes(1), 1440 },
			{ TimeSpan.FromMinutes(3), 1440 },
			{ TimeSpan.FromMinutes(5), 864 },
			{ TimeSpan.FromMinutes(15), 288 },
			{ TimeSpan.FromMinutes(30), 144 },
			{ TimeSpan.FromHours(1), 72 },
			{ TimeSpan.FromHours(2), 84 },
			{ TimeSpan.FromHours(4), 42 },
			{ TimeSpan.FromHours(6), 28 },
			{ TimeSpan.FromHours(12), 14 },
			{ TimeSpan.FromDays(1), 7 },
		}, CancellationToken);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(360000, CooperativeCancellation = true)]
	public Task BinHistCandlesSpot()
	{
		SkipUnlessLive();

		// Spot API has stricter rate limits than Futures, reduce 1s candles count
		return DownloadCandlesMultiTf<BinanceHistoryMessageAdapter>($"BTCUSDT@{BoardCodes.Binance}".ToSecurityId(), default, new DateTime(2022, 11, 21).UtcKind(), new()
		{
			{ TimeSpan.FromSeconds(1), 86400 },
			{ TimeSpan.FromMinutes(1), 1440 },
			{ TimeSpan.FromMinutes(3), 1440 },
			{ TimeSpan.FromMinutes(5), 864 },
			{ TimeSpan.FromMinutes(15), 288 },
			{ TimeSpan.FromMinutes(30), 144 },
			{ TimeSpan.FromHours(1), 72 },
			{ TimeSpan.FromHours(2), 84 },
			{ TimeSpan.FromHours(4), 42 },
			{ TimeSpan.FromHours(6), 28 },
			{ TimeSpan.FromHours(12), 14 },
			{ TimeSpan.FromDays(1), 7 },
		}, CancellationToken);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task BinHistTicksFut()
	{
		SkipUnlessLive();

		var secId = $"BTCUSDT@{BoardCodes.BinanceFut}".ToSecurityId();
		var from = new DateTime(2022, 11, 21).UtcKind();
		var to = from.EndOfDay();

		const int maxCount = 10000;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadTicks(CreateAdapter<BinanceHistoryMessageAdapter>(), secId, from, to, maxCount, SecurityTypes.Future).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task BinHistTicksFutCoin()
	{
		SkipUnlessLive();

		var secId = $"BTCUSD_PERP@{BoardCodes.BinanceCoin}".ToSecurityId();
		var from = new DateTime(2022, 11, 21).UtcKind();
		var to = from.EndOfDay();

		const int maxCount = 10000;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadTicks(CreateAdapter<BinanceHistoryMessageAdapter>(), secId, from, to, maxCount, SecurityTypes.Future).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task BinHistTicksSpot()
	{
		SkipUnlessLive();

		var secId = $"BTCUSDT@{BoardCodes.Binance}".ToSecurityId();
		var from = new DateTime(2022, 11, 21).UtcKind();
		var to = from.EndOfDay();

		const int maxCount = 10000;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadTicks(CreateAdapter<BinanceHistoryMessageAdapter>(), secId, from, to, maxCount).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task BinHistLevel1Fut()
	{
		SkipUnlessLive();

		var secId = $"BTCUSDT@{BoardCodes.BinanceFut}".ToSecurityId();
		var from = new DateTime(2024, 02, 01).UtcKind();
		var to = from.EndOfDay();

		const int maxCount = 10000;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadLevel1(CreateAdapter<BinanceHistoryMessageAdapter>(), secId, from, to, maxCount, secType: SecurityTypes.Future).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task BinHistLevel1FutCoin()
	{
		SkipUnlessLive();

		var secId = $"BTCUSD_PERP@{BoardCodes.BinanceCoin}".ToSecurityId();
		var from = new DateTime(2024, 02, 01).UtcKind();
		var to = from.EndOfDay();

		const int maxCount = 10000;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadLevel1(CreateAdapter<BinanceHistoryMessageAdapter>(), secId, from, to, maxCount, secType: SecurityTypes.Future).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task BinHistOrderBookFut()
	{
		SkipUnlessLive();

		var secId = $"BTCUSDT@{BoardCodes.BinanceFut}".ToSecurityId();
		var from = new DateTime(2025, 04, 01).UtcKind();
		var to = from.EndOfDay();

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadOrderBook(CreateAdapter<BinanceHistoryMessageAdapter>(), secId, from, to, 2520, SecurityTypes.Future).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(2520, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[RegionalLiveTests]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task BinHistOrderBookFutCoin()
	{
		SkipUnlessLive();

		var secId = $"BTCUSD_PERP@{BoardCodes.BinanceCoin}".ToSecurityId();
		var from = new DateTime(2025, 04, 01).UtcKind();
		var to = from.EndOfDay();

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadOrderBook(CreateAdapter<BinanceHistoryMessageAdapter>(), secId, from, to, 2520, SecurityTypes.Future).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(2520, counter);
	}
}
