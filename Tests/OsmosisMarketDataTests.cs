namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Osmosis;

/// <summary>
/// Osmosis publishes market data without credentials.
/// </summary>
[TestClass]
public class OsmosisMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new OsmosisMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "OSMO-USDC",
		BoardCode = BoardCodes.Osmosis,
	};

	// ticks are on-chain swaps read from the CometBFT event stream, and a single Osmosis pair
	// is swapped only about once or twice a minute - the whole chain emits around ten
	// token_swapped events per minute - so no bounded wait can rely on one arriving
	/// <inheritdoc />
	protected override DataType[] SkippedDataTypes => [DataType.Ticks];
}
