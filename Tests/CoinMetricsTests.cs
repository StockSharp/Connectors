namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinMetrics;
using StockSharp.Messages;

/// <summary>Anonymous market reference data from the Coin Metrics Community API.</summary>
[TestClass]
public class CoinMetricsTests : HistoryTestBase
{
	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task Securities()
	{
		var adapter = CreateAdapter<CoinMetricsMessageAdapter>();
		var lookup = new SecurityLookupMessage
		{
			SecurityId = new() { SecurityCode = "coinbase-btc-usd-spot" },
			Count = 1,
		};
		var count = 0;
		await foreach (var security in DownloadSecurities(adapter, lookup).WithCancellation(CancellationToken))
		{
			AreEqual("coinbase-btc-usd-spot", security.SecurityId.SecurityCode);
			IsFalse(security.Name.IsEmpty());
			count++;
		}
		AreEqual(1, count);
	}
}
