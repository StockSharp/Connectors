namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitget;
using StockSharp.Messages;

/// <summary>
/// Bitget publishes market data without credentials.
/// </summary>
[TestClass]
public class BitgetMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitgetMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [BitgetSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Bitget,
	};
}
