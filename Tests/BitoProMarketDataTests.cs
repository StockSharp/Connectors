namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.ComponentModel;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BitoPro;
using StockSharp.BitoPro.Native;
using StockSharp.Messages;

/// <summary>
/// BitoPro publishes spot market data without credentials.
/// </summary>
[TestClass]
public class BitoProMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new BitoProMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/TWD",
		BoardCode = BoardCodes.BitoPro,
	};

	[TestMethod]
	[Timeout(120000)]
	public async Task PublicWebSocketStreams()
	{
		var ticker = NewSignal();
		var book = NewSignal();
		var trade = NewSignal();
		var error = new TaskCompletionSource<Exception>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var client = new BitoProWsClient(
			"wss://stream.bitopro.com:443/ws",
			new WorkingTime(),
			0);

		client.TickerReceived += (_, _) =>
		{
			ticker.TrySetResult();
			return default;
		};
		client.OrderBookReceived += (_, _) =>
		{
			book.TrySetResult();
			return default;
		};
		client.TradesReceived += (_, _) =>
		{
			trade.TrySetResult();
			return default;
		};
		client.Error += (exception, _) =>
		{
			error.TrySetResult(exception);
			return default;
		};

		try
		{
			await client.ConnectAsync(CancellationToken);
			await client.SubscribeTickerAsync("BTC_TWD",
				CancellationToken);
			await client.SubscribeOrderBookAsync("BTC_TWD", 5,
				CancellationToken);
			await client.SubscribeTradesAsync("BTC_TWD",
				CancellationToken);

			var streams = Task.WhenAll(
				ticker.Task, book.Task, trade.Task);
			var timeout = Task.Delay(
				TimeSpan.FromSeconds(30), CancellationToken);
			var completed = await Task.WhenAny(
				streams, error.Task, timeout);

			if (completed == error.Task)
				throw await error.Task;
			if (completed != streams)
				Fail("BitoPro public WebSocket streams timed out.");
			await streams;
		}
		finally
		{
			await client.DisconnectAsync(CancellationToken);
			client.Dispose();
		}
	}

	private static TaskCompletionSource NewSignal()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);
}
