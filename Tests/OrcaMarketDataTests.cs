namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Orca;

/// <summary>
/// Orca publishes market data without credentials.
/// </summary>
[TestClass]
public class OrcaMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new OrcaMessageAdapter(new IncrementalIdGenerator())
	{
		// api.mainnet-beta.solana.com throttles getTransaction after about a dozen
		// calls, which is far less than one page of trade history needs
		RpcEndpoint = "https://solana-rpc.publicnode.com",
		StreamingEndpoint = "wss://solana-rpc.publicnode.com",

		Pools = "Czfq3xZZDmsdGdUyrNLtRhGc47cXcZtLG4crryfu44zE|SOL|USDC",
		MaximumDiscoveredPools = 0,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SOL-USDC",
		BoardCode = BoardCodes.Orca,
	};
}
