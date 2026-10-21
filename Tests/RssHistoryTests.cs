namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Rss;

[TestClass]
public class RssHistoryTests : HistoryTestBase
{
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
