namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Aevo;
using StockSharp.Messages;

/// <summary>
/// Aevo publishes market data without credentials.
/// </summary>
[TestClass]
public class AevoMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new AevoMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "ETH-PERP",
		BoardCode = BoardCodes.Aevo,
	};
}
