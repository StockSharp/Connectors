namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinJar;
using StockSharp.Messages;

/// <summary>
/// CoinJar publishes market data without credentials.
/// </summary>
[TestClass]
public class CoinJarMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[TestCategory("Integration")]
	[Timeout(30000, CooperativeCancellation = true)]
	public async Task PublicOrderBookFallsBackToRestWhenWebSocketIsUnavailable(bool stallHandshake)
	{
		var timeout = TimeSpan.FromSeconds(8);
		using var listener = stallHandshake ? new TcpListener(IPAddress.Loopback, 0) : null;
		listener?.Start();
		var port = listener?.LocalEndpoint is IPEndPoint endpoint ? endpoint.Port : 1;
		var adapter = new CoinJarMessageAdapter(new IncrementalIdGenerator())
		{
			WebSocketEndpoint = $"wss://127.0.0.1:{port}/socket/websocket",
			HeartbeatInterval = TimeSpan.Zero,
		};
		adapter.ReConnectionSettings.ReAttemptCount = 0;
		await using var harness = new MarketDataTestHarness(adapter);
		await harness.ConnectAsync(timeout, CancellationToken);
		var result = await harness.LookupSecuritiesAsync(new() { SecurityId = TestSecurityId }, 1, timeout, CancellationToken);
		AreEqual(1, result.Securities.Length);
		AreEqual(TestSecurityId, result.Securities[0].SecurityId);

		var bookId = adapter.TransactionIdGenerator.GetNextId();
		var reader = harness.CreateReader();
		await adapter.SendInMessageAsync(new MarketDataMessage
		{
			TransactionId = bookId,
			IsSubscribe = true,
			SecurityId = TestSecurityId,
			DataType2 = DataType.MarketDepth,
			MaxDepth = 1,
		}, CancellationToken);
		var book = await reader.WaitAsync<QuoteChangeMessage>(message =>
			message.OriginalTransactionId == bookId, timeout, CancellationToken);
		AreEqual(1, book.Bids.Length);
		AreEqual(1, book.Asks.Length);
		IsTrue(book.Bids[0].Price > 0);
		IsTrue(book.Asks[0].Price > 0);

		await adapter.SendInMessageAsync(new TimeMessage(), CancellationToken);
		var update = await reader.WaitAsync<QuoteChangeMessage>(message =>
			message.OriginalTransactionId == bookId, timeout, CancellationToken);
		AreEqual(QuoteChangeStates.SnapshotComplete, update.State);
		AreEqual(1, update.Bids.Length);
		AreEqual(1, update.Asks.Length);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CoinJarMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCAUD",
		BoardCode = BoardCodes.CoinJar,
	};
}
