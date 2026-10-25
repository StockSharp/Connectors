namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;
using Moq.Protected;

using Newtonsoft.Json;

using StockSharp.BusinessEntities;
using StockSharp.Messages;
using StockSharp.MoexISS;
using StockSharp.MoexISS.Native.Requests;

/// <summary>
/// Tests for the MoexISS connector.
/// </summary>
[TestClass]
public class MoexISSTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandleCheckIncludesSessionsBeforeNonTradingDays()
	{
		var fixture = new Mock<MoexISSTests> { CallBase = true };
		fixture.Protected().Setup<MessageAdapter>("CreateAdapter")
			.Returns(new PastSessionCandleAdapter(DateTime.UtcNow.Date.AddDays(-5)));
		fixture.Protected().SetupGet<TimeSpan>("DataTimeout").Returns(TimeSpan.FromSeconds(1));
		fixture.Object.TestContext = TestContext;

		await fixture.Object.Candles();
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task EmptyDividendTableIsAValidResponse()
	{
		using var http = new HttpClient(new HistoryHttpHandler(_ => new(HttpStatusCode.OK)
		{
			Content = new StringContent("{\"dividends\":{\"metadata\":{},\"columns\":[\"registryclosedate\",\"value\",\"currencyid\"],\"data\":[]}}"),
		})) { BaseAddress = new("https://iss.moex.com/iss/") };
		var response = await new SecurityDividendsRequest(http, "SBER").Get(CancellationToken);
		AreEqual(0, response.Dividends.Data.Count);
		AreEqual(3, response.Dividends.Columns.Count);
	}

	[TestMethod]
	[DataRow("{\"description\":{},\"boards\":{}}")]
	[DataRow("{\"dividends\":null}")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task MissingDividendTableReportsUnavailableData(string response)
	{
		using var http = new HttpClient(new HistoryHttpHandler(_ => new(HttpStatusCode.OK)
		{
			Content = new StringContent(response),
		})) { BaseAddress = new("https://iss.moex.com/iss/") };
		var error = await ThrowsAsync<JsonSerializationException>(async () =>
			await new SecurityDividendsRequest(http, "SBER").Get(CancellationToken));
		IsTrue(error.Message.Contains("dividends", StringComparison.OrdinalIgnoreCase));
	}

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

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new MoexISSMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SBER",
		BoardCode = "TQBR",
	};

	/// <inheritdoc />
	protected override TimeSpan CandleTimeFrame => TimeSpan.FromHours(1);

	/// <inheritdoc />
	/// <remarks>Include earlier sessions during weekends and exchange holidays.</remarks>
	protected override int CandlesDepth => 14 * 24;

	// ISS has no level1 stream: the adapter maps that data type to the dividend history
	// (/iss/securities/<code>/dividends.json), which holds a handful of records per year and
	// therefore is empty for any short window the harness asks for
	/// <inheritdoc />
	protected override DataType[] SkippedDataTypes => [DataType.Level1];
}
