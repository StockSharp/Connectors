namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Upbit;
using StockSharp.Upbit.Native.Model;

/// <summary>
/// Upbit publishes market data without credentials.
/// </summary>
[TestClass]
public class UpbitMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void RestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var candle = new Ohlc { DateTimeUtc = open, Open = 100, High = 110, Low = 90, Close = 105, Volume = 12.5 };

		var message = candle.ToCandleMessage(default, TimeSpan.FromMinutes(1), 42, open.AddMilliseconds(elapsedMilliseconds));

		AreEqual(expected, message.State);
		AreEqual(open, message.OpenTime);
		AreEqual(105m, message.ClosePrice);
		AreEqual(12.5m, message.TotalVolume);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new UpbitMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "KRW/BTC",
		BoardCode = BoardCodes.Upbit,
	};
}
