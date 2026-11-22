namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.DydxChain;
using StockSharp.Messages;

/// <summary>
/// DydxChain publishes market data without credentials.
/// </summary>
[TestClass]
[RegionalLiveTests] // The public indexer accepts the same handshake locally and replies with HTTP 426 on the hosted runner.
public class DydxChainMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new DydxChainMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USD",
		BoardCode = BoardCodes.DydxChain,
	};
}
