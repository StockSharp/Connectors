namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Mexc;

/// <summary>
/// Mexc publishes market data without credentials.
/// </summary>
[TestClass]
public class MexcMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new MexcMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [MexcSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Mexc,
	};
}
