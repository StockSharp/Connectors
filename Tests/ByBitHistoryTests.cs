namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.ByBitHistory;
using StockSharp.Messages;

/// <summary>
/// Tests for the ByBitHistory connector.
/// </summary>
[TestClass]
public class ByBitHistoryTests : HistoryTestBase
{
	[TestMethod]
	[TestCategory("Integration")]
	[RegionalLiveTests]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task ByBitHistLookupAll()
	{
		SkipUnlessLive();

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAdapter<ByBitHistoryMessageAdapter>(), LookupAll).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 200, $"counter={counter} expected > 200");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[RegionalLiveTests]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task ByBitHistLookupLimit()
	{
		SkipUnlessLive();

		const int count = 120;
		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAdapter<ByBitHistoryMessageAdapter>(), new() { Count = count }).WithCancellation(CancellationToken))
			counter++;
		AreEqual(count, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task ByBitHistTicksSpot()
	{
		SkipUnlessLive();

		var secId = $"BTCUSDT@{BoardCodes.ByBit}".ToSecurityId();
		var from = new DateTime(2024, 11, 1).UtcKind();
		var to = from.EndOfDay();
		const int maxCount = 1500;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadTicks(CreateAdapter<ByBitHistoryMessageAdapter>(), secId, from, to, maxCount, SecurityTypes.CryptoCurrency).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task ByBitHistTicksLinear()
	{
		SkipUnlessLive();

		var secId = $"BTCUSDT@{BoardCodes.ByBitLin}".ToSecurityId();
		var from = new DateTime(2024, 11, 1).UtcKind();
		var to = from.EndOfDay();
		const int maxCount = 1500;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadTicks(CreateAdapter<ByBitHistoryMessageAdapter>(), secId, from, to, maxCount, SecurityTypes.Future).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task ByBitHistOrderBookSpot()
	{
		SkipUnlessLive();

		var secId = $"BTCUSDT@{BoardCodes.ByBit}".ToSecurityId();
		var from = new DateTime(2025, 09, 05).UtcKind();
		var to = from.EndOfDay();
		const int maxCount = 1500;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadOrderBook(CreateAdapter<ByBitHistoryMessageAdapter>(), secId, from, to, maxCount, SecurityTypes.CryptoCurrency).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task ByBitHistOrderBookLinear()
	{
		SkipUnlessLive();

		var secId = $"BTCUSDT@{BoardCodes.ByBitLin}".ToSecurityId();
		var from = new DateTime(2025, 09, 05).UtcKind();
		var to = from.EndOfDay();
		const int maxCount = 1500;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadOrderBook(CreateAdapter<ByBitHistoryMessageAdapter>(), secId, from, to, maxCount, SecurityTypes.Future).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task ByBitHistOrderBookInv()
	{
		SkipUnlessLive();

		var secId = $"BTCUSD@{BoardCodes.ByBitInv}".ToSecurityId();
		var from = new DateTime(2025, 09, 01).UtcKind();
		var to = from.EndOfDay();
		const int maxCount = 1500;
		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadOrderBook(CreateAdapter<ByBitHistoryMessageAdapter>(), secId, from, to, maxCount, SecurityTypes.Future).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			counter++;
		}
		AreEqual(maxCount, counter);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task LookupPropagatesHttpFailure()
	{
		var adapter = CreateAdapter<ByBitHistoryMessageAdapter>();
		using var http = new HttpClient(new HistoryHttpHandler(_ => new(HttpStatusCode.Forbidden)
		{
			Content = new StringContent("Access denied."),
		}));
		HistoryHttpHandler.ReplaceClient(adapter.FindAdapter<ByBitHistoryMessageAdapter>(), http);

		await ThrowsAsync<HttpRequestException>(async () =>
		{
			await foreach (var _ in DownloadSecurities(adapter, LookupAll).WithCancellation(CancellationToken))
			{
			}
		});
	}
}
