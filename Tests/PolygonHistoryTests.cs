namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BusinessEntities;
using StockSharp.Messages;
using StockSharp.PolygonIO;

[TestClass]
[DoNotParallelize]
public class PolygonHistoryTests : HistoryTestBase
{
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
