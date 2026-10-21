namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BusinessEntities;
using StockSharp.Messages;
using StockSharp.MoexISS;

[TestClass]
public class MoexISSHistoryTests : HistoryTestBase
{
	/// <summary>
	/// Requires separate opt-in for the ISS dividend-history service.
	/// </summary>
	[TestInitialize]
	public void InitializeDividendAccess()
	{
		if (LiveTestAccess.GetMoexDividendSkipReason(TestContext?.TestName,
			Env("STOCKSHARP_LIVE_TESTS"), Env(LiveTestAccess.MoexDividendOptInVariable)) is string reason)
			Inconclusive(reason);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task MoexIssLookupAll()
	{
		SkipUnlessLive();

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAdapter<MoexISSMessageAdapter>(), LookupAll).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 30000, $"counter={counter} expected > 30000");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task MoexIssLookupLimit()
	{
		SkipUnlessLive();

		const int count = 300;

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAdapter<MoexISSMessageAdapter>(), new() { Count = count }).WithCancellation(CancellationToken))
			counter++;
		AreEqual(count, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task MoexIssLookupSber()
	{
		SkipUnlessLive();

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAdapter<MoexISSMessageAdapter>(), new() { SecurityId = new() { SecurityCode = "SBER" } }).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 20, $"counter={counter} expected > 20");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task MoexIssLookupSberLimit()
	{
		SkipUnlessLive();

		const int count = 3;

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAdapter<MoexISSMessageAdapter>(), new() { SecurityId = new() { SecurityCode = "SBER" }, Count = count }).WithCancellation(CancellationToken))
			counter++;
		AreEqual(count, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public Task MoexIssCandlesSber()
	{
		SkipUnlessLive();

		return DownloadCandlesMultiTf<MoexISSMessageAdapter>("SBER@TQBR".ToSecurityId(), default, new DateTime(2022, 11, 21).UtcKind(), new()
		{
			{ TimeSpan.FromMinutes(1), 812 },
			{ TimeSpan.FromMinutes(10), 249 },
			{ TimeSpan.FromHours(1), 45 },
		}, CancellationToken);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task MoexIssDivSber()
	{
		SkipUnlessLive();

		var secId = new Security { Id = "SBER@TQBR" }.ToSecurityId();
		var from = new DateTime(2010, 1, 30);
		var to = new DateTime(PrevYear, 5, 31);

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadLevel1(CreateAdapter<MoexISSMessageAdapter>(), secId, from, to).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from, to, ref lastTime);
			IsGreater((decimal)msg.Changes[Level1Fields.Dividend], 0m);
			counter++;
		}
		IsInRange(counter, 3, 5);
	}
}
