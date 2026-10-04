namespace StockSharp.Connectors.Tests;

using System.Linq;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Algo.Import;
using StockSharp.CSV;
using StockSharp.Messages;

[TestClass]
public class CsvTests : BaseTestClass
{
	private static bool OffersMarketData(CSVMessageAdapter adapter)
		=> adapter.PossibleSupportedMessages.Any(m => m.Type == MessageTypes.MarketData);

	/// <summary>
	/// What the adapter can be asked for follows the import settings it is given: with files to read
	/// it offers market data, with none it does not.
	/// </summary>
	[TestMethod]
	public void MarketDataIsOfferedOnlyWhileThereAreImportSettings()
	{
		var adapter = new CSVMessageAdapter(new IncrementalIdGenerator());

		adapter.Settings = [new ImportSettings { DataType = DataType.Ticks }];
		IsTrue(OffersMarketData(adapter));

		adapter.Settings = [];
		IsFalse(OffersMarketData(adapter));
	}
}
