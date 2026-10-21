namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Paradex;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Paradex.</summary>
[TestClass]
[RegionalLiveTests]
public class ParadexTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ParadexMessageAdapter(new IncrementalIdGenerator())
		{
			Sections = [ParadexSections.Derivatives],
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USD-PERP",
		BoardCode = BoardCodes.ParadexDerivatives,
	};
}
