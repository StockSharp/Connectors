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
		SecurityCode = "RUNE-BTC",
		BoardCode = BoardCodes.THORChain,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Both are rebuilt from Midgard swap actions that carry RUNE on one leg, and
	/// nowadays traffic is almost entirely asset to asset - RUNE shows up only as the
	/// internal or affiliate leg, which the adapter rightly ignores. Over 1000 BTC.BTC
	/// actions (77 hours) only eight swaps were RUNE against BTC, so the window these
	/// checks look at is empty far more often than not. Level1 stays covered, it comes
	/// from executable quotes rather than from history.
	/// </remarks>
	protected override DataType[] SkippedDataTypes =>
	[
		DataType.Ticks,
		.. THORChainMessageAdapter.AllTimeFrames.Select(tf => tf.TimeFrame()),
	];
}
