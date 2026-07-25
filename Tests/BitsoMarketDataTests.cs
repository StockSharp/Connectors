namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitso;
using StockSharp.Messages;

/// <summary>
/// Bitso publishes market data without credentials.
/// </summary>
[TestClass]
public class BitsoMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitsoMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_MXN",
		BoardCode = BoardCodes.Bitso,
	};
}
