namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitmex;
using StockSharp.Messages;

/// <summary>
/// Bitmex publishes market data without credentials.
/// </summary>
[TestClass]
public class BitmexMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitmexMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "XBTUSD",
		BoardCode = BoardCodes.Bitmex,
	};
}
