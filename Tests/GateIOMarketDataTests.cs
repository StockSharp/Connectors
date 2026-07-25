namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.GateIO;
using StockSharp.Messages;

/// <summary>
/// GateIO publishes market data without credentials.
/// </summary>
[TestClass]
public class GateIOMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GateIOMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [GateIOSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_USDT",
		BoardCode = BoardCodes.GateIO,
	};
}
