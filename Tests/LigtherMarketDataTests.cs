namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Ligther;
using StockSharp.Ligther.Native.Common;
using StockSharp.Messages;

/// <summary>
/// Ligther publishes market data without credentials.
/// </summary>
[TestClass]
public class LigtherMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	public void PollingCanCompleteTheOnlyBarReturnedAtTheStartOfASubscription()
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var cursor = LigtherSectionAdapter.StartCandleCursor(open);
		cursor = LigtherSectionAdapter.AdvanceCandleCursor(cursor, open, CandleStates.Active);

		IsTrue(cursor < open, "The forming first bar must remain eligible for another poll.");
		AreEqual(open, LigtherSectionAdapter.AdvanceCandleCursor(cursor, open, CandleStates.Finished));
	}

	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void TradeHistoryKeepsTheCurrentCandleActive(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var trades = new[]
		{
			new JObject { ["timestamp"] = (long)open.AddSeconds(15).ToUnix(false), ["trade_id"] = 2, ["price"] = "105", ["size"] = "7.5" },
			new JObject { ["timestamp"] = (long)open.AddSeconds(5).ToUnix(false), ["trade_id"] = 1, ["price"] = "100", ["size"] = "5" },
		};

		var message = LigtherSectionAdapter.BuildCandles(trades, TestSecurityId, TimeSpan.FromMinutes(1), open, open.AddMinutes(2), open.AddMilliseconds(elapsedMilliseconds)).Single();

		AreEqual(expected, message.State);
		AreEqual(open, message.OpenTime);
		AreEqual(100m, message.OpenPrice);
		AreEqual(105m, message.ClosePrice);
		AreEqual(12.5m, message.TotalVolume);
	}

	[TestMethod]
	public void PollingRevisitsAnOpenMinuteUntilItCloses()
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var previous = open.AddMinutes(-1);
		AreEqual(previous, LigtherSectionAdapter.AdvanceCandleCursor(previous, open, CandleStates.Active));
		AreEqual(open, LigtherSectionAdapter.AdvanceCandleCursor(previous, open, CandleStates.Finished));
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new LigtherMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [LigtherSections.Derivatives],
		UseWsReadOnlyMode = true,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC",
		BoardCode = BoardCodes.LigtherDerivatives,
	};
}
