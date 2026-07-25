namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Drift;
using StockSharp.Messages;

/// <summary>
/// Drift publishes market data without credentials.
/// </summary>
[TestClass]
public class DriftMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new DriftMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SOL-PERP",
		BoardCode = BoardCodes.Drift,
	};
}
