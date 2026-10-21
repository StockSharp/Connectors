namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Gleif;
using StockSharp.Messages;

/// <summary>Anonymous reference data checks for Gleif.</summary>
[TestClass]
public class GleifTests : HistoryTestBase
{
	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task Securities()
	{
		var adapter = CreateAdapter<GleifMessageAdapter>();
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
}
