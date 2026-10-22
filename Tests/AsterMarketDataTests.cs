namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Aster;
using StockSharp.Aster.Native.Common;
using StockSharp.Messages;

/// <summary>
/// Aster publishes market data without credentials.
/// </summary>
[TestClass]
public class AsterMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	public void PollingCanCompleteTheOnlyBarReturnedAtTheStartOfASubscription()
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var cursor = BinanceLikeSectionAdapter.StartCandleCursor(open);
		cursor = BinanceLikeSectionAdapter.AdvanceCandleCursor(cursor, open, CandleStates.Active);

		IsTrue(cursor < open, "The forming first bar must remain eligible for another poll.");
		AreEqual(open, BinanceLikeSectionAdapter.AdvanceCandleCursor(cursor, open, CandleStates.Finished));
	}

	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void RestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var candle = new JArray((long)open.ToUnix(false), "100", "110", "90", "105", "12.5", (long)open.AddMinutes(1).AddMilliseconds(-1).ToUnix(false));

		AreEqual(expected, BinanceLikeSectionAdapter.GetRestCandleState(candle, open.AddMilliseconds(elapsedMilliseconds)));
	}

	[TestMethod]
	public void PollingKeepsTheOpenMinuteEligibleUntilItsCompletedSnapshotArrives()
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var previous = open.AddMinutes(-1);
		var candle = new JArray((long)open.ToUnix(false), "100", "110", "90", "105", "12.5", (long)open.AddMinutes(1).AddMilliseconds(-1).ToUnix(false));

		var active = BinanceLikeSectionAdapter.GetRestCandleState(candle, open.AddSeconds(20));
		var cursor = BinanceLikeSectionAdapter.AdvanceCandleCursor(previous, open, active);
		AreEqual(previous, cursor);

		var finished = BinanceLikeSectionAdapter.GetRestCandleState(candle, open.AddMinutes(1));
		AreEqual(open, BinanceLikeSectionAdapter.AdvanceCandleCursor(cursor, open, finished));
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new AsterMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [AsterSections.Derivatives],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.AsterDerivatives,
	};
}
