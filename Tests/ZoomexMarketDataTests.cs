namespace StockSharp.Connectors.Tests;

using System;
using System.Net.WebSockets;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Zoomex;

/// <summary>
/// Zoomex publishes market data without credentials.
/// </summary>
[TestClass]
public class ZoomexMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(20000, CooperativeCancellation = true)]
	public async Task PublicCandleHistoryDoesNotRequireWebSocket()
	{
		var adapter = (ZoomexMessageAdapter)CreateAdapter();
		adapter.WebSocketEndpoint = "wss://127.0.0.1:1";
		adapter.ReConnectionSettings.ReAttemptCount = 0;
		adapter.HeartbeatInterval = TimeSpan.Zero;
		await using var harness = new MarketDataTestHarness(adapter);
		var timeout = TimeSpan.FromSeconds(8);
		await harness.ConnectAsync(timeout, CancellationToken);

		var instruments = await harness.LookupSecuritiesAsync(new() { SecurityId = TestSecurityId },
			1, timeout, CancellationToken);
		AreEqual(1, instruments.Securities.Length);
		AreEqual(TestSecurityId, instruments.Securities[0].SecurityId);

		var dataType = TimeSpan.FromMinutes(1).TimeFrame();
		var to = DateTime.UtcNow.AddMinutes(-2);
		var candles = await harness.SubscribeAsync<TimeFrameCandleMessage>(dataType, TestSecurityId,
			message =>
			{
				message.From = to.AddMinutes(-10);
				message.To = to;
				message.Count = 1;
			}, null, timeout, CancellationToken);
		CheckData(candles, "closed candle from REST");
		AreEqual(CandleStates.Finished, candles.Data.State);
		IsTrue(candles.Data.ClosePrice > 0);

		var realtime = await harness.SubscribeAsync<TimeFrameCandleMessage>(dataType, TestSecurityId,
			null, null, timeout, CancellationToken);
		IsTrue(realtime.Error is WebSocketException, realtime.Error?.Message);
		IsNull(realtime.Data);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ZoomexMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [ZoomexSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Zoomex,
	};
}
