namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.EsmaFirds;
using StockSharp.Messages;

/// <summary>Anonymous reference data checks for EsmaFirds.</summary>
[TestClass]
[RegionalLiveTests]
public class EsmaFirdsTests : HistoryTestBase
{
	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task Securities()
	{
		var adapter = CreateAdapter<EsmaFirdsMessageAdapter>(a =>
		{
			// Exact reference lookup does not need the registry's expensive active-trading filter.
			a.ActiveOnly = false;
			a.CfiCategories = string.Empty;
		});
		var lookup = new SecurityLookupMessage
		{
			SecurityId = new() { SecurityCode = "DE0005140008" },
			Count = 1,
		};
		var count = 0;
		await foreach (var security in DownloadSecurities(adapter, lookup).WithCancellation(CancellationToken))
		{
			AreEqual("DE0005140008", security.SecurityId.Isin);
			IsFalse(security.Name.IsEmpty());
			count++;
		}
		AreEqual(1, count);
	}
}
