namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitmart;

[TestClass]
public class BitmartRegressionTests : BaseTestClass
{
	[TestMethod]
	public void DefaultSpotUsesSpotRestHost()
	{
		using var adapter = new BitmartMessageAdapter(new IncrementalIdGenerator());

		AreEqual(BitmartSections.Spot, adapter.Section);
		AreEqual("api-cloud.bitmart.com", adapter.Address);
	}

	[TestMethod]
	public void ChangingSectionSelectsItsRestHost()
	{
		using var adapter = new BitmartMessageAdapter(new IncrementalIdGenerator());

		adapter.Section = BitmartSections.Futures;
		AreEqual("api-cloud-v2.bitmart.com", adapter.Address);

		adapter.Section = BitmartSections.Spot;
		AreEqual("api-cloud.bitmart.com", adapter.Address);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task SettingsRoundTripKeepsCustomRestHost(bool futures)
	{
		using var source = new BitmartMessageAdapter(new IncrementalIdGenerator())
		{
			Section = futures ? BitmartSections.Futures : BitmartSections.Spot,
			Address = "api.example.test",
		};
		var storage = new SettingsStorage();
		await source.SaveAsync(storage, CancellationToken);

		using var target = new BitmartMessageAdapter(new IncrementalIdGenerator());
		await target.LoadAsync(storage, CancellationToken);

		AreEqual(source.Section, target.Section);
		AreEqual("api.example.test", target.Address);
	}
}
