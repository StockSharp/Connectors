namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Ourbit;

/// <summary>
/// Ourbit publishes market data without credentials.
/// </summary>
[TestClass]
public class OurbitMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new OurbitMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [OurbitSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Ourbit,
	};
}
