namespace StockSharp.Connectors.Tests;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.AlphaVantage;
using StockSharp.AlphaVantage.Native;
using StockSharp.Messages;

/// <summary>
/// Tests for the AlphaVantage connector.
/// </summary>
[TestClass]
[CLSCompliant(false)]
[DoNotParallelize]
public class AlphaVantageTests : HistoryTestBase
{
	[TestMethod]
	[DataRow(SecurityTypes.Stock, 10080, "2025-07-18", "2025-07-14")]
	[DataRow(SecurityTypes.Stock, 43200, "2025-07-31", "2025-07-01")]
	[DataRow(SecurityTypes.CryptoCurrency, 10080, "2025-07-20", "2025-07-14")]
	[DataRow(SecurityTypes.CryptoCurrency, 43200, "2025-07-31", "2025-07-01")]
	[DataRow(SecurityTypes.Stock, 1440, "2025-07-18", "2025-07-18")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandleOpenTimeUsesTheBeginningOfItsPeriod(SecurityTypes securityType, int minutes, string responseDate, string expectedDate)
	{
		await using var server = new HistoryHttpServer(() =>
			"{\"Time Series\":{\"" + responseDate + "\":{\"1. open\":\"1\",\"2. high\":\"3\",\"3. low\":\"0.5\",\"4. close\":\"2\",\"5. volume\":\"10\"}}}", CancellationToken);
		var adapter = CreateAdapter<AlphaVantageMessageAdapter>(a =>
		{
			a.QueryEndpoint = server.Address.ToString();
			a.Token = "test-key".Secure();
			a.IterationInterval = TimeSpan.Zero;
		});
		var timeFrame = TimeSpan.FromMinutes(minutes);
		var expected = expectedDate.ToDateTime("yyyy-MM-dd").UtcKind();
		var securityId = new SecurityId
		{
			SecurityCode = securityType == SecurityTypes.CryptoCurrency ? "BTC/USD" : "AAPL",
			BoardCode = BoardCodes.AlphaVantage,
		};
		var count = 0;
		await foreach (var candle in DownloadCandles(adapter, securityId, timeFrame, expected,
			expected + timeFrame - TimeSpan.FromTicks(1), count: 1, secType: securityType).WithCancellation(CancellationToken))
		{
			AreEqual(expected, candle.OpenTime);
			AreEqual(2m, candle.ClosePrice);
			count++;
		}

		AreEqual(1, count);
	}

	[TestMethod]
	[DataRow("Information")]
	[DataRow("Note")]
	[DataRow("Error Message")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task LookupReportsApiErrors(string field)
	{
		await using var server = new HistoryHttpServer(() => $"{{\"{field}\":\"Request limit reached.\"}}", CancellationToken);
		using var client = new AlphaClient(server.Address.ToString(), TimeSpan.FromSeconds(1));
		var error = await ThrowsAsync<InvalidOperationException>(async () =>
			await client.Lookup("AAPL", "test-key".Secure(), CancellationToken));
		IsTrue(error.Message.Contains("Request limit reached."));
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task RequestsFromDifferentClientsAreSpaced()
	{
		var timer = Stopwatch.StartNew();
		TimeSpan? lastRequest = null;
		var interval = TimeSpan.MaxValue;
		await using var server = new HistoryHttpServer(() =>
		{
			if (lastRequest is { } last)
				interval = timer.Elapsed - last;
			lastRequest = timer.Elapsed;
			return "{\"bestMatches\":[]}";
		}, CancellationToken);
		using var first = new AlphaClient(server.Address.ToString(), TimeSpan.FromSeconds(1));
		using var second = new AlphaClient(server.Address.ToString(), TimeSpan.FromSeconds(1));
		await first.Lookup("AAPL", "test-key".Secure(), CancellationToken);
		await second.Lookup("AAPL", "test-key".Secure(), CancellationToken);
		IsTrue(interval >= TimeSpan.FromSeconds(1), $"Requests were only {interval} apart.");
	}

	// The token comes from the environment. A machine without the variable reports the tests
	// inconclusive rather than failing them on a null token.
	private static IMessageAdapter CreateAlphaVantage()
	{
		var token = Env("ALPHAVANTAGE_TOKEN");

		if (token.IsEmpty())
			Inconclusive("ALPHAVANTAGE_TOKEN is not set.");

		return CreateAdapter<AlphaVantageMessageAdapter>(a => a.Token = token.Secure());
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlphaVantageLookupMulti()
	{
		SkipUnlessLive();

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAlphaVantage(), new() { SecurityId = new() { SecurityCode = "A" }, SecurityTypes = [SecurityTypes.Stock, SecurityTypes.Currency] }).WithCancellation(CancellationToken))
			counter++;
		IsLess(counter, 40);
		IsGreater(counter, 20);

		counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAlphaVantage(), new() { SecurityTypes = [SecurityTypes.Stock, SecurityTypes.Currency] }).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 100);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlphaVantageLookupAapl()
	{
		SkipUnlessLive();

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAlphaVantage(), new() { SecurityId = new() { SecurityCode = "AAPL" } }).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 3);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlphaVantageLookupApplLimit()
	{
		SkipUnlessLive();

		const int count = 1;

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAlphaVantage(), new() { SecurityId = new() { SecurityCode = "AAPL" }, Count = count }).WithCancellation(CancellationToken))
			counter++;
		AreEqual(count, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlphaVantageLookupCrypto()
	{
		SkipUnlessLive();

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAlphaVantage(), new() { SecurityType = SecurityTypes.CryptoCurrency }).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 500);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlphaVantageLookupCryptoLimit()
	{
		SkipUnlessLive();

		const int count = 10;

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAlphaVantage(), new() { SecurityType = SecurityTypes.CryptoCurrency, Count = count }).WithCancellation(CancellationToken))
			counter++;
		AreEqual(count, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlphaVantageLookupFX()
	{
		SkipUnlessLive();

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAlphaVantage(), new() { SecurityType = SecurityTypes.Currency }).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 100);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlphaVantageLookupFXLimit()
	{
		SkipUnlessLive();

		const int count = 10;

		var counter = 0;
		await foreach (var _ in DownloadSecurities(CreateAlphaVantage(), new() { SecurityType = SecurityTypes.Currency, Count = count }).WithCancellation(CancellationToken))
			counter++;
		AreEqual(count, counter);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[DataRow(1440)]
	[DataRow(10080)]
	[DataRow(43200)]
	[Timeout(120000, CooperativeCancellation = true)]
	public Task AlphaVantageCandlesAapl(int minutes)
		=> CheckCandles("AAPL", SecurityTypes.Stock, minutes);

	[TestMethod]
	[TestCategory("Integration")]
	[DataRow(1440)]
	[DataRow(10080)]
	[DataRow(43200)]
	[Timeout(120000, CooperativeCancellation = true)]
	public Task AlphaVantageCandlesCrypto(int minutes)
		=> CheckCandles("BTC/USD", SecurityTypes.CryptoCurrency, minutes);

	[TestMethod]
	[TestCategory("Integration")]
	[DataRow(1440)]
	[DataRow(10080)]
	[DataRow(43200)]
	[Timeout(120000, CooperativeCancellation = true)]
	public Task AlphaVantageCandlesFX(int minutes)
		=> CheckCandles("EUR/USD", SecurityTypes.Currency, minutes);

	[TestMethod]
	[TestCategory("Integration")]
	[TestCategory("Paid")]
	[DataRow(1)]
	[DataRow(5)]
	[DataRow(15)]
	[DataRow(30)]
	[DataRow(60)]
	[Timeout(120000, CooperativeCancellation = true)]
	public Task AlphaVantageIntradayCandlesAapl(int minutes)
	{
		SkipUnlessLive();

		if (!Env("ALPHAVANTAGE_PAID_TESTS").EqualsIgnoreCase("1"))
			Inconclusive("Intraday stock history requires a paid Alpha Vantage plan. Set ALPHAVANTAGE_PAID_TESTS=1 when it is available.");

		return CheckCandles("AAPL", SecurityTypes.Stock, minutes);
	}

	private async Task CheckCandles(string symbol, SecurityTypes securityType, int minutes)
	{
		SkipUnlessLive();

		var now = DateTime.UtcNow;
		var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
		var from = month.AddMonths(-4);
		var to = month.AddTicks(-1);
		var securityId = $"{symbol}@{BoardCodes.AlphaVantage}".ToSecurityId();
		var timeFrame = TimeSpan.FromMinutes(minutes);
		var lastTime = ResetTimeOrder();
		var count = 0;

		await foreach (var candle in DownloadCandles(CreateAlphaVantage(), securityId, timeFrame,
			from, to, count: 2, secType: securityType).WithCancellation(CancellationToken))
		{
			Validate(candle, securityId, from, to, ref lastTime);
			AreEqual(timeFrame.TimeFrame(), candle.DataType);
			AreEqual(CandleStates.Finished, candle.State);
			count++;
		}

		AreEqual(2, count);
	}
}
