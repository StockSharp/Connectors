namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Zoomex;

/// <summary>
/// Zoomex publishes market data without credentials.
/// </summary>
[TestClass]
public class ZoomexMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ZoomexMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [ZoomexSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Zoomex,
	};
}
