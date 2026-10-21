namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Uniswap;
using StockSharp.Uniswap.Native;

/// <summary>Anonymous token reference reads through the Uniswap RPC client.</summary>
[TestClass]
public class UniswapTests : HistoryTestBase
{
	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task TokenMetadata()
	{
		// The adapter's quotes require a Trading API key; token metadata is public on-chain.
		using var client = new UniswapRpcClient("https://ethereum-rpc.publicnode.com", UniswapChains.Ethereum, null, null);
		await client.VerifyChainAsync(CancellationToken);
		var token = await client.GetTokenAsync("0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2", CancellationToken);

		AreEqual("WETH", token.Symbol);
		AreEqual(18, token.Decimals);
		IsFalse(token.Name.IsEmpty());
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task PublicRpcReadsDoNotRequireAWallet(string walletAddress)
	{
		await using var server = new HistoryHttpServer(() =>
			"{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"0x1\"}", CancellationToken);
		using var client = new UniswapRpcClient(server.Address.ToString(), UniswapChains.Ethereum, walletAddress, null);

		await client.VerifyChainAsync(CancellationToken);

		IsFalse(client.IsSigningAvailable);
		IsTrue(client.WalletAddress.IsEmpty());
	}
}
