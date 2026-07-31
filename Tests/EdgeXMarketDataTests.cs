namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.EdgeX;
using StockSharp.Messages;

/// <summary>
/// edgeX publishes market data without credentials.
/// </summary>
[TestClass]
public class EdgeXMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new EdgeXMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [EdgeXSections.Derivatives],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSD",
		BoardCode = BoardCodes.EdgeXDerivatives,
	};
}
