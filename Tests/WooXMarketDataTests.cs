namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.WooX;

/// <summary>
/// WooX publishes market data without credentials.
/// </summary>
[TestClass]
public class WooXMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new WooXMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SPOT_BTC_USDT",
		BoardCode = BoardCodes.WooX,
	};
}
