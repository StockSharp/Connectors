namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Balancer;
using StockSharp.Messages;

/// <summary>
/// Balancer publishes market data without credentials.
/// </summary>
[TestClass]
public class BalancerMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BalancerMessageAdapter(new IncrementalIdGenerator())
	{
		MaximumDiscoveredPools = 0,
		Pools = "0x5c6ee304399dbdb9c8ef030ab642b10820db8f56000200000000000000000014|0xba100000625a3754423978a60c9317c58a424e3d|0xc02aaa39b223fe8d0a0e5c4f27ead9083c756cc2|BAL-WETH",
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BAL-WETH",
		BoardCode = BoardCodes.Balancer,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Candles are folded from on-chain swaps, and the pool trades a few dozen times a day,
	/// so a one hundred minute window is regularly empty.
	/// </remarks>
	protected override int CandlesDepth => 4320;
}
