namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinsPh;
using StockSharp.Messages;

/// <summary>
/// CoinsPh publishes market data without credentials.
/// </summary>
[TestClass]
public class CoinsPhMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CoinsPhMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCPHP",
		BoardCode = BoardCodes.CoinsPh,
	};
}
