namespace StockSharp.Connectors.Tests;

using System.Linq;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CowProtocol;
using StockSharp.Messages;

/// <summary>
/// CowProtocol publishes market data without credentials.
/// </summary>
[TestClass]
public class CowProtocolMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CowProtocolMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "WETH-USDC",
		BoardCode = BoardCodes.CowProtocol,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Trades and candles are both rebuilt from the settlement contract's <c>Trade</c>
	/// logs, and the default Ethereum endpoint serves <c>eth_getLogs</c> only within
	/// roughly 128 blocks of the head: anything older is answered with
	/// <c>Archive requests require a personal token</c>. A keyed archive node has to be
	/// configured through <see cref="CowProtocolMessageAdapter.RpcEndpoint"/> for them.
	/// </remarks>
	protected override DataType[] SkippedDataTypes =>
	[
		DataType.Ticks,
		.. CowProtocolMessageAdapter.AllTimeFrames.Select(tf => tf.TimeFrame()),
	];
}
