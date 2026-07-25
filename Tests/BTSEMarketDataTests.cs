namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BTSE;
using StockSharp.Messages;

/// <summary>
/// BTSE publishes market data without credentials.
/// </summary>
[TestClass]
public class BTSEMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BTSEMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [BTSESections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USD",
		BoardCode = BoardCodes.Btse,
	};
}
