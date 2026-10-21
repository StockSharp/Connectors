namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Rss;

/// <summary>
/// Tests for the Rss connector.
/// </summary>
[TestClass]
public class RssTests : HistoryTestBase
{
	[TestMethod]
	[DataRow(RssAddresses.Reuters, "", false)]
	[DataRow(RssAddresses.Reuters, "0", false)]
	[DataRow(RssAddresses.Reuters, "true", false)]
	[DataRow(RssAddresses.Reuters, "1", true)]
	[DataRow(RssAddresses.Moex, "0", true)]
	[DataRow(RssAddresses.MarketWatch, "0", true)]
	[DataRow(RssAddresses.SmartLab, "0", true)]
	public void ReutersRequiresExplicitAccessWithoutExcludingOtherFeeds(string address, string setting, bool expectedToRun)
	{
		var reason = RssTests.GetFeedSkipReason(address, setting);

		AreEqual(expectedToRun, reason is null);
		if (reason is not null)
			IsTrue(reason.Contains("STOCKSHARP_LIVE_REUTERS_RSS_TESTS=1"), reason);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task FeedRequestsIdentifyTheConnectorAndAcceptRss()
	{
		var now = DateTime.UtcNow;
		await using var server = new HistoryHttpServer(() =>
			$"<rss version=\"2.0\"><channel><title>Feed</title><link>https://example.test/</link><description>News</description><item><title>News</title><guid>one</guid><pubDate>{now:R}</pubDate></item></channel></rss>", CancellationToken);
		var adapter = CreateAdapter<RssMessageAdapter>();
		adapter.FindAdapter<RssMessageAdapter>().Address = server.Address.ToString();
		var subscription = new MarketDataMessage
		{
			IsSubscribe = true,
			DataType2 = DataType.News,
			From = now.AddMinutes(-1),
			To = now.AddMinutes(1),
		};
		var count = 0;
		await foreach (var message in Download<NewsMessage>(adapter, subscription).WithCancellation(CancellationToken))
		{
			AreEqual("News", message.Headline);
			count++;
		}

		AreEqual(1, count);
		AreEqual("StockSharp-RSS-Connector/1.0", server.RequestHeaders["User-Agent"]);
		IsTrue(server.RequestHeaders.TryGetValue("Accept", out var accept) && accept.Contains("application/rss+xml"));
	}

	[TestMethod]
	[TestCategory("Integration")]
	[DynamicData(nameof(RssFeeds))]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task Rss(string address)
	{
		SkipUnlessLive();
		if (GetFeedSkipReason(address, Env("STOCKSHARP_LIVE_REUTERS_RSS_TESTS")) is string reason)
			Inconclusive(reason);

		var adapter = CreateAdapter<RssMessageAdapter>();
		adapter.FindAdapter<RssMessageAdapter>().Address = address;

		var now = DateTime.UtcNow;

		var mdMsg = new MarketDataMessage
		{
			IsSubscribe = true,
			DataType2 = DataType.News,
			From = now.AddDays(-90),
			To = now,
		};

		var counter = 0;
		await foreach (var _ in Download<NewsMessage>(adapter, mdMsg).WithCancellation(CancellationToken))
			counter++;
		IsGreater(counter, 0, $"No news from {address}.");
	}

	public static IEnumerable<object[]> RssFeeds
		=> RssAddresses.All.Select(address => new object[] { address });

	internal static string GetFeedSkipReason(string address, string setting)
		=> address == RssAddresses.Reuters && !setting.EqualsIgnoreCase("1")
			? "Set STOCKSHARP_LIVE_REUTERS_RSS_TESTS=1 when this runner has programmatic access to the Thomson Reuters RSS feed."
			: null;
}
