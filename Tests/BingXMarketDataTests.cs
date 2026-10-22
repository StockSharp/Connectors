namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.BingX;
using SpotExtensions = StockSharp.BingX.Native.Spot.Extensions;
using FuturesExtensions = StockSharp.BingX.Native.Futures.Extensions;
using StockSharp.Messages;

/// <summary>
/// BingX publishes market data without credentials.
/// </summary>
[TestClass]
public class BingXMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void SpotAndFuturesRestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var spot = JsonConvert.DeserializeObject<StockSharp.BingX.Native.Spot.Model.RestCandle>(JsonConvert.SerializeObject(new object[]
		{
			(long)open.ToUnix(false), "100", "110", "90", "105", "12.5", (long)open.AddMinutes(1).AddMilliseconds(-1).ToUnix(false), "1300",
		}));
		var futures = new StockSharp.BingX.Native.Futures.Model.RestCandle { Time = open, Open = 100, High = 110, Low = 90, Close = 105, Volume = 12.5 };
		var subscription = new MarketDataMessage { TransactionId = 42, DataType2 = TimeSpan.FromMinutes(1).TimeFrame() };
		var snapshotTime = open.AddMilliseconds(elapsedMilliseconds);

		foreach (var message in new[] { SpotExtensions.ToCandleMessage(spot, subscription, snapshotTime), FuturesExtensions.ToCandleMessage(futures, subscription, snapshotTime) })
		{
			AreEqual(expected, message.State);
			AreEqual(open, message.OpenTime);
			AreEqual(105m, message.ClosePrice);
			AreEqual(12.5m, message.TotalVolume);
		}
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BingXMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [BingXSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USDT",
		BoardCode = BoardCodes.BingX,
	};
}
