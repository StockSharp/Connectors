namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Messages;
using StockSharp.Mexc;
using StockSharp.Mexc.Native;

/// <summary>
/// Mexc publishes market data without credentials.
/// </summary>
[TestClass]
public class MexcMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void RestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var close = open.AddMinutes(1).AddMilliseconds(-1);
		var snapshotTime = open.AddMilliseconds(elapsedMilliseconds);
		var spot = JsonConvert.DeserializeObject<Mexc.Native.Spot.Model.Candle>(JsonConvert.SerializeObject(new object[]
		{
			(long)open.ToUnix(false), "100", "110", "90", "105", "12.5", (long)close.ToUnix(false), "1250",
		}));
		var futures = new Mexc.Native.Futures.Model.Candle
		{
			OpenTime = open, CloseTime = close, Open = 100, High = 110, Low = 90, Close = 105, Volume = 12.5,
		};

		foreach (var message in new[] { spot.ToCandleMessage(42, snapshotTime), futures.ToCandleMessage(42, snapshotTime) })
		{
			AreEqual(expected, message.State);
			AreEqual(open, message.OpenTime);
			AreEqual(105m, message.ClosePrice);
			AreEqual(12.5m, message.TotalVolume);
		}
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new MexcMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [MexcSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Mexc,
	};
}
