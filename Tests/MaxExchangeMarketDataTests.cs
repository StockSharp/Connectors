namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.ComponentModel;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.MaxExchange;
using StockSharp.MaxExchange.Native;
using StockSharp.Messages;

/// <summary>
/// MAX Exchange publishes spot market data without credentials.
/// </summary>
[TestClass]
public class MaxExchangeMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new MaxExchangeMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/TWD",
		BoardCode = BoardCodes.MaxExchange,
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
		var client = new MaxExchangeWsClient(
			"wss://max-stream.maicoin.com/ws",
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
				"btctwd", CancellationToken);
			await client.SubscribeOrderBookAsync(
				"btctwd", 5, CancellationToken);
			await client.SubscribeTradesAsync(
				"btctwd", CancellationToken);
			await client.SubscribeKlineAsync(
				"btctwd", "1m", CancellationToken);

			var streams = Task.WhenAll(
				ticker.Task, book.Task, trade.Task, candle.Task);
			var timeout = Task.Delay(
				TimeSpan.FromSeconds(30), CancellationToken);
			var completed = await Task.WhenAny(
				streams, error.Task, timeout);

			if (completed == error.Task)
				throw await error.Task;
			if (completed != streams)
				Fail("MAX Exchange public WebSocket streams timed out.");
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
