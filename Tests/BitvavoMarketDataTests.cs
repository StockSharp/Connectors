namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitvavo;
using StockSharp.Messages;

/// <summary>
/// Bitvavo publishes market data without credentials.
/// </summary>
[TestClass]
public class BitvavoMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitvavoMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-EUR",
		BoardCode = BoardCodes.Bitvavo,
	};
}
