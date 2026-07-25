namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.SynFutures;

/// <summary>
/// SynFutures publishes market data without credentials.
/// </summary>
[TestClass]
public class SynFuturesMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new SynFuturesMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDC",
		BoardCode = BoardCodes.SynFutures,
	};
}
