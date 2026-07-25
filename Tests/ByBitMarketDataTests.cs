namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.ByBit;
using StockSharp.Messages;

/// <summary>
/// ByBit publishes market data without credentials.
/// </summary>
[TestClass]
public class ByBitMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ByBitMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [ByBitSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.ByBit,
	};
}
