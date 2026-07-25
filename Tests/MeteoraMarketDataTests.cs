namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Meteora;

/// <summary>
/// Meteora publishes market data without credentials.
/// </summary>
[TestClass]
public class MeteoraMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new MeteoraMessageAdapter(new IncrementalIdGenerator())
	{
		// api.mainnet-beta.solana.com throttles getTransaction after ten calls,
		// which is far less than one page of trade history needs, and the bin-array
		// snapshot rules out the endpoints that cap getMultipleAccounts at ten keys
		RpcEndpoint = "https://rpc.magicblock.app/mainnet",
		StreamingEndpoint = "wss://rpc.magicblock.app/mainnet",

		// pin the pool instead of taking whatever tops the volume ranking today
		Pools = "5rCf1DM8LjKTw4YqhnoLcngyZYeNnQqztScTogYHAS6|SOL|USDC",
		MaximumDiscoveredPools = 0,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SOL-USDC",
		BoardCode = BoardCodes.Meteora,
	};
}
