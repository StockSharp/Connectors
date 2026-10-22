namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Deribit;
using StockSharp.Deribit.Native;
using StockSharp.Deribit.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Deribit publishes market data without credentials.
/// </summary>
[TestClass]
public class DeribitMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void HistoricalAndStreamingCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var candle = JsonConvert.DeserializeObject<Ohlc>(JsonConvert.SerializeObject(new
		{
			tick = (long)open.ToUnix(false), open = 100, high = 110, low = 90, close = 105, volume = 12.5,
		}));

		var message = candle.ToCandleMessage(TimeSpan.FromMinutes(1), 42, open.AddMilliseconds(elapsedMilliseconds));

		AreEqual(expected, message.State);
		AreEqual(open, message.OpenTime);
		AreEqual(105m, message.ClosePrice);
		AreEqual(12.5m, message.TotalVolume);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new DeribitMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-PERPETUAL",
		BoardCode = BoardCodes.Deribit,
	};
}
