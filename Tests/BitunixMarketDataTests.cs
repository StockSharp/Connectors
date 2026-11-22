namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitunix;
using StockSharp.Messages;

/// <summary>
/// Bitunix publishes market data without credentials.
/// </summary>
[TestClass]
public class BitunixMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task FuturesHistoryIncludesCarriedOpensInCandleBounds()
	{
		HistoryHttpServer api = null;
		await using var rest = api = new HistoryHttpServer(() => api.RequestTarget switch
		{
			"/api/v1/futures/market/trading_pairs" =>
				"""{"code":0,"data":[{"symbol":"BTCUSDT"}]}""",
			var path when path.StartsWith("/api/v1/futures/market/kline?", StringComparison.Ordinal) =>
				"""
				{"code":0,"data":[
					{"open":"61046","high":"61044.9","low":"60986","close":"61028","baseVol":"14.485","time":"1795346880000"},
					{"open":"61012.3","high":"61089","low":"61012.5","close":"61081","baseVol":"24.2415","time":"1795347480000"}
				]}
				""",
			var path => throw new InvalidOperationException($"Unexpected Bitunix request '{path}'."),
		}, CancellationToken);
		using var streams = new WebSocketTestServer();
		var adapter = (BitunixMessageAdapter)CreateAdapter();
		adapter.FuturesRestEndpoint = rest.Address.AbsoluteUri;
		adapter.FuturesPublicWsEndpoint = streams.Endpoint;
		adapter.HeartbeatInterval = TimeSpan.Zero;
		await using var harness = new MarketDataTestHarness(adapter);
		var timeout = TimeSpan.FromSeconds(5);
		var connect = harness.ConnectAsync(timeout, CancellationToken);
		using var socket = await streams.AcceptAsync(CancellationToken);
		await connect;

		var reader = harness.CreateReader();
		var transactionId = adapter.TransactionIdGenerator.GetNextId();
		await adapter.SendInMessageAsync(new MarketDataMessage
		{
			TransactionId = transactionId,
			IsSubscribe = true,
			SecurityId = TestSecurityId,
			DataType2 = TimeSpan.FromMinutes(1).TimeFrame(),
			From = new DateTime(2026, 11, 22, 11, 27, 0, DateTimeKind.Utc),
			To = new DateTime(2026, 11, 22, 11, 39, 0, DateTimeKind.Utc),
		}, CancellationToken);
		var first = await reader.WaitAsync<TimeFrameCandleMessage>(
			candle => candle.OriginalTransactionId == transactionId, timeout, CancellationToken);
		var second = await reader.WaitAsync<TimeFrameCandleMessage>(
			candle => candle.OriginalTransactionId == transactionId, timeout, CancellationToken);

		AreEqual(61046m, first.OpenPrice);
		AreEqual(61046m, first.HighPrice);
		AreEqual(60986m, first.LowPrice);
		AreEqual(61028m, first.ClosePrice);
		AreEqual(61012.3m, second.OpenPrice);
		AreEqual(61089m, second.HighPrice);
		AreEqual(61012.3m, second.LowPrice);
		AreEqual(61081m, second.ClosePrice);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitunixMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [BitunixSections.Futures],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.BitunixFutures,
	};
}
