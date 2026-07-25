namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Deriv;
using StockSharp.Messages;

/// <summary>
/// Deriv publishes market data without credentials.
/// </summary>
[TestClass]
public class DerivMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new DerivMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "R_100",
		BoardCode = BoardCodes.Deriv,
	};
}
