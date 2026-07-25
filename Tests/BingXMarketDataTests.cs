namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BingX;
using StockSharp.Messages;

/// <summary>
/// BingX publishes market data without credentials.
/// </summary>
[TestClass]
public class BingXMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BingXMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [BingXSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USDT",
		BoardCode = BoardCodes.BingX,
	};
}
