namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.QuickSwap;
using StockSharp.QuickSwap.Native;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for QuickSwap.</summary>
[TestClass]
public class QuickSwapTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	// Swaps and candles require a configured subgraph; these checks use public RPC only.
	protected override DataType[] SkippedDataTypes => [DataType.Ticks, DataType.CandleTimeFrame];

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task PublicRpcReadsDoNotRequireAWallet(string walletAddress)
	{
		await using var server = new HistoryHttpServer(() =>
			"{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"0x89\"}", CancellationToken);
		using var client = new QuickSwapRpcClient(server.Address.ToString(), QuickSwapChains.Polygon, walletAddress, null);

		await client.VerifyChainAsync(CancellationToken);

		IsFalse(client.IsSigningAvailable);
		IsTrue(client.WalletAddress.IsEmpty());
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new QuickSwapMessageAdapter(new IncrementalIdGenerator())
		{
			RpcEndpoint = "https://polygon-bor-rpc.publicnode.com",
			Markets = "V3|0x0d500B1d8E8eF31E21C99d1Db9A6444d3ADf1270|0x2791Bca1f2de4661ED88A30C99A7a9449Aa84174",
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "WPOL-USDC",
		BoardCode = BoardCodes.QuickSwap,
	};
}
