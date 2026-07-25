namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Aerodrome;
using StockSharp.Messages;

/// <summary>
/// Aerodrome publishes market data without credentials.
/// </summary>
[TestClass]
public class AerodromeMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	/// <remarks>
	/// The default https://mainnet.base.org throttles a whole test session away, so the checks
	/// run against another public Base node instead of the venue's own default.
	/// </remarks>
	protected override MessageAdapter CreateAdapter() => new AerodromeMessageAdapter(new IncrementalIdGenerator())
	{
		RpcEndpoint = "https://base-rpc.publicnode.com",
		WebSocketEndpoint = "wss://base-rpc.publicnode.com",
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "WETH-USDC-VOLATILE",
		BoardCode = BoardCodes.Aerodrome,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Both are folded from past swap logs, which a pruning node answers with
	/// "pruned history unavailable" - they need an archive RPC endpoint.
	/// </remarks>
	protected override DataType[] SkippedDataTypes => [DataType.Ticks, DataType.CandleTimeFrame];
}
