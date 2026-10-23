namespace StockSharp.Connectors.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;
using Moq.Protected;

using StockSharp.HitBtc;
using StockSharp.HitBtc.Native;
using StockSharp.Messages;

/// <summary>
/// Tests for the HitBtc connector.
/// </summary>
[TestClass]
public class HitBtcTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandleHistoryIncludesTradesBeforeAQuietPeriod()
	{
		var fixture = new Mock<HitBtcTests> { CallBase = true };
		fixture.Protected().Setup<MessageAdapter>("CreateAdapter").Returns(new QuietMarketAdapter());
		fixture.Protected().SetupGet<TimeSpan>("DataTimeout").Returns(TimeSpan.FromSeconds(1));
		fixture.Object.TestContext = TestContext;

		await fixture.Object.Candles();
	}

	private sealed class QuietMarketAdapter : HistoricalMessageAdapter
	{
		public QuietMarketAdapter() : base(new IncrementalIdGenerator())
		{
			this.AddMarketDataSupport();
			this.AddSupportedCandleTimeFrames([TimeSpan.FromMinutes(1)]);
		}

		protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		{
			await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
			var openTime = DateTime.UtcNow.AddHours(-6);
			if (message.From <= openTime && message.To >= openTime)
				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = message.TransactionId,
					SecurityId = message.SecurityId,
					OpenTime = openTime,
					DataType = message.DataType2,
					OpenPrice = 7m,
					HighPrice = 7.1m,
					LowPrice = 6.9m,
					ClosePrice = 7m,
				}, cancellationToken);
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
		}
	}

	[TestMethod]
	[DataRow("trades", true, 100)]
	[DataRow("trades", false, null)]
	[DataRow("ticker/1s", true, null)]
	[DataRow("orderbook/full", true, null)]
	public void TradeSubscriptionsRequestAnInitialSnapshot(string channel, bool subscribe, int? expectedLimit)
	{
		var request = HitBtcSocketClient.CreatePublicSubscription(channel, "UNIUSDT", subscribe);

		AreEqual(1, request.Symbols.Length);
		AreEqual("UNIUSDT", request.Symbols[0]);
		AreEqual(expectedLimit, request.Limit);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new HitBtcMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "UNI/USDT",
		BoardCode = BoardCodes.HitBtc,
	};

	/// <inheritdoc />
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(90);

	/// <inheritdoc />
	/// <remarks>HitBTC only publishes candles for intervals containing trades.</remarks>
	protected override int CandlesDepth => 1440;
}
