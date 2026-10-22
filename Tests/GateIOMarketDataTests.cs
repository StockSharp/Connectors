namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.GateIO;
using StockSharp.GateIO.Native;
using StockSharp.Messages;

/// <summary>
/// GateIO publishes market data without credentials.
/// </summary>
[TestClass]
public class GateIOMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(false, CandleStates.Active)]
	[DataRow(true, CandleStates.Finished)]
	public void SpotRestCandlesUseTheCompletionFlagAndPublishedFieldOrder(bool isClosed, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var payload = JsonConvert.SerializeObject(new object[]
		{
			(long)open.ToUnix(), "1300", "105", "110", "90", "100", "12.5", isClosed.ToString().ToLowerInvariant(),
		});
		var candle = JsonConvert.DeserializeObject<StockSharp.GateIO.Native.Spot.Model.RestCandle>(payload);

		var message = candle.ToCandleMessage(new MarketDataMessage { TransactionId = 42 });

		AreEqual(expected, message.State);
		AreEqual(open, message.OpenTime);
		AreEqual(100m, message.OpenPrice);
		AreEqual(105m, message.ClosePrice);
		AreEqual(12.5m, message.TotalVolume);
	}

	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void DerivativesRestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var subscription = new MarketDataMessage { TransactionId = 42, DataType2 = TimeSpan.FromMinutes(1).TimeFrame() };
		var snapshotTime = open.AddMilliseconds(elapsedMilliseconds);
		var futures = new StockSharp.GateIO.Native.Futures.Model.RestCandle { Time = open, Open = 100, High = 110, Low = 90, Close = 105, Volume = 12.5 };
		var delivery = new StockSharp.GateIO.Native.Delivery.Model.RestCandle { Time = open, Open = 100, High = 110, Low = 90, Close = 105, Volume = 12.5 };
		var options = new StockSharp.GateIO.Native.Options.Model.RestCandle { Time = open, Open = 100, High = 110, Low = 90, Close = 105, Volume = 12.5 };

		foreach (var message in new[] { futures.ToCandleMessage(subscription, snapshotTime), delivery.ToCandleMessage(subscription, snapshotTime), options.ToCandleMessage(subscription, snapshotTime) })
		{
			AreEqual(expected, message.State);
			AreEqual(open, message.OpenTime);
			AreEqual(105m, message.ClosePrice);
			AreEqual(12.5m, message.TotalVolume);
		}
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GateIOMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [GateIOSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_USDT",
		BoardCode = BoardCodes.GateIO,
	};
}
