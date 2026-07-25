namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bluefin;
using StockSharp.Messages;

/// <summary>
/// Bluefin publishes market data without credentials.
/// </summary>
[TestClass]
public class BluefinMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BluefinMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-PERP",
		BoardCode = BoardCodes.Bluefin,
	};
}
