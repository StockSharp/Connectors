namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitunix;
using StockSharp.Messages;

/// <summary>
/// Bitunix publishes market data without credentials.
/// </summary>
[TestClass]
public class BitunixMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitunixMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [BitunixSections.Futures],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.BitunixFutures,
	};
}
