namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Reflection;
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
[DoNotParallelize]
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
		var previous = Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS");
		try
		{
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_TESTS", setting);
			var probe = new AlorHistoryMarketDataTests();
			var skipped = false;
			foreach (var method in probe.GetType().GetMethods().Where(m => m.IsDefined(typeof(TestInitializeAttribute))))
			{
				try
				{
					method.Invoke(probe, null);
				}
				catch (TargetInvocationException ex) when (ex.InnerException is AssertInconclusiveException)
				{
					skipped = true;
				}
			}

			AreEqual(expectedToRun, !skipped);
		}
		finally
		{
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_TESTS", previous);
		}
	}

	[TestMethod]
	[DataRow(typeof(BinanceMarketDataTests), nameof(BinanceMarketDataTests.Securities))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.ASpotInstrumentSaysHowManyDecimalsItsPricesHave))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.Securities))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.Candles))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.ADayOfCandlesFromDaysAgo))]
	[DataRow(typeof(TokocryptoMarketDataTests), nameof(TokocryptoMarketDataTests.PublicWebSocketStreams))]
	[DataRow(typeof(BinanceHistoryTests), nameof(BinanceHistoryTests.BinHistLookupAll))]
	[DataRow(typeof(BinanceHistoryTests), nameof(BinanceHistoryTests.BinHistOrderBookFutCoin))]
	[DataRow(typeof(ByBitHistoryTests), nameof(ByBitHistoryTests.ByBitHistLookupAll))]
	[DataRow(typeof(ByBitHistoryTests), nameof(ByBitHistoryTests.ByBitHistLookupLimit))]
	[Timeout(10000)]
	public void RegionalTestsRequireAdditionalExplicitOptIn(Type fixtureType, string testName)
	{
		var previousLive = Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS");
		var previousRegional = Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_REGIONAL_TESTS");
		try
		{
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_TESTS", "1");
			foreach (var setting in new[] { "", "0", "true", "1" })
			{
				Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_REGIONAL_TESTS", setting);
				var reason = GetInitializationSkipReason(fixtureType, testName);

				AreEqual(setting == "1", reason is null, $"Regional setting '{setting}' was not respected.");
				if (reason is not null)
					IsTrue(reason.Contains("STOCKSHARP_LIVE_REGIONAL_TESTS=1"), reason);
			}
		}
		finally
		{
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_TESTS", previousLive);
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_REGIONAL_TESTS", previousRegional);
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
	[DataRow(typeof(KucoinMarketDataTests), nameof(KucoinMarketDataTests.Securities))]
	[DataRow(typeof(BYDFiMarketDataTests), nameof(BYDFiMarketDataTests.Securities))]
	[DataRow(typeof(MoexISSHistoryTests), nameof(MoexISSHistoryTests.MoexIssDivSber))]
	[DataRow(typeof(AlorHistoryMarketDataTests), nameof(AlorHistoryMarketDataTests.Securities))]
	[Timeout(10000)]
	public void RegionalOptInDoesNotExcludeOtherChecks(Type fixtureType, string testName)
	{
		var previousLive = Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS");
		var previousRegional = Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_REGIONAL_TESTS");
		try
		{
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_TESTS", "1");
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_REGIONAL_TESTS", null);

			IsNull(GetInitializationSkipReason(fixtureType, testName));
		}
		finally
		{
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_TESTS", previousLive);
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_REGIONAL_TESTS", previousRegional);
		}
	}

	[TestMethod]
	[DataRow(typeof(BinanceMarketDataTests), nameof(BinanceMarketDataTests.Securities))]
	[DataRow(typeof(ByBitMarketDataTests), nameof(ByBitMarketDataTests.Securities))]
	[DataRow(typeof(TokocryptoMarketDataTests), nameof(TokocryptoMarketDataTests.PublicWebSocketStreams))]
	[Timeout(10000)]
	public void RegionalOptInStillRequiresGeneralLiveAccess(Type fixtureType, string testName)
	{
		var previousLive = Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS");
		var previousRegional = Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_REGIONAL_TESTS");
		try
		{
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_TESTS", "0");
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_REGIONAL_TESTS", "1");

			var reason = GetInitializationSkipReason(fixtureType, testName);

			IsNotNull(reason);
			IsTrue(reason.Contains("STOCKSHARP_LIVE_TESTS=1"), reason);
		}
		finally
		{
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_TESTS", previousLive);
			Environment.SetEnvironmentVariable("STOCKSHARP_LIVE_REGIONAL_TESTS", previousRegional);
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
		var previousToken = Environment.GetEnvironmentVariable("COINMARKETCAP_TOKEN");
		var previousKeyless = Environment.GetEnvironmentVariable("COINMARKETCAP_KEYLESS_TESTS");
		CoinMarketCapMessageAdapter adapter = null;
		try
		{
			Environment.SetEnvironmentVariable("COINMARKETCAP_TOKEN", token);
			Environment.SetEnvironmentVariable("COINMARKETCAP_KEYLESS_TESTS", keylessSetting);
			string reason = null;
			try
			{
				adapter = (CoinMarketCapMessageAdapter)typeof(CoinMarketCapMarketDataTests)
					.GetMethod("CreateAdapter", BindingFlags.Instance | BindingFlags.NonPublic)
					.Invoke(new CoinMarketCapMarketDataTests(), null);
			}
			catch (TargetInvocationException error) when (error.InnerException is AssertInconclusiveException skipped)
			{
				reason = skipped.Message;
			}

			AreEqual(expectedToRun, reason is null);
			if (expectedToRun)
				AreEqual(token.IsEmpty() ? CoinMarketCapAccessModes.Keyless : CoinMarketCapAccessModes.ApiKey, adapter.AccessMode);
			else
			{
				IsTrue(reason.Contains("COINMARKETCAP_TOKEN"), reason);
				IsTrue(reason.Contains("COINMARKETCAP_KEYLESS_TESTS=1"), reason);
			}
		}
		finally
		{
			adapter?.Dispose();
			Environment.SetEnvironmentVariable("COINMARKETCAP_TOKEN", previousToken);
			Environment.SetEnvironmentVariable("COINMARKETCAP_KEYLESS_TESTS", previousKeyless);
		}
	}

	private static string GetInitializationSkipReason(Type fixtureType, string testName)
	{
		var context = new Mock<TestContext>();
		context.SetupGet(value => value.TestName).Returns(testName);
		var fixture = (BaseTestClass)Activator.CreateInstance(fixtureType);
		fixture.TestContext = context.Object;
		foreach (var method in fixtureType.GetMethods().Where(method => method.IsDefined(typeof(TestInitializeAttribute))))
		{
			try
			{
				method.Invoke(fixture, null);
			}
			catch (TargetInvocationException error) when (error.InnerException is AssertInconclusiveException skipped)
			{
				return skipped.Message;
			}
		}
		return null;
	}
}
