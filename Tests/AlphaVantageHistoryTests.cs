namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.AlphaVantage;
using StockSharp.Messages;

[TestClass]
[DoNotParallelize]
[CLSCompliant(false)]
public class AlphaVantageHistoryTests : HistoryTestBase
{
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
