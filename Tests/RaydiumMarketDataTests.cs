namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Raydium;

/// <summary>
/// Raydium publishes market data without credentials.
/// </summary>
[TestClass]
public class RaydiumMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new RaydiumMessageAdapter(new IncrementalIdGenerator())
	{
		// api.mainnet-beta.solana.com answers 429 to all but the first few getTransaction
		// calls, and the trade history reads one transaction per pool signature
		RpcEndpoint = "https://solana-rpc.publicnode.com",

		// pin both WSOL/USDC pools instead of taking whatever tops the volume ranking today
		Pools = "58oQChx4yWmvKdwLLZzBi4ChoCc2fqCUWBkwMihLYQo2|WSOL|USDC;" +
			"3ucNos4NbumPLZNWztqGHNFFgkHeRMBQAVemeeomsUxv|WSOL|USDC",
		MaximumDiscoveredPools = 0,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "WSOL-USDC",
		BoardCode = BoardCodes.Raydium,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Trades are rebuilt from the pool transaction log, which costs one JSON-RPC round trip
	/// per inspected signature, so a history request takes well over half a minute.
	/// </remarks>
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(90);
}
