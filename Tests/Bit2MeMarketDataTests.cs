namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bit2Me;
using StockSharp.Messages;

/// <summary>
/// Bit2Me publishes spot market data without credentials.
/// </summary>
[TestClass]
public class Bit2MeMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new Bit2MeMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/EUR",
		BoardCode = BoardCodes.Bit2Me,
	};
}
