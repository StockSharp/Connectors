namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Poloniex;
using StockSharp.Poloniex.Native;
using StockSharp.Poloniex.Native.Model;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Poloniex.</summary>
[TestClass]
public class PoloniexTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task TicksProbeReadsRecentTradesWithoutWaitingForANewTrade()
	{
		using var adapter = CreateAdapter();
		using var authenticator = new Authenticator(false, null, null);
		using var rest = new PoloniexRestClient("https://api.poloniex.com/", authenticator);
		var tradeTime = DateTime.UtcNow.AddMinutes(-5);
		var requests = 0;
		using var http = new HttpClient(new HistoryHttpHandler(request =>
		{
			AreEqual("/markets/BTC_USDT/trades", request.RequestUri.AbsolutePath);
			IsFalse(request.Headers.Contains("key"));
			requests++;
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent($$"""[{"id":"251667888","price":"80527.04","quantity":"0.032287","takerSide":"buy","createTime":{{(long)tradeTime.ToUnix(false)}}}]"""),
			};
		}));
		HistoryHttpHandler.ReplaceClient(rest, http, "_http");
		typeof(PoloniexMessageAdapter).GetField("_restClient", BindingFlags.Instance | BindingFlags.NonPublic)
			.SetValue(adapter, rest);
		var messages = new List<Message>();
		adapter.NewOutMessageAsync += (message, _) =>
		{
			messages.Add(message);
			return default;
		};
		var subscription = new MarketDataMessage
		{
			TransactionId = 42,
			IsSubscribe = true,
			SecurityId = TestSecurityId,
			DataType2 = DataType.Ticks,
		};
		ConfigureSubscription(subscription);

		await adapter.SendInMessageAsync(subscription, CancellationToken);

		AreEqual(1, requests, "The probe must read public recent trades even when no new trade arrives.");
		var tick = messages.OfType<ExecutionMessage>().Single();
		AreEqual(251667888L, tick.TradeId);
		AreEqual(80527.04m, tick.TradePrice);
		AreEqual(0.032287m, tick.TradeVolume);
		AreEqual(TestSecurityId, tick.SecurityId);
		AreEqual(42L, tick.OriginalTransactionId);
		IsTrue(messages.OfType<SubscriptionFinishedMessage>().Any(message => message.OriginalTransactionId == 42));
	}

	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void RestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var candle = new PoloniexCandle { StartTime = (long)open.ToUnix(false), CloseTime = (long)open.AddMinutes(1).AddMilliseconds(-1).ToUnix(false), Open = 100, High = 110, Low = 90, Close = 105, Quantity = 12.5m };
		var subscription = new MarketDataMessage { DataType2 = TimeSpan.FromMinutes(1).TimeFrame() };

		var message = candle.ToCandleMessage(subscription, open.AddMilliseconds(elapsedMilliseconds));

		AreEqual(expected, message.State);
		AreEqual(open, message.OpenTime);
		AreEqual(105m, message.ClosePrice);
		AreEqual(12.5m, message.TotalVolume);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PoloniexMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.Poloniex,
	};

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 != DataType.Ticks)
			return;

		message.To = DateTime.UtcNow;
		message.From = message.To.Value.AddDays(-1);
	}
}
