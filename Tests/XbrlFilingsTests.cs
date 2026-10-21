namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.XbrlFilings;
using StockSharp.Messages;

/// <summary>Anonymous reference data checks for XbrlFilings.</summary>
[TestClass]
public class XbrlFilingsTests : HistoryTestBase
{
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
