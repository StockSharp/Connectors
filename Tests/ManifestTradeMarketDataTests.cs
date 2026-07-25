namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.ManifestTrade;
using StockSharp.Messages;

/// <summary>
/// ManifestTrade publishes market data without credentials.
/// </summary>
[TestClass]
public class ManifestTradeMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ManifestTradeMessageAdapter(new IncrementalIdGenerator())
	{
		// api.mainnet-beta.solana.com throttles getTransaction after about a dozen
		// calls, which is far less than one page of trade history needs
		RpcEndpoint = "https://solana-rpc.publicnode.com",
		StreamingEndpoint = "wss://solana-rpc.publicnode.com",

		// the SOL-USDC book only sees quote churn, so a history page of the newest
		// transactions holds no fill at all - USDT-USDC is the busiest Manifest market
		Markets = "8sjV1AqBFvFuADBCQHhotaRq5DFFYSjjg1jMyVWMqXvZ|USDT|USDC",
		MaximumDiscoveredMarkets = 0,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "USDT-USDC",
		BoardCode = BoardCodes.ManifestTrade,
	};
}
