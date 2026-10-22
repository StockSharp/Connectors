namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.XbrlFilings;
using StockSharp.XbrlFilings.Native;
using StockSharp.Messages;

/// <summary>Anonymous reference data checks for XbrlFilings.</summary>
[TestClass]
public class XbrlFilingsTests : HistoryTestBase
{
	[TestMethod]
	[DataRow("/reports/viewer.html", "https://filings.xbrl.org/reports/viewer.html")]
	[DataRow("reports/viewer.html", "https://filings.xbrl.org/reports/viewer.html")]
	[DataRow("https://filings.xbrl.org/reports/viewer.html", "https://filings.xbrl.org/reports/viewer.html")]
	[DataRow("http://filings.xbrl.org/reports/viewer.html", null)]
	[DataRow("file:///reports/viewer.html", null)]
	public void PublicLinksResolveAsHttpsUrls(string value, string expected)
	{
		AreEqual(expected, XbrlFilingsExtensions.ResolvePublicUrl(new Uri("https://filings.xbrl.org/"), value)?.AbsoluteUri);
	}

	[TestMethod]
	public void FilingNewsFallsBackToTheReportPackage()
	{
		var filing = new XbrlFiling
		{
			Id = "26418",
			Attributes = new() { PeriodEnd = "2025-12-31", PackageUrl = "/reports/annual.xbri" },
		};
		var news = filing.ToNewsMessage(null, 42, new Uri("https://filings.xbrl.org/"));

		AreEqual("https://filings.xbrl.org/reports/annual.xbri", news.Url);
		IsFalse(news.Headline.IsEmpty());
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task Securities()
	{
		var adapter = CreateAdapter<XbrlFilingsMessageAdapter>();
		var lookup = new SecurityLookupMessage
		{
			SecurityId = new() { SecurityCode = "7LTWFZYICNSX8D621K86" },
			Count = 1,
		};
		var count = 0;
		await foreach (var security in DownloadSecurities(adapter, lookup).WithCancellation(CancellationToken))
		{
			AreEqual("7LTWFZYICNSX8D621K86", security.SecurityId.SecurityCode);
			IsFalse(security.Name.IsEmpty());
			count++;
		}
		AreEqual(1, count);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task News()
	{
		var adapter = CreateAdapter<XbrlFilingsMessageAdapter>();
		var subscription = new MarketDataMessage
		{
			IsSubscribe = true,
			DataType2 = DataType.News,
			SecurityId = new() { SecurityCode = "95980020140005693107" },
			Count = 1,
		};
		var count = 0;
		await foreach (var news in Download<NewsMessage>(adapter, subscription).WithCancellation(CancellationToken))
		{
			IsFalse(news.Headline.IsEmpty());
			IsFalse(news.Url.IsEmpty());
			count++;
		}
		AreEqual(1, count);
	}
}
