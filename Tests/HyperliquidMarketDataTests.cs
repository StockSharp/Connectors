namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Hyperliquid;
using StockSharp.Messages;

/// <summary>
/// Hyperliquid publishes market data without credentials.
/// </summary>
[TestClass]
public class HyperliquidMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new HyperliquidMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [HyperliquidSections.Derivatives],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC",
		BoardCode = BoardCodes.HyperliquidDerivatives,
	};
}
