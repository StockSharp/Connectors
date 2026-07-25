namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Aster;
using StockSharp.Messages;

/// <summary>
/// Aster publishes market data without credentials.
/// </summary>
[TestClass]
public class AsterMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new AsterMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [AsterSections.Derivatives],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.AsterDerivatives,
	};
}
