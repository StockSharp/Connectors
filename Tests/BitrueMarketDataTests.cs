namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitrue;
using StockSharp.Messages;

/// <summary>
/// Bitrue publishes market data without credentials.
/// </summary>
[TestClass]
public class BitrueMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitrueMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [BitrueSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Bitrue,
	};
}
