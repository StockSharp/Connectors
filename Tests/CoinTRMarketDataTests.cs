namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.ComponentModel;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinTR;
using StockSharp.CoinTR.Native;
using StockSharp.Messages;

/// <summary>
/// CoinTR publishes spot market data without credentials.
/// </summary>
[TestClass]
public class CoinTRMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new CoinTRMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.CoinTR,
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
		var client = new CoinTRWsClient(
			"wss://ws.cointr.com/v2/ws/public",
			"wss://ws.cointr.com/v2/ws/private",
			default,
			default,
			default,
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
		client.TradeReceived += (_, _) =>
		{
			trade.TrySetResult();
			return default;
		};
		client.CandleReceived += (_, _, _, _) =>
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
			await client.SubscribeTickerAsync("BTCUSDT",
				CancellationToken);
			await client.SubscribeOrderBookAsync("BTCUSDT",
				CancellationToken);
			await client.SubscribeTradesAsync("BTCUSDT",
				CancellationToken);
			await client.SubscribeCandlesAsync("BTCUSDT", "1m",
				CancellationToken);

			var streams = Task.WhenAll(
				ticker.Task, book.Task, trade.Task, candle.Task);
			var timeout = Task.Delay(
				TimeSpan.FromSeconds(30), CancellationToken);
			var completed = await Task.WhenAny(
				streams, error.Task, timeout);

			if (completed == error.Task)
				throw await error.Task;
			if (completed != streams)
				Fail("CoinTR public WebSocket streams timed out.");
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
