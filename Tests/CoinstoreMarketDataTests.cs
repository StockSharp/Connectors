namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.ComponentModel;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Coinstore;
using StockSharp.Coinstore.Native;
using StockSharp.Messages;

/// <summary>
/// Coinstore publishes spot market data without credentials.
/// </summary>
[TestClass]
public class CoinstoreMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new CoinstoreMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.Coinstore,
	};

	[TestMethod]
	[Timeout(120000)]
	public async Task PublicWebSocketStreams()
	{
		var ticker = NewSignal();
		var book = NewSignal();
		var trade = NewSignal();
		var candle = NewSignal();
		var error = new TaskCompletionSource<Exception>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var client = new CoinstoreWsClient(
			"wss://ws.coinstore.com/s/ws",
			new WorkingTime(), 0);

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
		client.KlineReceived += (_, _) =>
		{
			candle.TrySetResult();
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
			await client.SubscribeTickerAsync(
				"BTCUSDT", CancellationToken);
			await client.SubscribeOrderBookAsync(
				"BTCUSDT", 5, CancellationToken);
			await client.SubscribeTradesAsync(
				"BTCUSDT", CancellationToken);
			await client.SubscribeKlineAsync(
				"BTCUSDT", "min_1", CancellationToken);

			var streams = Task.WhenAll(
				ticker.Task, book.Task, trade.Task, candle.Task);
			var timeout = Task.Delay(
				TimeSpan.FromSeconds(30), CancellationToken);
			var completed = await Task.WhenAny(
				streams, error.Task, timeout);

			if (completed == error.Task)
				throw await error.Task;
			if (completed != streams)
				Fail(
					"Coinstore public WebSocket streams timed out.");
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
