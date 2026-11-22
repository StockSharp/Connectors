namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.DydxChain;
using StockSharp.Messages;

/// <summary>
/// DydxChain publishes market data without credentials.
/// </summary>
[TestClass]
public class DydxChainMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task PublicMarketDataFallsBackToRestWhenWebSocketIsRejected()
	{
		var bookRequests = 0;
		var tradeRequests = 0;
		HistoryHttpServer api = null;
		await using var rest = api = new HistoryHttpServer(() => api.RequestTarget switch
		{
			"/" => """{"jsonrpc":"2.0","id":1,"result":{"node_info":{"network":"dydx-mainnet-1"},"sync_info":{"latest_block_height":"100","latest_block_time":"2026-01-02T00:00:00Z"}}}""",
			"/v4/time" => """{"iso":"2026-01-02T00:00:00Z"}""",
			"/v4/height" => """{"height":"101","time":"2026-01-02T00:00:02Z"}""",
			"/v4/perpetualMarkets" => """{"markets":{"BTC-USD":{"ticker":"BTC-USD","clobPairId":"0","tickSize":"0.1","stepSize":"0.001","stepBaseQuantums":1,"subticksPerTick":1}}}""",
			"/v4/orderbooks/perpetualMarket/BTC-USD" => ++bookRequests == 1
				? """{"bids":[{"price":"99","size":"1"}],"asks":[{"price":"101","size":"1"}]}"""
				: """{"bids":[{"price":"100","size":"1"}],"asks":[{"price":"102","size":"1"}]}""",
			var path when path.StartsWith("/v4/trades/perpetualMarket/BTC-USD?", StringComparison.Ordinal) =>
				++tradeRequests == 1
					? """{"trades":[{"id":"first","side":"BUY","price":"100","size":"1","createdAt":"2026-01-02T00:00:00Z"}]}"""
					: """{"trades":[{"id":"second","side":"SELL","price":"101","size":"2","createdAt":"2026-01-02T00:00:01Z"},{"id":"first","side":"BUY","price":"100","size":"1","createdAt":"2026-01-02T00:00:00Z"}]}""",
			var path when path.StartsWith("/v4/candles/perpetualMarkets/BTC-USD?", StringComparison.Ordinal) =>
				"""{"candles":[{"ticker":"BTC-USD","startedAt":"2026-01-01T23:59:00Z","resolution":"1MIN","open":"100","high":"102","low":"99","close":"101","baseTokenVolume":"3"}]}""",
			var path => throw new InvalidOperationException($"Unexpected dYdX request '{path}'."),
		}, CancellationToken);
		await using var socket = new HistoryHttpServer(() => new HttpResponseMessage(HttpStatusCode.UpgradeRequired), CancellationToken);
		var adapter = new DydxChainMessageAdapter(new IncrementalIdGenerator())
		{
			IndexerEndpoint = rest.Address.AbsoluteUri,
			ValidatorEndpoint = rest.Address.AbsoluteUri,
			WebSocketEndpoint = new UriBuilder(socket.Address) { Scheme = "ws" }.Uri.AbsoluteUri,
			HeartbeatInterval = TimeSpan.Zero,
		};
		adapter.ReConnectionSettings.ReAttemptCount = 0;
		await using var harness = new MarketDataTestHarness(adapter);
		var timeout = TimeSpan.FromSeconds(5);
		await harness.ConnectAsync(timeout, CancellationToken);
		var result = await harness.LookupSecuritiesAsync(new(), 5, timeout, CancellationToken);
		AreEqual(1, result.Securities.Length);
		AreEqual(TestSecurityId, result.Securities[0].SecurityId);

		var candles = await harness.SubscribeAsync<TimeFrameCandleMessage>(TimeSpan.FromMinutes(1).TimeFrame(),
			TestSecurityId, message => message.Count = 1, null, timeout, CancellationToken);
		CheckData(candles, "candles");
		AreEqual(101m, candles.Data.ClosePrice);
		AreEqual(0, socket.RequestTimes.Count);

		var bookId = adapter.TransactionIdGenerator.GetNextId();
		var books = harness.CreateReader();
		await adapter.SendInMessageAsync(new MarketDataMessage
		{
			TransactionId = bookId,
			IsSubscribe = true,
			SecurityId = TestSecurityId,
			DataType2 = DataType.MarketDepth,
		}, CancellationToken);
		var book = await books.WaitAsync<QuoteChangeMessage>(message =>
			message.OriginalTransactionId == bookId, timeout, CancellationToken);
		AreEqual(99m, book.Bids[0].Price);

		var tickId = adapter.TransactionIdGenerator.GetNextId();
		var trades = harness.CreateReader();
		await adapter.SendInMessageAsync(new MarketDataMessage
		{
			TransactionId = tickId,
			IsSubscribe = true,
			SecurityId = TestSecurityId,
			DataType2 = DataType.Ticks,
		}, CancellationToken);
		var first = await trades.WaitAsync<ExecutionMessage>(message =>
			message.OriginalTransactionId == tickId, timeout, CancellationToken);
		AreEqual("first", first.TradeStringId);

		await adapter.SendInMessageAsync(new TimeMessage(), CancellationToken);

		var update = await books.WaitAsync<QuoteChangeMessage>(message =>
			message.OriginalTransactionId == bookId, timeout, CancellationToken);
		AreEqual(QuoteChangeStates.SnapshotComplete, update.State);
		AreEqual(100m, update.Bids[0].Price);
		var trade = await trades.WaitAsync<ExecutionMessage>(message =>
			message.OriginalTransactionId == tickId, timeout, CancellationToken);
		AreEqual("second", trade.TradeStringId);
		AreEqual(101m, trade.TradePrice);
		AreEqual(1, socket.RequestTimes.Count);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new DydxChainMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USD",
		BoardCode = BoardCodes.DydxChain,
	};
}
