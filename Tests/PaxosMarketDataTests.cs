namespace StockSharp.Connectors.Tests;

using System.Linq;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Paxos;

/// <summary>
/// Paxos publishes market data without credentials.
/// </summary>
[TestClass]
public class PaxosMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PaxosMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSD",
		BoardCode = BoardCodes.Paxos,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Historical candles are the only Paxos market data served to an authenticated
	/// session only, the anonymous request is answered with 401.
	/// </remarks>
	protected override DataType[] SkippedDataTypes
		=> [.. PaxosMessageAdapter.AllTimeFrames.Select(tf => tf.TimeFrame())];
}
