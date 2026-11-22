namespace StockSharp.Connectors.Tests;

using System.Linq;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.THORChain;

/// <summary>
/// THORChain publishes market data without credentials.
/// </summary>
[TestClass]
public class THORChainMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new THORChainMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "RUNE-ETH",
		BoardCode = BoardCodes.THORChain,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Both are rebuilt from Midgard swap actions that carry RUNE on one leg, and
	/// nowadays traffic is almost entirely asset to asset - RUNE shows up only as the
	/// internal or affiliate leg, which the adapter ignores. The history window often
	/// has no direct RUNE swaps. Level1 exercises executable quotes for the ETH pool.
	/// </remarks>
	protected override DataType[] SkippedDataTypes =>
	[
		DataType.Ticks,
		.. THORChainMessageAdapter.AllTimeFrames.Select(tf => tf.TimeFrame()),
	];
}
