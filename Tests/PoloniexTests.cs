namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Poloniex;
using StockSharp.Poloniex.Native.Model;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Poloniex.</summary>
[TestClass]
public class PoloniexTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void RestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var candle = new PoloniexCandle { StartTime = (long)open.ToUnix(false), CloseTime = (long)open.AddMinutes(1).AddMilliseconds(-1).ToUnix(false), Open = 100, High = 110, Low = 90, Close = 105, Quantity = 12.5m };
		var subscription = new MarketDataMessage { DataType2 = TimeSpan.FromMinutes(1).TimeFrame() };

		var message = candle.ToCandleMessage(subscription, open.AddMilliseconds(elapsedMilliseconds));

		AreEqual(expected, message.State);
		AreEqual(open, message.OpenTime);
		AreEqual(105m, message.ClosePrice);
		AreEqual(12.5m, message.TotalVolume);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PoloniexMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.Poloniex,
	};
}
