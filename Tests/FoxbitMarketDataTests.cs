namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Foxbit;
using StockSharp.Messages;

/// <summary>
/// Foxbit publishes market data without credentials.
/// </summary>
[TestClass]
public class FoxbitMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new FoxbitMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCBRL",
		BoardCode = BoardCodes.Foxbit,
	};
}
