namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.PancakeSwap;
using StockSharp.PancakeSwap.Native;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for PancakeSwap.</summary>
[TestClass]
public class PancakeSwapTests : LiveMarketDataTestBase
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
			"{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"0x38\"}", CancellationToken);
		using var client = new PancakeSwapRpcClient(server.Address.ToString(), PancakeSwapChains.BnbSmartChain, walletAddress, null);

		await client.VerifyChainAsync(CancellationToken);

		IsFalse(client.IsSigningAvailable);
		IsTrue(client.WalletAddress.IsEmpty());
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PancakeSwapMessageAdapter(new IncrementalIdGenerator())
		{
			RpcEndpoint = "https://bsc-rpc.publicnode.com",
			Markets = "V3|0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c|0x55d398326f99059fF775485246999027B3197955|500",
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "WBNB-USDT",
		BoardCode = BoardCodes.PancakeSwap,
	};
}
