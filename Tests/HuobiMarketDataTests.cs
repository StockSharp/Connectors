namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Huobi;
using StockSharp.Messages;

/// <summary>
/// Huobi publishes market data without credentials.
/// </summary>
[TestClass]
public class HuobiMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void HistoricalAndStreamingCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		CheckCandleState(open, TimeSpan.FromMinutes(1), open.AddMilliseconds(elapsedMilliseconds), expected);
	}

	[TestMethod]
	public void MonthlyCandlesCloseAtTheNextUtcPlusEightMonth()
	{
		var open = new DateTime(2024, 2, 29, 16, 0, 0, DateTimeKind.Utc);
		var close = new DateTime(2024, 3, 31, 16, 0, 0, DateTimeKind.Utc);
		CheckCandleState(open, TimeSpan.FromTicks(TimeHelper.TicksPerMonth), close.AddMilliseconds(-1), CandleStates.Active);
		CheckCandleState(open, TimeSpan.FromTicks(TimeHelper.TicksPerMonth), close, CandleStates.Finished);
	}

	private void CheckCandleState(DateTime open, TimeSpan timeFrame, DateTime snapshotTime, CandleStates expected)
	{
		var json = JsonConvert.SerializeObject(new
		{
			id = (long)open.ToUnix(), open = "100", high = "110", low = "90", close = "105", amount = "12.5", count = 7,
		});
		var messages = new[]
		{
			JsonConvert.DeserializeObject<Huobi.Native.Spot.Model.Ohlc>(json).ToCandleMessage(TestSecurityId, timeFrame, 42, snapshotTime),
			JsonConvert.DeserializeObject<Huobi.Native.Futures.Model.Ohlc>(json).ToCandleMessage(TestSecurityId, timeFrame, 42, snapshotTime),
			JsonConvert.DeserializeObject<Huobi.Native.Usdt.Model.Ohlc>(json).ToCandleMessage(TestSecurityId, timeFrame, 42, snapshotTime),
		};

		foreach (var message in messages)
		{
			AreEqual(expected, message.State);
			AreEqual(open, message.OpenTime);
			AreEqual(105m, message.ClosePrice);
			AreEqual(12.5m, message.TotalVolume);
			AreEqual(7, message.TotalTicks);
		}
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new HuobiMessageAdapter(new IncrementalIdGenerator())
	{
		Section = HuobiSections.Spot,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.Huobi,
	};
}
