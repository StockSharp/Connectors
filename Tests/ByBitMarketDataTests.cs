namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.ByBit;
using StockSharp.ByBit.Native;
using StockSharp.ByBit.Native.Model;
using StockSharp.Messages;

/// <summary>
/// ByBit publishes market data without credentials.
/// </summary>
[TestClass]
[RegionalLiveTests(nameof(ByBitMarketDataTests.Securities))]
[RegionalLiveTests(nameof(ByBitMarketDataTests.Candles))]
[RegionalLiveTests(nameof(ByBitMarketDataTests.ASpotInstrumentSaysHowManyDecimalsItsPricesHave))]
[RegionalLiveTests(nameof(ByBitMarketDataTests.ADayOfCandlesFromDaysAgo))]
public class ByBitMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void RestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var candle = JsonConvert.DeserializeObject<Kline>(JsonConvert.SerializeObject(new object[]
		{
			(long)open.ToUnix(false), "100", "110", "90", "105", "12.5", "1300",
		}));
		var subscription = new MarketDataMessage { TransactionId = 42, DataType2 = TimeSpan.FromMinutes(1).TimeFrame() };

		var message = candle.ToCandleMessage(subscription, open.AddMilliseconds(elapsedMilliseconds));

		AreEqual(expected, message.State);
		AreEqual(open, message.OpenTime);
		AreEqual(105m, message.ClosePrice);
		AreEqual(12.5m, message.TotalVolume);
	}

	[TestMethod]
	public void RestMonthlyCandleClosesAtTheCalendarBoundary()
	{
		var open = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
		var candle = new Kline { OpenTime = (long)open.ToUnix(false) };
		var subscription = new MarketDataMessage { DataType2 = TimeSpan.FromDays(30).TimeFrame() };

		AreEqual(CandleStates.Active, candle.ToCandleMessage(subscription, open.AddMonths(1).AddMilliseconds(-1)).State);
		AreEqual(CandleStates.Finished, candle.ToCandleMessage(subscription, open.AddMonths(1)).State);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ByBitMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [ByBitSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.ByBit,
	};

	/// <summary>
	/// A spot instrument says how many decimals its prices have.
	/// </summary>
	/// <remarks>
	/// The venue gives a price scale for its derivatives alone, so its spot instruments came without one, and a
	/// product that checks for it - Hydra marks such an instrument as not set up - took them for broken.
	/// </remarks>
	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000)]
	public Task ASpotInstrumentSaysHowManyDecimalsItsPricesHave() => RunAsync(async harness =>
	{
		var result = await harness.LookupSecuritiesAsync(SecuritiesLookup, SecuritiesLimit, SecuritiesTimeout, CancellationToken);

		var instrument = result.Securities.Single(security => security.SecurityId == TestSecurityId);

		IsNotNull(instrument.PriceStep, "The instrument has no price step.");
		AreEqual(instrument.PriceStep.Value.GetCachedDecimals(), instrument.Decimals);
	});

	/// <summary>
	/// A session let go after a disconnect ends without errors.
	/// </summary>
	/// <remarks>
	/// The reset that follows a disconnect asked every socket, closed by then, to close again, and each one answered
	/// with an error: a product that looked instruments up and let the connection go showed one per section.
	/// </remarks>
	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000)]
	public Task ASessionLetGoAfterADisconnectEndsWithoutErrors() => RunAsync(async harness =>
	{
		await harness.DisconnectAsync(ConnectTimeout, CancellationToken);

		var reader = harness.CreateReader();
		await harness.Adapter.SendInMessageAsync(new ResetMessage(), CancellationToken);
		await reader.WaitAsync<ResetMessage>(null, ConnectTimeout, CancellationToken);

		AreEqual(0, harness.Errors.Length, string.Join(Environment.NewLine, harness.Errors.Select(error => error.Message)));
	});

	/// <summary>
	/// A day of minute candles from before the latest thousand of them.
	/// </summary>
	/// <remarks>
	/// The venue starts a page of candles at the moment it is given only when the moment is sent under the name it
	/// knows. Sent under another, it is ignored and the latest thousand come back - every one of them after a day that
	/// is days old, so the day came back empty.
	/// </remarks>
	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000)]
	public Task ADayOfCandlesFromDaysAgo() => RunAsync(async harness =>
	{
		var from = DateTime.UtcNow.Date.AddDays(-3);
		var to = from.AddDays(1);

		var result = await harness.SubscribeAsync<TimeFrameCandleMessage>(
			TimeSpan.FromMinutes(1).TimeFrame(), TestSecurityId,
			mdMsg =>
			{
				mdMsg.From = from;
				mdMsg.To = to;
			},
			null,
			DataTimeout, CancellationToken);

		CheckData(result, "candles");

		AreEqual(from, result.Data.OpenTime, "The first candle is not the first minute of the day asked for.");
	});
}
