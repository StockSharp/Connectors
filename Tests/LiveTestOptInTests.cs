namespace StockSharp.Connectors.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;
using Moq.Protected;

using StockSharp.CoinMarketCap;
using StockSharp.Messages;

[TestClass]
[CLSCompliant(false)]
public class LiveTestOptInTests : BaseTestClass
{
	private sealed class CandleAdapter : HistoricalMessageAdapter
	{
		public CandleAdapter() : base(new IncrementalIdGenerator())
		{
			this.AddMarketDataSupport();
			this.AddSupportedCandleTimeFrames([TimeSpan.FromMinutes(1)]);
		}

		protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		{
			await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
			await TimeSpan.FromMilliseconds(1200).Delay(cancellationToken);
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = message.SecurityId,
				DataType = message.DataType2,
				OpenTime = message.From ?? DateTime.UtcNow,
				OpenPrice = 1,
				HighPrice = 1,
				LowPrice = 1,
				ClosePrice = 1,
				State = CandleStates.Finished,
			}, cancellationToken);
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
		}
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandleCheckRunsWhenHeartbeatIsDisabled()
	{
		var adapter = new CandleAdapter();
		var check = new Mock<LiveMarketDataTestBase> { CallBase = true };
		check.Protected().Setup<MessageAdapter>("CreateAdapter").Returns(adapter);
		check.Protected().SetupGet<SecurityId>("TestSecurityId").Returns(new SecurityId { SecurityCode = "TEST", BoardCode = "TEST" });
		check.Object.TestContext = TestContext;

		try
		{
			await check.Object.Candles();
		}
		catch (AssertInconclusiveException ex)
		{
			Fail(ex.Message);
		}
	}

	[TestMethod]
	[DataRow("", false)]
	[DataRow("0", false)]
	[DataRow("1", true)]
	[Timeout(10000)]
	public void LiveTestsHonorExplicitOptIn(string setting, bool expectedToRun)
	{
		var reason = LiveTestAccess.GetLiveSkipReason(setting);

		AreEqual(expectedToRun, reason is null);
		if (reason is not null)
			IsTrue(reason.Contains("STOCKSHARP_LIVE_TESTS=1"), reason);
	}

	[TestMethod]
	[DataRow(typeof(BinanceTests), nameof(BinanceTests.Securities))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.ASpotInstrumentSaysHowManyDecimalsItsPricesHave))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.Securities))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.Candles))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.ADayOfCandlesFromDaysAgo))]
	[DataRow(typeof(TokocryptoTests), nameof(TokocryptoTests.PublicWebSocketStreams))]
	[DataRow(typeof(BinanceHistoryTests), nameof(BinanceHistoryTests.BinHistLookupAll))]
	[DataRow(typeof(BinanceHistoryTests), nameof(BinanceHistoryTests.BinHistOrderBookFutCoin))]
	[DataRow(typeof(ByBitHistoryTests), nameof(ByBitHistoryTests.ByBitHistLookupAll))]
	[DataRow(typeof(ByBitHistoryTests), nameof(ByBitHistoryTests.ByBitHistLookupLimit))]
	[DataRow(typeof(CowProtocolMarketDataTests), nameof(CowProtocolMarketDataTests.Securities))]
	[DataRow(typeof(CowProtocolMarketDataTests), nameof(CowProtocolMarketDataTests.Level1))]
	[DataRow(typeof(CowProtocolMarketDataTests), nameof(CowProtocolMarketDataTests.OrderBook))]
	[Timeout(10000)]
	public void RegionalTestsRequireAdditionalExplicitOptIn(Type fixtureType, string testName)
	{
		foreach (var setting in new[] { "", "0", "true", "1" })
		{
			var reason = LiveTestAccess.GetMarketDataSkipReason(fixtureType, testName, "1", setting);

			AreEqual(setting == "1", reason is null, $"Regional setting '{setting}' was not respected.");
			if (reason is not null)
				IsTrue(reason.Contains("STOCKSHARP_LIVE_REGIONAL_TESTS=1"), reason);
		}
	}

	[TestMethod]
	[DataRow(typeof(BinanceHistoryTests), nameof(BinanceHistoryTests.BinHistLookupLimit))]
	[DataRow(typeof(BinanceHistoryTests), nameof(BinanceHistoryTests.BinHistCandlesSpot))]
	[DataRow(typeof(ByBitHistoryTests), nameof(ByBitHistoryTests.ByBitHistTicksSpot))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.Level1))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.OrderBook))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.Ticks))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.ASessionLetGoAfterADisconnectEndsWithoutErrors))]
	[DataRow(typeof(KucoinTests), nameof(KucoinTests.Securities))]
	[DataRow(typeof(BYDFiMarketDataTests), nameof(BYDFiMarketDataTests.Securities))]
	[DataRow(typeof(MoexISSTests), nameof(MoexISSTests.MoexIssLookupSber))]
	[DataRow(typeof(AlorHistoryTests), nameof(AlorHistoryTests.Securities))]
	[Timeout(10000)]
	public void RegionalOptInDoesNotExcludeOtherChecks(Type fixtureType, string testName)
	{
		IsNull(LiveTestAccess.GetMarketDataSkipReason(fixtureType, testName, "1", null));
	}

	[TestMethod]
	[DataRow(typeof(BinanceTests), nameof(BinanceTests.Securities))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.Securities))]
	[DataRow(typeof(TokocryptoTests), nameof(TokocryptoTests.PublicWebSocketStreams))]
	[Timeout(10000)]
	public void RegionalOptInStillRequiresGeneralLiveAccess(Type fixtureType, string testName)
	{
		var reason = LiveTestAccess.GetMarketDataSkipReason(fixtureType, testName, "0", "1");

		IsNotNull(reason);
		IsTrue(reason.Contains("STOCKSHARP_LIVE_TESTS=1"), reason);
	}

	[TestMethod]
	[DataRow(typeof(BinanceTests), nameof(BinanceTests.Spot_AnUpdateThatStartsWhereTheBookEnds_IsApplied))]
	[DataRow(typeof(BackpackTests), nameof(BackpackTests.MarketListAcceptsIndexAlongsideCryptoAndStocks))]
	[DataRow(typeof(BinanceHistoryTests), nameof(BinanceHistoryTests.SpotLookupUsesPublicMarketDataEndpoint))]
	public void OrdinaryTestsIgnoreLiveAccessSettings(Type fixtureType, string testName)
	{
		foreach (var live in new[] { "", "0", "1" })
			foreach (var regional in new[] { "", "0", "1" })
				IsNull(LiveTestAccess.GetMarketDataSkipReason(fixtureType, testName, live, regional));
	}

	[TestMethod]
	[DataRow("", false)]
	[DataRow("0", false)]
	[DataRow("true", false)]
	[DataRow("1", true)]
	public void UnavailableVenueApisRequireTheirOwnExplicitOptIn(string setting, bool expectedToRun)
	{
		if (expectedToRun)
		{
			using var bydfi = BYDFiMarketDataTests.CreateAdapter(setting);
			using var gmtrade = GMTradeMarketDataTests.CreateAdapter(setting);
			IsNotNull(bydfi);
			IsNotNull(gmtrade);
		}
		else
		{
			var bydfi = Throws<AssertInconclusiveException>(() => BYDFiMarketDataTests.CreateAdapter(setting));
			IsTrue(bydfi.Message.Contains("STOCKSHARP_LIVE_BYDFI_TESTS=1"));
			var gmtrade = Throws<AssertInconclusiveException>(() => GMTradeMarketDataTests.CreateAdapter(setting));
			IsTrue(gmtrade.Message.Contains("STOCKSHARP_LIVE_GMTRADE_TESTS=1"));
		}
	}

	[TestMethod]
	[DataRow("", "", false)]
	[DataRow("", "0", false)]
	[DataRow("", "true", false)]
	[DataRow("", "1", true)]
	[DataRow("test-key", "", true)]
	[Timeout(10000)]
	public void CoinMarketCapRequiresAKeyOrExplicitKeylessOptIn(string token, string keylessSetting, bool expectedToRun)
	{
		if (expectedToRun)
		{
			using var adapter = CoinMarketCapMarketDataTests.CreateAdapter(token, keylessSetting);
			AreEqual(token.IsEmpty() ? CoinMarketCapAccessModes.Keyless : CoinMarketCapAccessModes.ApiKey, adapter.AccessMode);
		}
		else
		{
			var reason = Throws<AssertInconclusiveException>(() => CoinMarketCapMarketDataTests.CreateAdapter(token, keylessSetting)).Message;
			IsTrue(reason.Contains("COINMARKETCAP_TOKEN"), reason);
			IsTrue(reason.Contains("COINMARKETCAP_KEYLESS_TESTS=1"), reason);
		}
	}

	[TestMethod]
	[DataRow("1", "0", "", false)]
	[DataRow("1", "0", "0", false)]
	[DataRow("1", "0", "true", false)]
	[DataRow("1", "0", "1", true)]
	[DataRow("1", "1", "0", false)]
	[DataRow("1", "1", "1", true)]
	[DataRow("0", "1", "1", false)]
	[Timeout(10000)]
	public void MoexDividendHistoryRequiresItsOwnExplicitOptIn(string live, string regional, string dividends, bool expectedToRun)
	{
		const string testName = nameof(MoexISSTests.MoexIssDivSber);
		IsNull(LiveTestAccess.GetRegionalSkipReason(typeof(MoexISSTests), testName, regional));

		var reason = LiveTestAccess.GetMoexDividendSkipReason(testName, live, dividends);

		AreEqual(expectedToRun, reason is null);
		if (reason is not null)
			IsTrue(reason.Contains(live == "1" ? "STOCKSHARP_LIVE_MOEX_DIVIDENDS_TESTS=1" : "STOCKSHARP_LIVE_TESTS=1"), reason);
	}

	[TestMethod]
	[DataRow(nameof(MoexISSTests.MoexIssLookupAll))]
	[DataRow(nameof(MoexISSTests.MoexIssLookupLimit))]
	[DataRow(nameof(MoexISSTests.MoexIssLookupSber))]
	[DataRow(nameof(MoexISSTests.MoexIssLookupSberLimit))]
	[DataRow(nameof(MoexISSTests.MoexIssCandlesSber))]
	[Timeout(10000)]
	public void MoexDividendOptInDoesNotExcludePublicIssHistory(string testName)
	{
		foreach (var setting in new[] { "", "0", "1" })
			IsNull(LiveTestAccess.GetMoexDividendSkipReason(testName, "1", setting));
	}
}
