namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.BitStamp;
using StockSharp.BitStamp.Native;
using StockSharp.BitStamp.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Bitstamp publishes market data without credentials.
/// </summary>
[TestClass]
public class BitStampMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void RestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var candle = JsonConvert.DeserializeObject<Ohlc>(JsonConvert.SerializeObject(new
		{
			timestamp = (long)open.ToUnix(), open = "100", high = "110", low = "90", close = "105", volume = "12.5",
		}));
		var subscription = new MarketDataMessage { TransactionId = 42, DataType2 = TimeSpan.FromMinutes(1).TimeFrame() };

		var message = candle.ToCandleMessage(subscription, open.AddMilliseconds(elapsedMilliseconds));

		AreEqual(expected, message.State);
		AreEqual(open, message.OpenTime);
		AreEqual(105m, message.ClosePrice);
		AreEqual(12.5m, message.TotalVolume);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitStampMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USD",
		BoardCode = BoardCodes.BitStamp,
	};
}
