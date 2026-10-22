namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitbank;
using StockSharp.Bitbank.Native;
using StockSharp.Bitbank.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Bitbank publishes market data without credentials.
/// </summary>
[TestClass]
public class BitbankMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task PublicSocketIoTradeUpdatesAreDecoded()
	{
		using var server = new WebSocketTestServer();
		using var client = new PusherClient(server.Endpoint, new Ecng.ComponentModel.WorkingTime());
		var received = new TaskCompletionSource<(string Symbol, Trade Trade)>(TaskCreationOptions.RunContinuationsAsynchronously);
		client.NewTrade += (symbol, trade, _) =>
		{
			received.TrySetResult((symbol, trade));
			return default;
		};
		client.Error += (error, _) =>
		{
			received.TrySetException(error);
			return default;
		};
		var accepting = server.AcceptAsync(CancellationToken);
		var connecting = client.ConnectAsync(CancellationToken).AsTask();
		using var peer = await accepting;
		AreEqual("40", await WebSocketTestServer.ReadTextAsync(peer, CancellationToken));
		await WebSocketTestServer.SendTextAsync(peer,
			"""0{"sid":"engine-session","upgrades":[],"pingInterval":25000,"pingTimeout":20000}""", CancellationToken);
		await WebSocketTestServer.SendTextAsync(peer, """40{"sid":"namespace-session"}""", CancellationToken);
		await connecting;
		await client.SubscribeTradesAsync("btc_jpy", CancellationToken);
		AreEqual("""42["join-room","transactions_btc_jpy"]""", await WebSocketTestServer.ReadTextAsync(peer, CancellationToken));

		await WebSocketTestServer.SendTextAsync(peer,
			"""42["message",{"room_name":"transactions_btc_jpy","message":{"data":{"transactions":[{"transaction_id":123,"side":"buy","price":"12000000","amount":"0.001","executed_at":1704196815000}]}}}]""", CancellationToken);
		var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken);

		AreEqual("btc_jpy", result.Symbol);
		AreEqual(123L, result.Trade.Id);
		AreEqual(12000000m, result.Trade.Price.ToDecimal());
	}

	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void RestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var candle = new Ohlc { Time = (long)open.ToUnix(false), Open = 100, High = 110, Low = 90, Close = 105, Volume = 12.5 };

		var message = candle.ToCandleMessage(default, TimeSpan.FromMinutes(1), 42, open.AddMilliseconds(elapsedMilliseconds));

		AreEqual(expected, message.State);
		AreEqual(open, message.OpenTime);
		AreEqual(105m, message.ClosePrice);
		AreEqual(12.5m, message.TotalVolume);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitbankMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_JPY",
		BoardCode = BoardCodes.Bitbank,
	};

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 != DataType.Ticks)
			return;

		// Use a completed day: today's dated trade endpoint can return 404.
		message.From = DateTime.UtcNow.Date.AddDays(-1);
		message.To = message.From.Value.AddDays(1).AddTicks(-1);
		message.Count = 1;
	}
}
