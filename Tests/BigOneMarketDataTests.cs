namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.ComponentModel;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BigOne;
using StockSharp.BigOne.Native;
using StockSharp.Messages;

/// <summary>
/// BigONE publishes spot and contract market data without credentials.
/// </summary>
[TestClass]
public class BigOneMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new BigOneMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.BigOne,
	};

	[TestMethod]
	[Timeout(120000)]
	public async Task PublicSpotWebSocketStreams()
	{
		var ticker = NewSignal();
		var book = NewSignal();
		var trade = NewSignal();
		var candle = NewSignal();
		var error = new TaskCompletionSource<Exception>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var client = new BigOneSpotWsClient(
			"wss://api.big.one/ws/v2",
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
		client.CandleReceived += (_, _) =>
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
				"BTC-USDT", CancellationToken);
			await client.SubscribeOrderBookAsync(
				"BTC-USDT", CancellationToken);
			await client.SubscribeTradesAsync(
				"BTC-USDT", CancellationToken);
			await client.SubscribeCandlesAsync(
				"BTC-USDT", "MIN1", CancellationToken);

			await AwaitStreamsAsync(
				[ticker.Task, book.Task, trade.Task, candle.Task],
				error.Task, "BigONE spot WebSocket streams");
		}
		finally
		{
			await client.DisconnectAsync(CancellationToken);
			client.Dispose();
		}
	}

	[TestMethod]
	[Timeout(120000)]
	public async Task PublicContractWebSocketStreams()
	{
		var ticker = NewSignal();
		var book = NewSignal();
		var trade = NewSignal();
		var candle = NewSignal();
		var error = new TaskCompletionSource<Exception>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var client = new BigOneContractWsClient(
			"wss://api.big.one/ws/contract/v2",
			"wss://api.big.one/ws/contract/v2/stream",
			null, new WorkingTime(), 0);

		client.InstrumentReceived += (_, _) =>
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
		client.CandlesReceived += (_, _) =>
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
			await client.SubscribeInstrumentAsync(
				"BTCUSD", CancellationToken);
			await client.SubscribeOrderBookAsync(
				"BTCUSD", CancellationToken);
			await client.SubscribeTradesAsync(
				"BTCUSD", CancellationToken);
			await client.SubscribeCandlesAsync(
				"BTCUSD", "1MIN", CancellationToken);

			await AwaitStreamsAsync(
				[ticker.Task, book.Task, trade.Task, candle.Task],
				error.Task, "BigONE contract WebSocket streams");
		}
		finally
		{
			await client.DisconnectAsync(CancellationToken);
			client.Dispose();
		}
	}

	private async Task AwaitStreamsAsync(Task[] tasks,
		Task<Exception> error, string description)
	{
		var streams = Task.WhenAll(tasks);
		var timeout = Task.Delay(
			TimeSpan.FromSeconds(30), CancellationToken);
		var completed = await Task.WhenAny(streams, error, timeout);
		if (completed == error)
			throw await error;
		if (completed != streams)
			Fail($"{description} timed out.");
		await streams;
	}

	private static TaskCompletionSource NewSignal()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);
}
