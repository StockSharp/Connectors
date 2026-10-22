namespace StockSharp.Connectors.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;
using Moq.Protected;

using StockSharp.Messages;
using StockSharp.StandX;

/// <summary>
/// StandX publishes market data without credentials.
/// </summary>
[TestClass]
public class StandXMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override int CandlesDepth => 1440;

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandleHistoryIncludesTheLatestBarsAfterAGapInTrading()
	{
		var fixture = new Mock<StandXMarketDataTests> { CallBase = true };
		fixture.Protected().Setup<MessageAdapter>("CreateAdapter").Returns(new QuietMarketAdapter());
		fixture.Protected().SetupGet<TimeSpan>("DataTimeout").Returns(TimeSpan.FromMilliseconds(100));
		fixture.Object.TestContext = TestContext;

		await fixture.Object.Candles();
	}

	private sealed class QuietMarketAdapter : HistoricalMessageAdapter
	{
		public QuietMarketAdapter() : base(new IncrementalIdGenerator())
		{
			this.AddMarketDataSupport();
			this.AddSupportedCandleTimeFrames([TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)]);
		}

		protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		{
			await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
			var openTime = DateTime.UtcNow.AddHours(-3);
			if (message.From <= openTime && message.To >= openTime)
				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = message.TransactionId,
					SecurityId = message.SecurityId,
					OpenTime = openTime,
					DataType = message.DataType2,
					OpenPrice = 100m,
					HighPrice = 101m,
					LowPrice = 99m,
					ClosePrice = 100m,
				}, cancellationToken);
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new StandXMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USD",
		BoardCode = BoardCodes.StandX,
	};
}
