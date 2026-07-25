namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Ligther;
using StockSharp.Messages;

/// <summary>
/// Ligther publishes market data without credentials.
/// </summary>
[TestClass]
public class LigtherMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new LigtherMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [LigtherSections.Derivatives],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC",
		BoardCode = BoardCodes.LigtherDerivatives,
	};
}
