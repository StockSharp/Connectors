namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.PumpSwap;

/// <summary>
/// PumpSwap publishes market data without credentials.
/// </summary>
[TestClass]
public class PumpSwapMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PumpSwapMessageAdapter(new IncrementalIdGenerator())
	{
		// api.mainnet-beta.solana.com answers 429 to all but the first few getTransaction
		// calls, and the trade history reads one transaction per pool signature
		RpcEndpoint = "https://solana-rpc.publicnode.com",

		// the shipped default pool trades a few times a day, so a tick or candle request
		// covering the last hour finds nothing at all
		Pools = "DPzKoJVewaH1wpchD3gWKeeGm7G2mXkBW48uRniAgbVx",
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "CUPSEY-WSOL",
		BoardCode = BoardCodes.PumpSwap,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Trades are rebuilt from the pool transaction log, which costs one JSON-RPC round trip
	/// per inspected signature, so a history request takes well over half a minute.
	/// </remarks>
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(90);
}
